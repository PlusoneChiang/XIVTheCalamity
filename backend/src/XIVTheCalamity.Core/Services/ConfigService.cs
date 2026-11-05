using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using XIVTheCalamity.Core.Json;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Core.Services;

/// <summary>
/// Configuration management service
/// </summary>
public class ConfigService
{
    private static readonly object _lock = new();
    private static readonly SemaphoreSlim _globalConfigLock = new(1, 1);
    private string ConfigPath => GetConfigFilePath();
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
        _platformProviders = platformProviders;
        
        // Ensure config directory exists
        var configDir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
    }

    /// <summary>
    /// Get config file path
    /// </summary>
    public string GetConfigPath() => ConfigPath;

    public async Task<GlobalConfig> LoadGlobalConfigAsync()
    {
        await _globalConfigLock.WaitAsync();
        try
        {
            var globalPath = GetGlobalConfigFilePath();
            if (File.Exists(globalPath))
            {
                var json = await File.ReadAllTextAsync(globalPath);
                var global = JsonSerializer.Deserialize<GlobalConfig>(json, _jsonOptions)
                    ?? throw new JsonException("全域設定內容無效。");
                ValidateGlobalConfig(global, validateGamePath: false);
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("enablePreRelease", out _))
                {
                    global.EnablePreRelease = (await ReadLegacyGlobalValuesAsync(PlatformPathService.Instance.GetConfigPath())).EnablePreRelease;
                    SaveGlobalConfigSync(global);
                }
                return global;
            }

            var migratedGlobal = await MigrateGlobalConfigAsync();
            SaveGlobalConfigSync(migratedGlobal);
            return migratedGlobal;
        }
        finally
        {
            _globalConfigLock.Release();
        }
    }

    public async Task SaveGlobalConfigAsync(GlobalConfig global)
    {
        ValidateGlobalConfig(global, validateGamePath: true);
        await _globalConfigLock.WaitAsync();
        try { SaveGlobalConfigSync(global); }
        finally { _globalConfigLock.Release(); }
    }

    /// <summary>
    /// Load configuration
    /// </summary>
    public async Task<AppConfig> LoadConfigAsync(string? profileName = null)
    {
        var configPath = GetConfigFilePath(profileName);
        bool configMissing;
        lock (_lock) configMissing = !File.Exists(configPath);
        if (configMissing)
        {
            Console.WriteLine($"[Config] Config file not found, creating default config at {configPath}");
            return await ApplyGlobalConfigAsync(CreateDefaultConfig());
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
            
            if (config is null)
            {
                Console.WriteLine("[Config] Failed to deserialize config, using default");
                return await ApplyGlobalConfigAsync(CreateDefaultConfig());
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
                    UseHomeAlias = config.Wine.UseHomeAlias,
                    LeftOptionIsAlt = config.Wine.LeftOptionIsAlt,
                    RightOptionIsAlt = config.Wine.RightOptionIsAlt,
                    LeftCommandIsCtrl = config.Wine.LeftCommandIsCtrl,
                    RightCommandIsCtrl = config.Wine.RightCommandIsCtrl
                };
            }

            config.DiscordRpc ??= new DiscordRpcConfig();
             
            // Force-overwrite managed fields that users must not change
            config.Dalamud.PluginRepoUrl = DalamudConfig.ManagedPluginRepoUrl;

            Console.WriteLine("[Config] Config loaded successfully");
            return await ApplyGlobalConfigAsync(config);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Config] Invalid JSON format: {ex.Message}");
            
            // Backup corrupted config
            var backupPath = $"{configPath}.backup";
            if (File.Exists(configPath))
            {
                File.Copy(configPath, backupPath, true);
                Console.WriteLine($"[Config] Backed up corrupted config to {backupPath}");
            }
            
            return await ApplyGlobalConfigAsync(CreateDefaultConfig());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to load config: {ex.Message}");
            return await ApplyGlobalConfigAsync(CreateDefaultConfig());
        }
    }

    /// <summary>
    /// Save configuration
    /// </summary>
    public async Task SaveConfigAsync(AppConfig config, string? profileName = null)
    {
        await ApplyGlobalConfigAsync(config);
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
                UseHomeAlias = config.WineCompat?.UseHomeAlias ?? false,
                LeftOptionIsAlt = config.WineCompat?.LeftOptionIsAlt ?? true,
                RightOptionIsAlt = config.WineCompat?.RightOptionIsAlt ?? true,
                LeftCommandIsCtrl = config.WineCompat?.LeftCommandIsCtrl ?? true,
                RightCommandIsCtrl = config.WineCompat?.RightCommandIsCtrl ?? true
            };
        }

        // Force-overwrite managed fields before validation and save
        config.Dalamud.PluginRepoUrl = DalamudConfig.ManagedPluginRepoUrl;

        ValidateConfig(config);
        
        lock (_lock)
        {
            SaveConfigSync(config, profileName);
        }
    }

    /// <summary>
    /// Save config synchronously (internal)
    /// </summary>
    private void SaveConfigSync(AppConfig config, string? profileName = null)
    {
        var configPath = GetConfigFilePath(profileName);
        try
        {
            var node = JsonSerializer.SerializeToNode(config, CoreJsonContext.Default.AppConfig)!.AsObject();
            node["game"]?.AsObject().Remove("gamePath");
            node["launcher"]?.AsObject().Remove("announcementMode");
            node["launcher"]?.AsObject().Remove("developmentMode");
            node["launcher"]?.AsObject().Remove("showDalamudTab");
            node["launcher"]?.AsObject().Remove("enablePreRelease");
            File.WriteAllText(configPath, node.ToJsonString(_jsonOptions));
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
        return await ApplyGlobalConfigAsync(defaultConfig);
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
    private static string GetConfigFilePath(string? profileName = null)
    {
        var platformPaths = PlatformPathService.Instance;
        if (string.IsNullOrEmpty(profileName))
        {
            return platformPaths.GetConfigPath("config.json");
        }
        return platformPaths.GetConfigPathForProfile(profileName, "config.json");
    }

    private static string GetGlobalConfigFilePath() => Path.Combine(PlatformPathService.Instance.AppDataDirectory, "global.json");

    private async Task<AppConfig> ApplyGlobalConfigAsync(AppConfig config)
    {
        var global = await LoadGlobalConfigAsync();
        config.Game.GamePath = global.GamePath;
        config.Launcher.AnnouncementMode = global.AnnouncementMode;
        config.Launcher.DevelopmentMode = global.DevelopmentMode;
        config.Launcher.ShowDalamudTab = global.ShowDalamudTab;
        config.Launcher.EnablePreRelease = global.EnablePreRelease;
        return config;
    }

    private async Task<GlobalConfig> MigrateGlobalConfigAsync()
    {
        var paths = PlatformPathService.Instance;
        var defaultLegacy = await ReadLegacyGlobalValuesAsync(paths.GetConfigPathForProfile("default"));
        var activeLegacy = await ReadLegacyGlobalValuesAsync(paths.GetConfigPath());
        return new GlobalConfig
        {
            GamePath = defaultLegacy.GamePath,
            ShowDalamudTab = defaultLegacy.ShowDalamudTab,
            AnnouncementMode = activeLegacy.AnnouncementMode,
            DevelopmentMode = activeLegacy.DevelopmentMode,
            EnablePreRelease = activeLegacy.EnablePreRelease
        };
    }

    private static async Task<GlobalConfig> ReadLegacyGlobalValuesAsync(string path)
    {
        if (!File.Exists(path)) return new GlobalConfig();
        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var launcher = root.TryGetProperty("launcher", out var launcherElement) ? launcherElement : default;
            var game = root.TryGetProperty("game", out var gameElement) ? gameElement : default;
            return new GlobalConfig
            {
                GamePath = game.ValueKind == JsonValueKind.Object && game.TryGetProperty("gamePath", out var gamePath) ? gamePath.GetString() ?? "" : "",
                ShowDalamudTab = launcher.ValueKind == JsonValueKind.Object && launcher.TryGetProperty("showDalamudTab", out var showTab) && showTab.ValueKind == JsonValueKind.True,
                AnnouncementMode = launcher.ValueKind == JsonValueKind.Object && launcher.TryGetProperty("announcementMode", out var mode) && mode.GetString() == "official" ? "official" : "local",
                DevelopmentMode = launcher.ValueKind == JsonValueKind.Object && launcher.TryGetProperty("developmentMode", out var development) && development.ValueKind == JsonValueKind.True,
                EnablePreRelease = launcher.ValueKind == JsonValueKind.Object && launcher.TryGetProperty("enablePreRelease", out var preRelease) && preRelease.ValueKind == JsonValueKind.True
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            File.Copy(path, path + ".backup", true);
            return new GlobalConfig();
        }
    }

    private static void ValidateGlobalConfig(GlobalConfig global, bool validateGamePath)
    {
        if (global.AnnouncementMode is not ("local" or "official"))
            throw new ArgumentException("公告模式必須是 local 或 official。");
        if (validateGamePath && !string.IsNullOrEmpty(global.GamePath) && !Directory.Exists(global.GamePath))
            throw new ArgumentException($"Game path does not exist: {global.GamePath}");
    }

    private static void SaveGlobalConfigSync(GlobalConfig global)
    {
        var path = GetGlobalConfigFilePath();
        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(global, _jsonOptions);
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }
}
