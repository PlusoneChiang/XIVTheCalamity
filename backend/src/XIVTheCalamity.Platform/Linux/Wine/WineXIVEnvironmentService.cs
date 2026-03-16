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
    DxvkDownloadService dxvkDownloadService,
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
    
    /// <summary>
    /// Detect the Wine lib directory name (lib64 on Fedora/RHEL, lib on Arch/Ubuntu)
    /// </summary>
    private string WineLibDirName
    {
        get
        {
            if (Directory.Exists(Path.Combine(WineRoot, "lib64", "wine")))
                return "lib64";
            if (Directory.Exists(Path.Combine(WineRoot, "lib", "wine")))
                return "lib";
            return "lib64"; // fallback
        }
    }
    
    public async IAsyncEnumerable<EnvironmentProgressEvent> InitializeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WINE-XIV] Starting Wine-XIV environment initialization");
        
        // Step 1: Check if Wine is installed (5%)
        yield return new EnvironmentProgressEvent
        {
            Stage = "check_wine",
            MessageKey = "progress.checking_wine",
            CompletedItems = 5,
            TotalItems = 100
        };
        
        var wineStatus = await downloadService.GetStatusAsync();
        logger?.LogInformation("[WINE-XIV] Wine status: Installed={IsInstalled}", wineStatus.IsInstalled);
        
        // Step 2: Download Wine if not installed (5-65%)
        var needsDownload = !wineStatus.IsInstalled;
        if (needsDownload)
        {
            logger?.LogInformation("[WINE-XIV] Wine not found, starting download");
            
            // Forward all download progress events
            var downloadFailed = false;
            string? downloadError = null;
            
            await foreach (var downloadProgress in downloadService.DownloadAsync(cancellationToken))
            {
                // Map download progress (0-100%) to environment progress (5-65%)
                var mappedPercentage = 5 + (int)(downloadProgress.Percentage * 0.6);
                
                yield return new EnvironmentProgressEvent
                {
                    Stage = downloadProgress.Stage,
                    MessageKey = downloadProgress.MessageKey,
                    CompletedItems = mappedPercentage,
                    TotalItems = 100
                };
                
                if (downloadProgress.HasError)
                {
                    downloadFailed = true;
                    downloadError = downloadProgress.ErrorMessage;
                    break;
                }
            }
            
            if (downloadFailed)
            {
                yield return new EnvironmentProgressEvent
                {
                    Stage = "error",
                    MessageKey = "error.wine_download_failed",
                    HasError = true,
                    ErrorMessage = downloadError ?? "Wine download failed"
                };
                yield break;
            }
            
            logger?.LogInformation("[WINE-XIV] Wine downloaded successfully");
        }
        
        // Step 3: Initialize Wine prefix
        // Dynamic range: if download occurred, prefix gets 70-85%; otherwise 10-70%
        var prefixStart = needsDownload ? 70 : 10;
        var prefixEnd = needsDownload ? 85 : 70;
        
        yield return new EnvironmentProgressEvent
        {
            Stage = "init_prefix",
            MessageKey = "progress.init_wine_prefix",
            CompletedItems = prefixStart,
            TotalItems = 100
        };
        
        await EnsurePrefixAsync(cancellationToken);
        
        yield return new EnvironmentProgressEvent
        {
            Stage = "init_prefix",
            MessageKey = "progress.init_wine_prefix",
            CompletedItems = prefixEnd,
            TotalItems = 100
        };
        
        // Step 4: Download DXVK if needed
        var dxvkStart = needsDownload ? 85 : 75;
        var dxvkRange = needsDownload ? 7 : 15;
        
        yield return new EnvironmentProgressEvent
        {
            Stage = "download_dxvk",
            MessageKey = "progress.checking_dxvk",
            CompletedItems = dxvkStart,
            TotalItems = 100
        };
        
        await foreach (var dxvkProgress in dxvkDownloadService.EnsureDxvkAsync(cancellationToken))
        {
            if (dxvkProgress.HasError)
            {
                logger?.LogWarning("[WINE-XIV] DXVK download failed: {Error}", dxvkProgress.ErrorMessage);
                // Non-fatal: game can fall back to WineD3D
                break;
            }
            
            var mappedPercentage = dxvkStart + (int)(dxvkProgress.Percentage * dxvkRange / 100.0);
            yield return new EnvironmentProgressEvent
            {
                Stage = dxvkProgress.Stage,
                MessageKey = dxvkProgress.MessageKey,
                CompletedItems = mappedPercentage,
                TotalItems = 100
            };
        }
        
        // Step 5: Install DXVK DLLs to wineprefix
        var dllsStart = dxvkStart + dxvkRange;
        yield return new EnvironmentProgressEvent
        {
            Stage = "install_dlls",
            MessageKey = "progress.installing_dlls",
            CompletedItems = dllsStart,
            TotalItems = 100
        };
        
        InstallDxvkToPrefix();
        
        // Complete
        yield return new EnvironmentProgressEvent
        {
            Stage = "complete",
            MessageKey = "progress.environment_ready",
            CompletedItems = 100,
            TotalItems = 100,
            IsComplete = true
        };
        
        logger?.LogInformation("[WINE-XIV] Wine-XIV environment initialization complete");
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
    
    private void InstallDxvkToPrefix()
    {
        logger?.LogInformation("[WINE-XIV] Installing DXVK DLLs to wineprefix");
        dxvkDownloadService.InstallToPrefix(WinePrefix);
        logger?.LogInformation("[WINE-XIV] DXVK DLLs installed successfully");
    }
    
    public string GetEmulatorDirectory()
    {
        return WineRoot;
    }

    public string GetWineExecutablePath()
    {
        return Wine;
    }
    
    public Dictionary<string, string> GetEnvironment()
    {
        // Load current config (synchronously - GetEnvironment must be sync)
        var config = configService.LoadConfigAsync().GetAwaiter().GetResult();
        var wineXIVConfig = config.WineXIV ?? new WineXIVConfig();
        
        var wineLibPath = Path.Combine(WineRoot, WineLibDirName, "wine");
        var wineDllPath = Path.Combine(wineLibPath, "x86_64-windows");
        
        var env = new Dictionary<string, string>
        {
            // Basic Wine environment
            ["WINEPREFIX"] = WinePrefix,
            
            // Wine library paths
            ["WINEDLLPATH"] = wineDllPath,
            ["LD_LIBRARY_PATH"] = $"{Path.Combine(WineRoot, WineLibDirName)}:{wineLibPath}/x86_64-unix",
            
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
