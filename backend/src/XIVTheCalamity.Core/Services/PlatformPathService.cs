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

    private PlatformPathService()
    {
        CurrentPlatform = GetCurrentPlatform();
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS uses ~/Library/Application Support
            var appSupport = Path.Combine(homeDir, "Library", "Application Support", "XIVTheCalamity");
            AppDataDirectory = appSupport;
            UserDataDirectory = appSupport;
            CacheDirectory = Path.Combine(homeDir, "Library", "Caches", "XIVTheCalamity");
            LogsDirectory = Path.Combine(homeDir, "Library", "Logs", "XIVTheCalamity");
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
        else
        {
            throw new PlatformNotSupportedException(
                $"Unsupported platform: {RuntimeInformation.OSDescription}");
        }

        // Ensure directories exist
        EnsureDirectoriesExist();
    }

    /// <summary>
    /// Get config file path
    /// </summary>
    public string GetConfigPath(string filename = "config.json")
    {
        return Path.Combine(AppDataDirectory, filename);
    }

    /// <summary>
    /// Get Wine prefix path
    /// All Wine/Proton prefix data stored here
    /// </summary>
    public string GetWinePrefixPath()
    {
        return Path.Combine(UserDataDirectory, "wineprefix");
    }

    /// <summary>
    /// Get game installation directory (game subdirectory from configured path)
    /// Returns the 'game' subdirectory under the configured game path
    /// </summary>
    public string GetGameDirectory()
    {
        return Path.Combine(UserDataDirectory, "game");
    }

    /// <summary>
    /// Get Dalamud directory
    /// Includes Dalamud runtime, plugins, and assets
    /// </summary>
    public string GetDalamudDirectory()
    {
        return Path.Combine(UserDataDirectory, "Dalamud");  // 大写 D，保持向后兼容
    }

    /// <summary>
    /// Get Wine-XIV installation directory (Linux only)
    /// This is where Wine-XIV is downloaded and stored
    /// </summary>
    public string GetWineXIVDirectory()
    {
        return Path.Combine(UserDataDirectory, "wine");
    }

    /// <summary>
    /// Get Wine/Wine-XIV emulator root directory
    /// macOS: Wine directory
    /// Linux: Wine-XIV directory (~/.config/XIVTheCalamity/wine/)
    /// Windows: Empty (native execution)
    /// </summary>
    public string GetEmulatorRootDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetMacOSWineDirectory();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return GetLinuxWineXIVDirectory();
        }

        throw new PlatformNotSupportedException(
            $"Emulator not supported on platform: {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    /// Get macOS Wine directory
    /// Priority: 1. Dev environment (project root), 2. Resources (production)
    /// </summary>
    private string GetMacOSWineDirectory()
    {
        var appDir = AppContext.BaseDirectory;
        var currentDir = new DirectoryInfo(appDir);

        // Priority 1: Dev environment - search upward for wine/
        while (currentDir != null)
        {
            var winePath = Path.Combine(currentDir.FullName, "wine");
            if (Directory.Exists(winePath) && Directory.Exists(Path.Combine(winePath, "bin")))
            {
                return winePath;
            }
            currentDir = currentDir.Parent;
        }

        // Priority 2: Production environment - Resources directory
        var resourcesPath = Path.Combine(appDir, "..", "Resources", "wine");
        if (Directory.Exists(resourcesPath))
        {
            return resourcesPath;
        }

        throw new DirectoryNotFoundException(
            $"Wine not found. Searched from: {appDir}");
    }

    /// <summary>
    /// Get Linux Wine-XIV directory
    /// Returns: ~/.config/XIVTheCalamity/wine/
    /// </summary>
    private string GetLinuxWineXIVDirectory()
    {
        // Wine-XIV is always downloaded to user config directory
        var wineRoot = Path.Combine(UserDataDirectory, "wine");
        return wineRoot;
    }

    /// <summary>
    /// Get Wine/Wine-XIV executable path
    /// macOS: wine executable in Wine directory
    /// Linux: wine64 executable in Wine-XIV bin directory
    /// </summary>
    public string GetWineExecutable()
    {
        var emulatorRoot = GetEmulatorRootDirectory();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: wine/bin/wine64
            return Path.Combine(emulatorRoot, "bin", "wine64");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux Wine-XIV: wine/bin/wine64
            return Path.Combine(emulatorRoot, "bin", "wine64");
        }

        throw new PlatformNotSupportedException();
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
    }

    /// <summary>
    /// Check if running on macOS
    /// </summary>
    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Check if running on Linux
    /// </summary>
    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// Get platform-specific information for debugging
    /// </summary>
    public string GetDebugInfo()
    {
        return $@"Platform Path Service Debug Info:
Platform: {RuntimeInformation.OSDescription}
Architecture: {RuntimeInformation.OSArchitecture}
AppDataDirectory: {AppDataDirectory}
UserDataDirectory: {UserDataDirectory}
CacheDirectory: {CacheDirectory}
LogsDirectory: {LogsDirectory}
Emulator Root: {GetEmulatorRootDirectory()}
Wine Executable: {GetWineExecutable()}
Base Directory: {AppContext.BaseDirectory}";
    }
}
