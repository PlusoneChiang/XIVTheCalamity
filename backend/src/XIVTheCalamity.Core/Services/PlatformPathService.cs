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
    /// Get Proton installation directory (Linux only)
    /// This is where Proton GE is downloaded and stored
    /// </summary>
    public string GetProtonDirectory()
    {
        return Path.Combine(UserDataDirectory, "proton");
    }

    /// <summary>
    /// Get Wine/Proton emulator root directory
    /// macOS: Wine directory
    /// Linux: Proton GE directory
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
            return GetLinuxProtonDirectory();
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
    /// Get Linux Proton directory
    /// Priority: 1. User data directory (downloaded), 2. Dev environment (project root), 3. Resources (AppImage)
    /// </summary>
    private string GetLinuxProtonDirectory()
    {
        var appDir = AppContext.BaseDirectory;

        // Priority 1: User data directory (downloaded Proton)
        var userProtonPath = GetProtonDirectory();
        if (Directory.Exists(userProtonPath))
        {
            var protonDirs = Directory.GetDirectories(userProtonPath, "GE-Proton*");
            if (protonDirs.Length > 0)
            {
                return protonDirs[0];
            }
        }

        // Priority 2: Dev environment - search upward for proton-ge/ (for manual setup)
        var currentDir = new DirectoryInfo(appDir);
        while (currentDir != null)
        {
            var protonPath = Path.Combine(currentDir.FullName, "proton-ge");
            if (Directory.Exists(protonPath))
            {
                var protonDirs = Directory.GetDirectories(protonPath, "GE-Proton*");
                if (protonDirs.Length > 0)
                {
                    return protonDirs[0];
                }
            }
            currentDir = currentDir.Parent;
        }

        // Priority 3: Production environment (AppImage) - Resources directory
        var resourcesPath = Path.Combine(appDir, "..", "Resources", "proton");
        if (Directory.Exists(resourcesPath))
        {
            var protonDirs = Directory.GetDirectories(resourcesPath, "GE-Proton*");
            if (protonDirs.Length > 0)
            {
                return protonDirs[0];
            }
        }

        throw new DirectoryNotFoundException(
            $"Proton not found. Searched: {userProtonPath}, project root from {appDir}, Resources");
    }

    /// <summary>
    /// Get Wine/Proton executable path
    /// macOS: wine executable in Wine directory
    /// Linux: wine executable in Proton GE files/bin
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
            // Linux Proton: proton-ge/GE-ProtonXX-XX/files/bin/wine
            return Path.Combine(emulatorRoot, "files", "bin", "wine");
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
