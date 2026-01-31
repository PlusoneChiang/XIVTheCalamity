using System.Runtime.InteropServices;

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
    
    public string FontFile { get; }
    public string FontName { get; }

    private WinePathService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("Wine is only supported on macOS and Linux");
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        AppSupport = Path.Combine(homeDir, "Library", "Application Support", "XIVTheCalamity");
        WinePrefix = Path.Combine(AppSupport, "wineprefix");
        
        // Wine runtime directory
        // Dev: project root wine/
        // Bundle: XIVTheCalamity.app/Contents/Resources/wine/
        var appDir = AppContext.BaseDirectory;
        
        // Strategy 1: Dev environment - search upward for wine/
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
        
        // Strategy 2: Bundle environment
        if (string.IsNullOrEmpty(WineRoot))
        {
            var bundleWinePath = Path.Combine(appDir, "..", "..", "Resources", "wine");
            bundleWinePath = Path.GetFullPath(bundleWinePath);
            if (Directory.Exists(bundleWinePath) && Directory.Exists(Path.Combine(bundleWinePath, "bin")))
            {
                WineRoot = bundleWinePath;
            }
        }
        
        if (string.IsNullOrEmpty(WineRoot) || !Directory.Exists(WineRoot))
        {
            throw new DirectoryNotFoundException($"Wine directory not found. Searched from: {appDir}");
        }
        
        WineBin = Path.Combine(WineRoot, "bin");
        WineDll = Path.Combine(WineRoot, "lib", "wine");
        
        WineExecutable = Path.Combine(WineBin, "wine64");
        Wine = Path.Combine(WineBin, "wine64");
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
        GstRegistry = Path.Combine(WinePrefix, "gstreamer-registry.bin");
        
        FontFile = "NotoSansTC-Regular.ttf";
        FontName = "Noto Sans TC";
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
            
            // .NET 7+ Apple Silicon support
            ["DOTNET_EnableWriteXorExecute"] = "0",
        };

        return env;
    }

    /// <summary>
    /// Get DXMT/DXVK environment variables
    /// </summary>
    public Dictionary<string, string> GetDxmtEnvironment(WineSettings settings)
    {
        var env = new Dictionary<string, string>();

        // DXMT config
        if (settings.DxmtEnabled)
        {
            env["XL_DXMT_ENABLED"] = "1";
            env["MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS"] = "1";
            env["DXMT_CONFIG"] = $"d3d11.metalSpatialUpscaleFactor={settings.MetalFxSpatialFactor};d3d11.preferredMaxFrameRate={settings.MaxFramerate};";
            env["DXMT_METALFX_SPATIAL_SWAPCHAIN"] = settings.MetalFxSpatialEnabled ? "1" : "0";
        }
        else
        {
            env["XL_DXMT_ENABLED"] = "0";
        }

        // DXVK config
        env["DXVK_HUD"] = settings.DxvkHud ?? "";
        env["DXVK_ASYNC"] = settings.DxvkAsync ? "1" : "0";
        env["DXVK_FRAME_RATE"] = settings.MaxFramerate.ToString();
        env["DXVK_CONFIG_FILE"] = @"C:\dxvk.conf";
        env["DXVK_STATE_CACHE_PATH"] = @"C:\";
        env["DXVK_LOG_PATH"] = @"C:\";

        // MSYNC
        env["WINEMSYNC"] = settings.Msync ? "1" : "0";

        // Metal 3 performance overlay
        env["MTL_HUD_ENABLED"] = settings.Metal3PerformanceOverlay ? "1" : "0";

        return env;
    }
}

/// <summary>
/// Wine settings
/// </summary>
public class WineSettings
{
    public bool DxmtEnabled { get; set; } = true;
    public int MetalFxSpatialFactor { get; set; } = 2;
    public int MaxFramerate { get; set; } = 60;
    public bool MetalFxSpatialEnabled { get; set; } = true;
    public string? DxvkHud { get; set; }
    public bool DxvkAsync { get; set; } = true;
    public bool Msync { get; set; } = true;
    public bool Metal3PerformanceOverlay { get; set; } = false;
}
