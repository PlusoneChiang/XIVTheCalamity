using System.Runtime.InteropServices;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.MacOS.Wine;

/// <summary>
/// Wine path management service
/// Based on XoM Wine.swift
/// </summary>
public class WinePathService
{
    private static WinePathService? _instance;
    private static readonly object _lock = new();

    public string AppSupport { get; }
    public string WinePrefix { get; }
    public string WineRoot { get; }
    public string WineBin { get; }
    public string WineDll { get; }
    public string WineExecutable { get; }
    public string Wine { get; }
    public string Wineboot { get; }
    public string WineServer { get; }
    public string Winecfg { get; }
    public string Regedit { get; }
    public string RegExe { get; }
    
    public string PrefixDriveC { get; }
    public string PrefixWindows { get; }
    public string PrefixFonts { get; }
    public string PrefixSystem32 { get; }
    
    // Application paths
    public string FfxivConfigPath { get; }
    public string LogsPath { get; }
    public string DalamudLogsPath { get; }
    
    public string GstLib { get; }
    public string GstPlugin { get; }
    public string GstRegistry { get; }
    
    /// <summary>
    /// Font entries to install (primary TC + fallback SC)
    /// </summary>
    public (string File, string Name)[] Fonts { get; }

    private WinePathService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("Wine is only supported on macOS and Linux");
        }

        var homeDir = HomePathService.GetEffectiveHomePath();
        
        AppSupport = Path.Combine(homeDir, "Library", "Application Support", "XIVTheCalamity");
        WinePrefix = Path.Combine(AppSupport, "wineprefix");
        
        // Wine runtime directory search
        // Priority: AppData (downloaded) > Dev (project root wine/) > Bundle (Resources/wine/)
        var appDir = AppContext.BaseDirectory;
        
        // Strategy 1: AppData - downloaded Wine
        var appDataWine = Path.Combine(AppSupport, "wine");
        if (Directory.Exists(appDataWine) && Directory.Exists(Path.Combine(appDataWine, "bin")))
        {
            WineRoot = appDataWine;
        }
        
        // Strategy 2: Dev environment - search upward for wine/
        if (string.IsNullOrEmpty(WineRoot))
        {
            var currentDir = new DirectoryInfo(appDir);
            while (currentDir is not null)
            {
                var winePath = Path.Combine(currentDir.FullName, "wine");
                if (Directory.Exists(winePath) && Directory.Exists(Path.Combine(winePath, "bin")))
                {
                    WineRoot = winePath;
                    break;
                }
                currentDir = currentDir.Parent;
            }
        }
        
        // Strategy 3: Bundle environment (legacy)
        if (string.IsNullOrEmpty(WineRoot))
        {
            var bundleWinePath = Path.Combine(appDir, "..", "..", "Resources", "wine");
            bundleWinePath = Path.GetFullPath(bundleWinePath);
            if (Directory.Exists(bundleWinePath) && Directory.Exists(Path.Combine(bundleWinePath, "bin")))
            {
                WineRoot = bundleWinePath;
            }
        }
        
        // Fallback: use appData path (will be created by download service)
        if (string.IsNullOrEmpty(WineRoot))
        {
            WineRoot = appDataWine;
        }
        
        WineBin = Path.Combine(WineRoot, "bin");
        WineDll = Path.Combine(WineRoot, "lib", "wine");
        
        WineExecutable = Path.Combine(WineBin, "wine");
        Wine = Path.Combine(WineBin, "wine");
        Wineboot = Path.Combine(WineBin, "wineboot");
        WineServer = Path.Combine(WineBin, "wineserver");
        Winecfg = Path.Combine(WineBin, "winecfg");
        Regedit = Path.Combine(WineBin, "regedit");
        
        RegExe = @"C:\windows\system32\reg.exe";
        
        PrefixDriveC = Path.Combine(WinePrefix, "drive_c");
        PrefixWindows = Path.Combine(PrefixDriveC, "windows");
        PrefixFonts = Path.Combine(PrefixWindows, "Fonts");
        PrefixSystem32 = Path.Combine(PrefixWindows, "system32");
        
        // Application paths
        FfxivConfigPath = Path.Combine(AppSupport, "ffxivConfig");
        LogsPath = Path.Combine(AppSupport, "logs");
        DalamudLogsPath = Path.Combine(LogsPath, "Dalamud");
        
        GstLib = Path.Combine(WineRoot, "lib");
        GstPlugin = Path.Combine(GstLib, "gstreamer-1.0");
        GstRegistry = Path.Combine(AppSupport, "gstreamer-registry.bin");
        
        Fonts = new[]
        {
            ("NotoSansTC-Regular.ttf", "Noto Sans TC"),
            ("NotoSansSC-Regular.ttf", "Noto Sans SC"),
        };
    }

    /// <summary>
    /// Check if Wine is actually installed (binary exists)
    /// </summary>
    public bool IsWineInstalled => File.Exists(WineExecutable);

    /// <summary>
    /// Reset singleton to re-detect Wine path after download
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _instance = null;
        }
    }

    public static WinePathService Instance
    {
        get
        {
            if (_instance is null)
            {
                lock (_lock)
                {
                    _instance ??= new WinePathService();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Get Wine environment variables
    /// </summary>
    public Dictionary<string, string> GetEnvironment()
    {
        var env = new Dictionary<string, string>
        {
            ["WINEPREFIX"] = WinePrefix,
            ["WINEDLLPATH"] = WineDll,
            ["LANG"] = "en_US",
            
            // GStreamer config
            ["GST_PLUGIN_PATH"] = GstPlugin,
            ["GST_REGISTRY"] = GstRegistry,
            ["DYLD_FALLBACK_LIBRARY_PATH"] = GstLib,
            ["GST_PLUGIN_SYSTEM_PATH_1_0"] = "",
            ["GST_PLUGIN_SCANNER_1_0"] = "",
            ["GST_REGISTRY_FORK"] = "no",
            
            // MoltenVK config (required by DXVK)
            ["MVK_ALLOW_METAL_FENCES"] = "1",
            ["MVK_CONFIG_FULL_IMAGE_VIEW_SWIZZLE"] = "1",
            ["MVK_CONFIG_RESUME_LOST_DEVICE"] = "1",
            ["MVK_CONFIG_LOG_LEVEL"] = "mvk_error",
            
            // NOTE: DOTNET_EnableWriteXorExecute is NOT set here
            // It's only needed for Dalamud and is set in GameLaunchService when Dalamud is enabled
            // Setting it globally causes Wine processes to crash with exit code 136
        };

        return env;
    }
}
