using System.Runtime.InteropServices;

namespace XIVTheCalamity.Core.Services;

/// <summary>
/// Cross-platform path management service
/// Provides consistent path resolution for macOS and Linux
/// </summary>
public class PlatformPathService
{
    private static PlatformPathService? _instance;
    private static readonly object _lock = new();

    public static PlatformPathService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new PlatformPathService();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Application data directory (for config, saves, etc.)
    /// macOS: ~/Library/Application Support/XIVTheCalamity
    /// Linux: ~/.config/XIVTheCalamity
    /// </summary>
    public string AppDataDirectory { get; }

    /// <summary>
    /// User data directory (for game files, cache, etc.)
    /// macOS: ~/Library/Application Support/XIVTheCalamity
    /// Linux: ~/.config/XIVTheCalamity
    /// </summary>
    public string UserDataDirectory { get; }

    /// <summary>
    /// Cache directory
    /// macOS: ~/Library/Caches/XIVTheCalamity
    /// Linux: ~/.config/XIVTheCalamity/cache
    /// </summary>
    public string CacheDirectory { get; }

    /// <summary>
    /// Logs directory
    /// macOS: ~/Library/Logs/XIVTheCalamity
    /// Linux: ~/.config/XIVTheCalamity/logs
    /// </summary>
    public string LogsDirectory { get; }

    /// <summary>
    /// Current operating system
    /// </summary>
    public OSPlatform CurrentPlatform { get; }

    private string _activeProfile = "default";

    /// <summary>
    /// Currently active profile name
    /// </summary>
    public string ActiveProfile => _activeProfile;

    private PlatformPathService()
    {
        CurrentPlatform = GetCurrentPlatform();
        var homeDir = HomePathService.GetEffectiveHomePath();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS uses ~/Library/Application Support
            var appSupport = Path.Combine(homeDir, "Library", "Application Support", "XIVTheCalamity");
            AppDataDirectory = appSupport;
            UserDataDirectory = appSupport;
            CacheDirectory = Path.Combine(homeDir, "Library", "Caches", "XIVTheCalamity");
            LogsDirectory = Path.Combine(appSupport, "logs");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux: Use ~/.config (matches Electron's app.getPath('appData'))
            var baseDir = Path.Combine(homeDir, ".config", "XIVTheCalamity");
            
            AppDataDirectory = baseDir;
            UserDataDirectory = baseDir;
            CacheDirectory = Path.Combine(baseDir, "cache");
            LogsDirectory = Path.Combine(baseDir, "logs");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: Use %APPDATA% (Roaming) to match Electron's app.getPath('appData')
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var baseDir = Path.Combine(appData, "XIVTheCalamity");
            
            AppDataDirectory = baseDir;
            UserDataDirectory = baseDir;
            CacheDirectory = Path.Combine(baseDir, "cache");
            LogsDirectory = Path.Combine(baseDir, "logs");
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"Unsupported platform: {RuntimeInformation.OSDescription}");
        }

        // Load active profile from file
        LoadActiveProfile();

        // Ensure directories exist
        EnsureDirectoriesExist();
    }

    private void LoadActiveProfile()
    {
        var activeFile = Path.Combine(AppDataDirectory, "active_profile.txt");
        if (File.Exists(activeFile))
        {
            var name = File.ReadAllText(activeFile).Trim();
            if (!string.IsNullOrEmpty(name))
            {
                _activeProfile = name;
            }
        }
    }

    /// <summary>
    /// Switch active profile and write it to persistent storage
    /// </summary>
    public void SwitchProfile(string profileName)
    {
        _activeProfile = string.IsNullOrWhiteSpace(profileName) ? "default" : profileName.Trim();
        var activeFile = Path.Combine(AppDataDirectory, "active_profile.txt");
        File.WriteAllText(activeFile, _activeProfile);

        // Ensure directories exist for the new profile
        EnsureDirectoriesExist();
    }

    /// <summary>
    /// Get config file path
    /// </summary>
    public string GetConfigPath(string filename = "config.json")
    {
        return _activeProfile == "default"
            ? Path.Combine(AppDataDirectory, filename)
            : Path.Combine(AppDataDirectory, "profiles", _activeProfile, filename);
    }

    /// <summary>
    /// Get config file path for a specific profile
    /// </summary>
    public string GetConfigPathForProfile(string profileName, string filename = "config.json")
    {
        return profileName == "default"
            ? Path.Combine(AppDataDirectory, filename)
            : Path.Combine(AppDataDirectory, "profiles", profileName, filename);
    }

    /// <summary>
    /// Get Wine prefix path
    /// All Wine/Wine-XIV prefix data stored here
    /// </summary>
    public string GetWinePrefixPath()
    {
        return Path.Combine(UserDataDirectory, "wineprefix");
    }

    /// <summary>
    /// Get Dalamud directory
    /// Includes Dalamud runtime, plugins, and assets
    /// </summary>
    public string GetDalamudDirectory()
    {
        return _activeProfile == "default"
            ? Path.Combine(UserDataDirectory, "Dalamud")
            : Path.Combine(UserDataDirectory, "profiles", _activeProfile, "Dalamud");
    }

    /// <summary>
    /// Get FFXIV config directory
    /// </summary>
    public string GetFfxivConfigDirectory()
    {
        return _activeProfile == "default"
            ? Path.Combine(AppDataDirectory, "ffxivConfig")
            : Path.Combine(AppDataDirectory, "profiles", _activeProfile, "ffxivConfig");
    }

    /// <summary>
    /// Get macOS Wine directory
    /// Priority: 1. AppData (downloaded), 2. Dev environment (project root), 3. Resources (production)
    /// Returns null if Wine is not found (will be downloaded on first launch)
    /// </summary>
    private string GetMacOSWineDirectory()
    {
        var appDir = AppContext.BaseDirectory;
        
        // Priority 1: AppData - downloaded Wine
        var appDataWine = Path.Combine(AppDataDirectory, "wine");
        if (Directory.Exists(appDataWine) && Directory.Exists(Path.Combine(appDataWine, "bin")))
        {
            return appDataWine;
        }
        
        // Priority 2: Dev environment - search upward for wine/
        var currentDir = new DirectoryInfo(appDir);
        while (currentDir != null)
        {
            var winePath = Path.Combine(currentDir.FullName, "wine");
            if (Directory.Exists(winePath) && Directory.Exists(Path.Combine(winePath, "bin")))
            {
                return winePath;
            }
            currentDir = currentDir.Parent;
        }

        // Priority 3: Production environment - Resources directory (legacy bundle)
        var resourcesPath = Path.Combine(appDir, "..", "Resources", "wine");
        if (Directory.Exists(resourcesPath))
        {
            return resourcesPath;
        }

        // Wine not found - return expected appData path (will be created by download)
        return appDataWine;
    }

    private OSPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return OSPlatform.OSX;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return OSPlatform.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return OSPlatform.Windows;

        throw new PlatformNotSupportedException();
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(UserDataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);

        if (_activeProfile != "default")
        {
            Directory.CreateDirectory(Path.Combine(AppDataDirectory, "profiles", _activeProfile));
            Directory.CreateDirectory(GetFfxivConfigDirectory());
            Directory.CreateDirectory(GetDalamudDirectory());
        }
    }

    /// <summary>
    /// Check if running on macOS
    /// </summary>
    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Check if running on Linux
    /// </summary>
    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

}
