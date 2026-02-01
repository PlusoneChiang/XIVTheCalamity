using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.Linux.Wine;

/// <summary>
/// Wine-XIV environment service for Linux
/// Simpler than Proton - Wine-XIV handles most configuration internally
/// </summary>
public class WineXIVEnvironmentService(
    WineXIVDownloadService downloadService,
    ConfigService configService,
    ILogger<WineXIVEnvironmentService>? logger = null
) : IEnvironmentService
{
    private readonly PlatformPathService _platformPaths = PlatformPathService.Instance;
    
    // Wine paths
    private string WineRoot => _platformPaths.GetEmulatorRootDirectory();
    private string WineBin => Path.Combine(WineRoot, "bin");
    private string Wine => Path.Combine(WineBin, "wine64");
    private string WineServer => Path.Combine(WineBin, "wineserver");
    private string WinePrefix => _platformPaths.GetWinePrefixPath();
    
    public async IAsyncEnumerable<EnvironmentProgressEvent> InitializeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WINE-XIV] Starting Wine-XIV environment initialization");
        
        // Collect events to avoid yield in try-catch
        var events = new List<EnvironmentProgressEvent>();
        EnvironmentProgressEvent? finalEvent = null;
        
        try
        {
            // Step 1: Check if Wine is installed (5%)
            events.Add(new EnvironmentProgressEvent
            {
                Stage = "check_wine",
                MessageKey = "progress.checking_wine",
                CompletedItems = 5,
                TotalItems = 100
            });
            
            var wineStatus = await downloadService.GetStatusAsync();
            logger?.LogInformation("[WINE-XIV] Wine status: Installed={IsInstalled}", wineStatus.IsInstalled);
            
            // Step 2: Download Wine if not installed (10-50%)
            if (!wineStatus.IsInstalled)
            {
                logger?.LogInformation("[WINE-XIV] Wine not found, starting download");
                
                events.Add(new EnvironmentProgressEvent
                {
                    Stage = "download_wine",
                    MessageKey = "progress.downloading_wine",
                    CompletedItems = 10,
                    TotalItems = 100
                });
                
                // Note: Wine download still uses old Progress<T> pattern
                // Will be refactored in Phase 4
                var downloadProgress = new Progress<DownloadProgress>(p =>
                {
                    logger?.LogDebug("[WINE-XIV] Download progress: Stage={Stage}, Percent={Percent:F1}%", 
                        p.Stage, p.Percentage);
                });
                
                var success = await downloadService.DownloadAsync(downloadProgress, cancellationToken);
                if (!success)
                {
                    throw new Exception("Failed to download Wine-XIV");
                }
                
                events.Add(new EnvironmentProgressEvent
                {
                    Stage = "wine_downloaded",
                    MessageKey = "progress.wine_downloaded",
                    CompletedItems = 50,
                    TotalItems = 100
                });
            }
            
            // Step 3: Initialize Wine prefix (50-80%)
            events.Add(new EnvironmentProgressEvent
            {
                Stage = "init_prefix",
                MessageKey = "progress.init_wine_prefix",
                CompletedItems = 50,
                TotalItems = 100
            });
            
            await EnsurePrefixAsync(cancellationToken);
            
            // Step 4: Install required DLLs (80-100%)
            events.Add(new EnvironmentProgressEvent
            {
                Stage = "install_dlls",
                MessageKey = "progress.installing_dlls",
                CompletedItems = 80,
                TotalItems = 100
            });
            
            await InstallRequiredDllsAsync();
            
            // Complete
            finalEvent = new EnvironmentProgressEvent
            {
                Stage = "complete",
                MessageKey = "progress.environment_ready",
                CompletedItems = 100,
                TotalItems = 100,
                IsComplete = true
            };
            
            logger?.LogInformation("[WINE-XIV] Wine-XIV environment initialization complete");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WINE-XIV] Initialization failed");
            
            finalEvent = new EnvironmentProgressEvent
            {
                Stage = "error",
                MessageKey = "error.wine_init_failed",
                HasError = true,
                ErrorMessage = ex.Message
            };
        }
        
        // Yield all collected events
        foreach (var evt in events)
        {
            yield return evt;
        }
        
        if (finalEvent != null)
        {
            yield return finalEvent;
        }
    }
    
    public async Task EnsurePrefixAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(WinePrefix))
        {
            logger?.LogInformation("[WINE-XIV] Creating Wine prefix: {Prefix}", WinePrefix);
            
            var env = GetEnvironment();
            
            var psi = new ProcessStartInfo
            {
                FileName = Wine,
                Arguments = "wineboot -i",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            foreach (var (key, value) in env)
            {
                psi.Environment[key] = value;
            }
            
            logger?.LogDebug("[WINE-XIV] Running wineboot to initialize prefix");
            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
                logger?.LogDebug("[WINE-XIV] Wineboot exited with code: {ExitCode}", process.ExitCode);
            }
        }
        else
        {
            logger?.LogDebug("[WINE-XIV] Wine prefix already exists: {Prefix}", WinePrefix);
        }
    }
    
    private async Task InstallRequiredDllsAsync()
    {
        // Wine-XIV includes DXVK internally, but we need to copy to system32 for Dalamud
        var system32Path = Path.Combine(WinePrefix, "drive_c", "windows", "system32");
        Directory.CreateDirectory(system32Path);
        
        var wineLibPath = Path.Combine(WineRoot, "lib64", "wine");
        var wineDxvkPath = Path.Combine(wineLibPath, "x86_64-windows");
        
        // DXVK DLLs
        var dxvkDlls = new[] { "d3d11.dll", "dxgi.dll", "d3d10core.dll", "d3d9.dll" };
        
        logger?.LogInformation("[WINE-XIV] Installing DXVK DLLs to wineprefix");
        
        foreach (var dll in dxvkDlls)
        {
            var sourcePath = Path.Combine(wineDxvkPath, dll);
            var destPath = Path.Combine(system32Path, dll);
            
            if (File.Exists(sourcePath))
            {
                // Delete existing file if it exists
                if (File.Exists(destPath))
                {
                    try
                    {
                        File.Delete(destPath);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning("[WINE-XIV] Could not delete existing {Dll}: {Error}", dll, ex.Message);
                    }
                }
                
                File.Copy(sourcePath, destPath, overwrite: false);
                logger?.LogDebug("[WINE-XIV] Installed {Dll}", dll);
            }
            else
            {
                logger?.LogWarning("[WINE-XIV] DLL not found: {Path}", sourcePath);
            }
        }
        
        logger?.LogInformation("[WINE-XIV] DLLs installed successfully");
        await Task.CompletedTask;
    }
    
    public string GetEmulatorDirectory()
    {
        return WineRoot;
    }
    
    public Dictionary<string, string> GetEnvironment()
    {
        // Load current config (synchronously - GetEnvironment must be sync)
        var config = configService.LoadConfigAsync().GetAwaiter().GetResult();
        var wineXIVConfig = config.WineXIV ?? new WineXIVConfig();
        
        var wineLibPath = Path.Combine(WineRoot, "lib64", "wine");
        var wineDllPath = Path.Combine(wineLibPath, "x86_64-windows");
        
        var env = new Dictionary<string, string>
        {
            // Basic Wine environment
            ["WINEPREFIX"] = WinePrefix,
            
            // Wine library paths
            ["WINEDLLPATH"] = wineDllPath,
            ["LD_LIBRARY_PATH"] = $"{Path.Combine(WineRoot, "lib64")}:{wineLibPath}/x86_64-unix",
            
            // DLL overrides - CRITICAL: Keep d3d11,dxgi,d3d10core,d3d9=n,b for FFXIV
            // Different from XIVLauncher.Core to ensure DXGI fallback works
            ["WINEDLLOVERRIDES"] = "mshtml=;d3d11,dxgi,d3d10core,d3d9=n,b",
            
            // Wine synchronization - configured from WineXIVConfig
            ["WINEESYNC"] = wineXIVConfig.EsyncEnabled ? "1" : "0",
            ["WINEFSYNC"] = wineXIVConfig.FsyncEnabled ? "1" : "0",
            
            // DXVK configuration - configured from WineXIVConfig
            ["DXVK_HUD"] = wineXIVConfig.DxvkHudEnabled ? "fps,frametime,memory" : "0",
            ["DXVK_ASYNC"] = "0",  // Always disabled for stability
            
            // Wine debug - configured from WineXIVConfig
            ["WINEDEBUG"] = string.IsNullOrEmpty(wineXIVConfig.WineDebug) ? "-all" : wineXIVConfig.WineDebug,
            
            // XIVLauncher marker
            ["XL_WINEONLINUX"] = "true",
        };
        
        // GameMode support (Linux only)
        if (wineXIVConfig.GameModeEnabled)
        {
            env["LD_PRELOAD"] = "/usr/lib/libgamemodeauto.so.0";
            logger?.LogDebug("[WINE-XIV] GameMode enabled");
        }
        
        logger?.LogDebug("[WINE-XIV] Generated environment with config: Esync={Esync}, Fsync={Fsync}, DXVK HUD={DxvkHud}, GameMode={GameMode}", 
            wineXIVConfig.EsyncEnabled, wineXIVConfig.FsyncEnabled, wineXIVConfig.DxvkHudEnabled, wineXIVConfig.GameModeEnabled);
        
        return env;
    }
    
    public async Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("[WINE-XIV] Executing: {Command} {Args}", command, string.Join(" ", args));
        
        var startInfo = new ProcessStartInfo
        {
            FileName = Wine,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        
        startInfo.ArgumentList.Add(command);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        
        var env = GetEnvironment();
        foreach (var (key, value) in env)
        {
            startInfo.Environment[key] = value;
        }
        
        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new Exception("Failed to start Wine process");
        }
        
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        
        return new ProcessResult(process.ExitCode, output, error);
    }
    
    public Task ApplyConfigAsync(CancellationToken cancellationToken = default)
    {
        // Wine-XIV doesn't need config application like Proton
        // Configuration is done through environment variables
        logger?.LogDebug("[WINE-XIV] ApplyConfigAsync called (no-op for Wine-XIV)");
        return Task.CompletedTask;
    }
    
    public void StartAudioRouter(int gamePid, bool esync, bool msync)
    {
        // Audio routing is not needed on Linux
        logger?.LogDebug("[WINE-XIV] StartAudioRouter called (no-op for Linux)");
    }
    
    public Task<bool> IsAvailableAsync()
    {
        var isAvailable = File.Exists(Wine);
        return Task.FromResult(isAvailable);
    }
    
    public string GetDebugInfo()
    {
        return $"Wine-XIV Environment:\n" +
               $"  Wine Root: {WineRoot}\n" +
               $"  Wine Prefix: {WinePrefix}\n" +
               $"  Wine Executable: {Wine}\n" +
               $"  Installed: {File.Exists(Wine)}";
    }
}
