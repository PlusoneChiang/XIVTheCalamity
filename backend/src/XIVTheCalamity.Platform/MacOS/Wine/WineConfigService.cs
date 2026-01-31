using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Platform.MacOS.Wine;

/// <summary>
/// Wine configuration service
/// </summary>
public class WineConfigService
{
    private readonly WinePathService _paths;
    private readonly ILogger<WineConfigService>? _logger;

    public WineConfigService(ILogger<WineConfigService>? logger = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException("Wine is only supported on macOS and Linux");
        }

        _paths = WinePathService.Instance;
        _logger = logger;
    }

    /// <summary>
    /// Launch Wine tool (winecfg, regedit, cmd, etc.)
    /// </summary>
    /// <param name="toolName">Tool name (e.g., "winecfg", "regedit", "cmd")</param>
    /// <param name="wineConfig">Optional Wine configuration for environment variables</param>
    /// <returns>Process ID if successful, null if failed</returns>
    public async Task<int?> LaunchToolAsync(string toolName, WineConfig? wineConfig = null, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Launching Wine tool: {ToolName}", toolName);

        try
        {
            var env = _paths.GetEnvironment();
            
            // 應用 Wine 配置到環境變數
            if (wineConfig != null)
            {
                ApplyWineConfigToEnvironment(env, wineConfig);
            }

            // 設定 Wine log 檔案路徑 (同一天集中一個檔案)
            var logDir = GetWineLogDirectory();
            var dateStamp = DateTime.Now.ToString("yyyyMMdd");
            var wineLogPath = Path.Combine(logDir, $"wine-{toolName}-{dateStamp}.log");
            
            _logger?.LogInformation("Wine tool log will be written to: {LogPath}", wineLogPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _paths.WineExecutable,
                Arguments = toolName,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var (key, value) in env)
            {
                startInfo.Environment[key] = value;
            }

            _logger?.LogInformation("Wine environment variables:");
            foreach (var (key, value) in env)
            {
                if (key.Contains("WINE") || key.Contains("DXMT") || key.Contains("DXVK") || key.Contains("MTL"))
                {
                    _logger?.LogInformation("  {Key}={Value}", key, value);
                }
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                _logger?.LogError("Failed to start {ToolName}", toolName);
                return null;
            }

            // 非同步寫入 log 檔案
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var logFile = new StreamWriter(wineLogPath, append: true);
                    await logFile.WriteLineAsync($"=== Wine Tool: {toolName} ===");
                    await logFile.WriteLineAsync($"=== Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    await logFile.WriteLineAsync($"=== PID: {process.Id} ===");
                    await logFile.WriteLineAsync();

                    // 寫入環境變數
                    await logFile.WriteLineAsync("=== Environment Variables ===");
                    foreach (var (key, value) in env)
                    {
                        if (key.Contains("WINE") || key.Contains("DXMT") || key.Contains("DXVK") || key.Contains("MTL"))
                        {
                            await logFile.WriteLineAsync($"{key}={value}");
                        }
                    }
                    await logFile.WriteLineAsync();

                    // 讀取並寫入 stdout
                    var stdoutTask = Task.Run(async () =>
                    {
                        while (!process.StandardOutput.EndOfStream)
                        {
                            var line = await process.StandardOutput.ReadLineAsync();
                            if (line != null)
                            {
                                await logFile.WriteLineAsync($"[OUT] {line}");
                                await logFile.FlushAsync();
                            }
                        }
                    });

                    // 讀取並寫入 stderr
                    var stderrTask = Task.Run(async () =>
                    {
                        while (!process.StandardError.EndOfStream)
                        {
                            var line = await process.StandardError.ReadLineAsync();
                            if (line != null)
                            {
                                await logFile.WriteLineAsync($"[ERR] {line}");
                                await logFile.FlushAsync();
                            }
                        }
                    });

                    await Task.WhenAll(stdoutTask, stderrTask);
                    
                    await logFile.WriteLineAsync();
                    await logFile.WriteLineAsync($"=== Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to write Wine log for {ToolName}", toolName);
                }
            });

            _logger?.LogInformation("{ToolName} launched with PID {ProcessId}", toolName, process.Id);
            await Task.CompletedTask;
            return process.Id;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to launch {ToolName}", toolName);
            return null;
        }
    }

    /// <summary>
    /// Get Wine log directory
    /// </summary>
    private string GetWineLogDirectory()
    {
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            appSupport = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            appSupport = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }
        
        var logDir = Path.Combine(appSupport, "XIVTheCalamity", "logs");
        Directory.CreateDirectory(logDir);
        
        return logDir;
    }

    /// <summary>
    /// Apply WineConfig to environment variables
    /// <summary>
    /// Apply Wine configuration to environment variables dictionary
    /// </summary>
    public void ApplyWineConfigToEnvironment(Dictionary<string, string> env, WineConfig config)
    {
        // Wine Debug - default to "-all" if empty
        env["WINEDEBUG"] = string.IsNullOrEmpty(config.WineDebug) ? "-all" : config.WineDebug;
        _logger?.LogDebug("Setting WINEDEBUG={WineDebug}", env["WINEDEBUG"]);
        
        // Esync
        if (config.EsyncEnabled)
        {
            env["WINEESYNC"] = "1";
        }
        
        // Msync
        if (config.Msync)
        {
            env["WINEMSYNC"] = "1";
        }
        
        // DXMT configuration
        if (config.DxmtEnabled)
        {
            env["XL_DXMT_ENABLED"] = "1";
            env["MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS"] = "1";
            env["DXMT_CONFIG"] = $"d3d11.metalSpatialUpscaleFactor={config.MetalFxSpatialFactor};d3d11.preferredMaxFrameRate={config.MaxFramerate};";
            env["DXMT_METALFX_SPATIAL_SWAPCHAIN"] = config.MetalFxSpatialEnabled ? "1" : "0";
        }
        else
        {
            env["XL_DXMT_ENABLED"] = "0";
        }
        
        // Metal HUD
        if (config.Metal3PerformanceOverlay)
        {
            env["MTL_HUD_ENABLED"] = "1";
        }
        
        // Native Resolution: true = use retina mode (high res), false = use scaling
        if (config.NativeResolution)
        {
            env["WINE_RETINA_MODE"] = "1";
        }
        
        // DLL Overrides - select based on DXMT setting
        var dxgiOverride = config.DxmtEnabled ? "n" : "b";  // native for DXMT, builtin for DXVK
        env["WINEDLLOVERRIDES"] = $"msquic=,mscoree=n,b;d3d9,d3d10core=n;d3d11=n;dxgi={dxgiOverride}";
    }
    
    /// <summary>
    /// Get full Wine environment variables (base + configuration)
    /// </summary>
    /// <param name="config">Wine configuration</param>
    /// <param name="dalamudRuntimePath">Dalamud Runtime path (macOS only, if Dalamud is enabled)</param>
    public Dictionary<string, string> GetFullEnvironment(WineConfig config, string? dalamudRuntimePath = null)
    {
        var env = _paths.GetEnvironment();
        ApplyWineConfigToEnvironment(env, config);
        
        // On macOS, Dalamud Runtime path needs to be set during Wine environment initialization
        // Reference XoM UnixDalamudRunner.cs - path needs to be converted to Wine path format
        // Because Dalamud.Boot runs in Wine environment, it needs Windows path format to load DLLs
        if (!string.IsNullOrEmpty(dalamudRuntimePath))
        {
            var winePath = ConvertToWinePath(dalamudRuntimePath);
            env["DALAMUD_RUNTIME"] = winePath;
            env["DOTNET_ROOT"] = winePath;
        }
        
        return env;
    }

    /// <summary>
    /// Check if Wine is available
    /// </summary>
    public bool IsWineAvailable()
    {
        return File.Exists(_paths.Wine) && File.Exists(_paths.Winecfg);
    }
    
    /// <summary>
    /// Convert Unix path to Wine path format
    /// </summary>
    /// <param name="unixPath">Unix path (e.g. /Users/...)</param>
    /// <returns>Wine path (e.g. Z:\Users\...)</returns>
    private static string ConvertToWinePath(string unixPath)
    {
        if (string.IsNullOrEmpty(unixPath))
            return unixPath;
            
        // Wine maps macOS root directory to Z: drive
        return "Z:" + unixPath.Replace("/", "\\");
    }
}
