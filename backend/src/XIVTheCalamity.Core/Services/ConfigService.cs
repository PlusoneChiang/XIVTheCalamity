using System.Runtime.InteropServices;
using System.Text.Json;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Core.Services;

/// <summary>
/// Configuration management service
/// </summary>
public class ConfigService
{
    private static readonly object _lock = new();
    private readonly string _configPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConfigService()
    {
        _configPath = GetConfigFilePath();
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
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
            
            // Migrate deprecated VerboseLogging to DevelopmentMode
            #pragma warning disable CS0618
            if (config.Launcher.VerboseLogging && !config.Launcher.DevelopmentMode)
            {
                config.Launcher.DevelopmentMode = true;
                Console.WriteLine("[Config] Migrated VerboseLogging to DevelopmentMode");
            }
            #pragma warning restore CS0618
            
            // Ensure platform-specific config exists with defaults
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && config.Wine == null)
            {
                config.Wine = new WineConfig
                {
                    DxmtEnabled = Environment.OSVersion.Version.Major >= 14,
                    MetalFxSpatialEnabled = false,
                    MetalFxSpatialFactor = 2.0,
                    Metal3PerformanceOverlay = false,
                    HudScale = 1.0,
                    NativeResolution = false,
                    MaxFramerate = 60,
                    AudioRouting = false,
                    EsyncEnabled = true,
                    Msync = true,
                    WineDebug = ""
                };
                Console.WriteLine("[Config] Initialized Wine config with defaults");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && config.Proton == null)
            {
                config.Proton = new ProtonConfig
                {
                    DxvkHudEnabled = false,
                    FsyncEnabled = true,
                    EsyncEnabled = true,
                    GameModeEnabled = true,
                    MaxFramerate = 60,
                    WineDebug = ""
                };
                Console.WriteLine("[Config] Initialized Proton config with defaults");
            }
            
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
    public async Task SaveConfigAsync(AppConfig config)
    {
        ValidateConfig(config);
        
        lock (_lock)
        {
            SaveConfigSync(config);
        }
        
        await Task.CompletedTask;
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
    /// Create default configuration
    /// </summary>
    private AppConfig CreateDefaultConfig()
    {
        var config = new AppConfig
        {
            Game = new GameConfig
            {
                GamePath = "",
                Region = "TraditionalChinese"
            },
            Dalamud = new DalamudConfig
            {
                Enabled = false,
                InjectDelay = 5000,
                SafeMode = false,
                PluginRepoUrl = "https://kamori.goats.dev/Dalamud/Release/Meta"
            },
            Launcher = new LauncherConfig
            {
                EncryptedArguments = true,
                ExitWithGame = true,
                NonZeroExitError = true,
                DevelopmentMode = false
            }
        };

        // Platform-specific defaults
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            config.Wine = new WineConfig
            {
                DxmtEnabled = Environment.OSVersion.Version.Major >= 14, // macOS 14.0+
                MetalFxSpatialEnabled = false,
                MetalFxSpatialFactor = 2.0,
                Metal3PerformanceOverlay = false,
                HudScale = 1.0,
                NativeResolution = false,
                MaxFramerate = 60,
                AudioRouting = false,
                EsyncEnabled = true,
                Msync = true,
                WineDebug = ""
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            config.Proton = new ProtonConfig
            {
                DxvkHudEnabled = false,
                FsyncEnabled = true,
                EsyncEnabled = true,
                GameModeEnabled = true,  // Default enabled
                MaxFramerate = 60,
                WineDebug = ""
            };
        }

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

        // Validate Wine config (macOS only)
        if (config.Wine != null)
        {
            // Validate metalFxSpatialFactor
            if (config.Wine.MetalFxSpatialFactor < 1.0 || config.Wine.MetalFxSpatialFactor > 4.0)
            {
                throw new ArgumentException("MetalFxSpatialFactor must be between 1.0 and 4.0");
            }

            // Validate maxFramerate
            if (config.Wine.MaxFramerate < 30 || config.Wine.MaxFramerate > 240)
            {
                throw new ArgumentException("MaxFramerate must be between 30 and 240");
            }
        }

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
