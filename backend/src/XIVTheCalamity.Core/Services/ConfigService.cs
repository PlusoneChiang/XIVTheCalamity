using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using XIVTheCalamity.Core.Json;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Core.Services;

/// <summary>
/// Configuration management service
/// </summary>
public class ConfigService
{
    private static readonly object _lock = new();
    private readonly string _configPath;
    private readonly IEnumerable<IPlatformConfigProvider> _platformProviders;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
            CoreJsonContext.Default,
            new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        ),
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ConfigService() : this(new IPlatformConfigProvider[] { new MacOSConfigProvider(), new LinuxConfigProvider() })
    {
    }

    public ConfigService(IEnumerable<IPlatformConfigProvider> platformProviders)
    {
        _configPath = GetConfigFilePath();
        _platformProviders = platformProviders;
        
        // Ensure config directory exists
        var configDir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
    }

    /// <summary>
    /// Get config file path
    /// </summary>
    public string GetConfigPath() => _configPath;

    /// <summary>
    /// Load configuration
    /// </summary>
    public async Task<AppConfig> LoadConfigAsync()
    {
        lock (_lock)
        {
            if (!File.Exists(_configPath))
            {
                Console.WriteLine($"[Config] Config file not found, creating default config at {_configPath}");
                var defaultConfig = CreateDefaultConfig();
                SaveConfigSync(defaultConfig);
                return defaultConfig;
            }
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
            
            if (config is null)
            {
                Console.WriteLine("[Config] Failed to deserialize config, using default");
                return CreateDefaultConfig();
            }
            
            // Apply platform strategy default values (e.g. initial creation of sub-config objects)
            var provider = _platformProviders.FirstOrDefault(p => p.MatchesPlatform());
            provider?.ApplyPlatformDefaults(config);
            
            // Always sync Wine configuration fields to split config fields if not already populated
            if (config.Wine != null)
            {
                config.WineGraphics ??= new WineGraphicsConfig
                {
                    MetalFxSpatialEnabled = config.Wine.MetalFxSpatialEnabled,
                    MetalFxSpatialFactor = config.Wine.MetalFxSpatialFactor,
                    Metal3PerformanceOverlay = config.Wine.Metal3PerformanceOverlay,
                    HudScale = config.Wine.HudScale,
                    NativeResolution = config.Wine.NativeResolution,
                    MaxFramerate = config.Wine.MaxFramerate
                };
                config.WinePerformance ??= new WinePerformanceConfig
                {
                    Msync = config.Wine.Msync,
                    WineDebug = config.Wine.WineDebug
                };
                config.WineCompat ??= new WineCompatConfig
                {
                    AudioRouting = config.Wine.AudioRouting,
                    UseHomeAlias = config.Wine.UseHomeAlias,
                    LeftOptionIsAlt = config.Wine.LeftOptionIsAlt,
                    RightOptionIsAlt = config.Wine.RightOptionIsAlt,
                    LeftCommandIsCtrl = config.Wine.LeftCommandIsCtrl,
                    RightCommandIsCtrl = config.Wine.RightCommandIsCtrl,
                    ImeCandidatePositionX = config.Wine.ImeCandidatePositionX,
                    ImeCandidatePositionY = config.Wine.ImeCandidatePositionY
                };
            }

            config.DiscordRpc ??= new DiscordRpcConfig();
             
            // Force-overwrite managed fields that users must not change
            config.Dalamud.PluginRepoUrl = DalamudConfig.ManagedPluginRepoUrl;

            Console.WriteLine("[Config] Config loaded successfully");
            return config;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Config] Invalid JSON format: {ex.Message}");
            
            // Backup corrupted config
            var backupPath = $"{_configPath}.backup";
            if (File.Exists(_configPath))
            {
                File.Copy(_configPath, backupPath, true);
                Console.WriteLine($"[Config] Backed up corrupted config to {backupPath}");
            }
            
            return CreateDefaultConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to load config: {ex.Message}");
            return CreateDefaultConfig();
        }
    }

    /// <summary>
    /// Save configuration
    /// </summary>
    public Task SaveConfigAsync(AppConfig config)
    {
        // Sync split config properties back to the main Wine wrapper object before save and validation
        if (config.WineGraphics != null || config.WinePerformance != null || config.WineCompat != null)
        {
            config.Wine = new WineConfig
            {
                // Graphics
                MetalFxSpatialEnabled = config.WineGraphics?.MetalFxSpatialEnabled ?? false,
                MetalFxSpatialFactor = config.WineGraphics?.MetalFxSpatialFactor ?? 2.0,
                Metal3PerformanceOverlay = config.WineGraphics?.Metal3PerformanceOverlay ?? false,
                HudScale = config.WineGraphics?.HudScale ?? 1.0,
                NativeResolution = config.WineGraphics?.NativeResolution ?? false,
                MaxFramerate = config.WineGraphics?.MaxFramerate ?? 60,
                
                // Performance
                Msync = config.WinePerformance?.Msync ?? true,
                WineDebug = config.WinePerformance?.WineDebug ?? "",
                
                // Compat
                AudioRouting = config.WineCompat?.AudioRouting ?? false,
                UseHomeAlias = config.WineCompat?.UseHomeAlias ?? false,
                LeftOptionIsAlt = config.WineCompat?.LeftOptionIsAlt ?? true,
                RightOptionIsAlt = config.WineCompat?.RightOptionIsAlt ?? true,
                LeftCommandIsCtrl = config.WineCompat?.LeftCommandIsCtrl ?? true,
                RightCommandIsCtrl = config.WineCompat?.RightCommandIsCtrl ?? true,
                ImeCandidatePositionX = config.WineCompat?.ImeCandidatePositionX ?? 25,
                ImeCandidatePositionY = config.WineCompat?.ImeCandidatePositionY ?? 85
            };
        }

        // Force-overwrite managed fields before validation and save
        config.Dalamud.PluginRepoUrl = DalamudConfig.ManagedPluginRepoUrl;

        ValidateConfig(config);
        
        lock (_lock)
        {
            SaveConfigSync(config);
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Save config synchronously (internal)
    /// </summary>
    private void SaveConfigSync(AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(_configPath, json);
            Console.WriteLine("[Config] Config saved successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to save config: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Reset to default configuration
    /// </summary>
    public async Task<AppConfig> ResetToDefaultAsync()
    {
        Console.WriteLine("[Config] Resetting config to default");
        var defaultConfig = CreateDefaultConfig();
        await SaveConfigAsync(defaultConfig);
        return defaultConfig;
    }

    /// <summary>
    /// Create default configuration using static factory and platform provider defaults
    /// </summary>
    private AppConfig CreateDefaultConfig()
    {
        var config = AppConfig.CreateDefault();

        // Apply platform-specific strategy defaults
        var provider = _platformProviders.FirstOrDefault(p => p.MatchesPlatform());
        provider?.ApplyPlatformDefaults(config);

        return config;
    }

    /// <summary>
    /// Validate configuration
    /// </summary>
    private void ValidateConfig(AppConfig config)
    {
        // Validate gamePath if not empty
        if (!string.IsNullOrEmpty(config.Game.GamePath) && !Directory.Exists(config.Game.GamePath))
        {
            throw new ArgumentException($"Game path does not exist: {config.Game.GamePath}");
        }

        // Validate region
        if (config.Game.Region != "TraditionalChinese")
        {
            throw new ArgumentException("Region must be 'TraditionalChinese'");
        }

        // Delegate platform config validation to strategy provider
        var provider = _platformProviders.FirstOrDefault(p => p.MatchesPlatform());
        provider?.ValidatePlatformConfig(config);

        // Validate injectDelay (milliseconds)
        if (config.Dalamud.InjectDelay < 0 || config.Dalamud.InjectDelay > 30000)
        {
            throw new ArgumentException("InjectDelay must be between 0 and 30000 milliseconds");
        }

        // Validate pluginRepoUrl
        if (!string.IsNullOrWhiteSpace(config.Dalamud.PluginRepoUrl) && 
            !Uri.TryCreate(config.Dalamud.PluginRepoUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("PluginRepoUrl must be a valid URL");
        }
    }

    /// <summary>
    /// Get config file path
    /// </summary>
    private static string GetConfigFilePath()
    {
        var platformPaths = PlatformPathService.Instance;
        return platformPaths.GetConfigPath("config.json");
    }
}
