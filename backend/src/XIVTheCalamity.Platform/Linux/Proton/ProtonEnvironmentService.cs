using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.Linux.Proton;

/// <summary>
/// Linux Proton environment service
/// Implements IEnvironmentService interface for Proton GE
/// </summary>
public class ProtonEnvironmentService(
    ProtonDownloadService downloadService,
    ILogger<ProtonEnvironmentService>? logger = null
) : IEnvironmentService
{
    private readonly PlatformPathService _platformPaths = PlatformPathService.Instance;
    
    // Proton paths
    private string ProtonRoot => _platformPaths.GetEmulatorRootDirectory();
    private string ProtonBin => Path.Combine(ProtonRoot, "files", "bin");
    private string Wine => Path.Combine(ProtonBin, "wine");
    private string WineServer => Path.Combine(ProtonBin, "wineserver");
    private string WinePrefix => _platformPaths.GetWinePrefixPath();
    
    public async Task InitializeAsync(IProgress<EnvironmentInitProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[PROTON-ENV] ========== Starting Proton environment initialization ==========");
        
        try
        {
            // Step 1: Check if Proton is installed (5%)
            progress?.Report(new EnvironmentInitProgress
            {
                Stage = "check_proton",
                MessageKey = "progress.checking_proton",
                CompletedItems = 5,
                TotalItems = 100
            });
            
            var protonStatus = await downloadService.GetStatusAsync();
            logger?.LogInformation("[PROTON-ENV] Proton status: Installed={IsInstalled}", protonStatus.IsInstalled);
            
            // Step 2: Download Proton if not installed (10-50%)
            if (!protonStatus.IsInstalled)
            {
                logger?.LogInformation("[PROTON-ENV] Proton not found, starting download");
                
                progress?.Report(new EnvironmentInitProgress
                {
                    Stage = "download_proton",
                    MessageKey = "progress.downloading_proton",
                    CompletedItems = 10,
                    TotalItems = 100
                });
                
                var downloadProgress = new Progress<DownloadProgress>(p =>
                {
                    logger?.LogDebug("[PROTON-ENV] Download progress: Stage={Stage}, Percent={Percent:F1}%", 
                        p.Stage, p.Percentage);
                    
                    if (p.HasError)
                    {
                        progress?.Report(new EnvironmentInitProgress
                        {
                            Stage = "download_error",
                            MessageKey = p.MessageKey,
                            HasError = true,
                            ErrorMessage = p.ErrorMessage
                        });
                    }
                    else if (p.IsComplete)
                    {
                        progress?.Report(new EnvironmentInitProgress
                        {
                            Stage = "proton_downloaded",
                            MessageKey = "progress.proton_downloaded",
                            CompletedItems = 50,
                            TotalItems = 100
                        });
                    }
                    else
                    {
                        // Map download progress to overall progress
                        // Download: 0-100% → Overall: 10-50%
                        var downloadPercent = p.Percentage;
                        var overallPercent = 10 + (int)(downloadPercent * 40 / 100);
                        
                        progress?.Report(new EnvironmentInitProgress
                        {
                            Stage = p.Stage,
                            MessageKey = p.MessageKey,
                            CurrentFile = p.CurrentFile,
                            BytesDownloaded = p.BytesDownloaded,
                            TotalBytes = p.TotalBytes,
                            // Map download's 0-100% to overall 10-50% range
                            CompletedItems = overallPercent,
                            TotalItems = 100,
                            ExtraData = new Dictionary<string, object>
                            {
                                ["downloadedMB"] = p.BytesDownloaded / 1024.0 / 1024.0,
                                ["totalMB"] = p.TotalBytes / 1024.0 / 1024.0
                            }
                        });
                    }
                });
                
                await downloadService.DownloadAsync(progress: downloadProgress, cancellationToken: cancellationToken);
                logger?.LogInformation("[PROTON-ENV] Proton downloaded successfully");
            }
            else
            {
                logger?.LogInformation("[PROTON-ENV] Proton already installed at {Path}", protonStatus.InstallPath);
                progress?.Report(new EnvironmentInitProgress
                {
                    Stage = "proton_ready",
                    MessageKey = "progress.proton_ready",
                    CompletedItems = 50,
                    TotalItems = 100
                });
            }
            
            // Step 3: Initialize Wine Prefix (60-90%)
            progress?.Report(new EnvironmentInitProgress
            {
                Stage = "init_prefix",
                MessageKey = "progress.initializing_prefix",
                CompletedItems = 60,
                TotalItems = 100
            });
            
            await EnsurePrefixAsync(cancellationToken);
            
            progress?.Report(new EnvironmentInitProgress
            {
                Stage = "prefix_complete",
                MessageKey = "progress.prefix_initialized",
                CompletedItems = 90,
                TotalItems = 100
            });
            
            // Step 4: Complete (100%)
            progress?.Report(new EnvironmentInitProgress
            {
                Stage = "complete",
                MessageKey = "progress.complete",
                CompletedItems = 100,
                TotalItems = 100,
                IsComplete = true
            });
            
            logger?.LogInformation("[PROTON-ENV] ========== Initialization complete ==========");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[PROTON-ENV] ========== Initialization failed ==========");
            
            progress?.Report(new EnvironmentInitProgress
            {
                Stage = "error",
                MessageKey = "error.initialization_failed",
                HasError = true,
                ErrorMessage = ex.Message
            });
            
            throw;
        }
    }

    public async Task EnsurePrefixAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("[PROTON-ENV] EnsurePrefixAsync called");
        
        // Check if Proton is installed first
        var status = await downloadService.GetStatusAsync();
        if (!status.IsInstalled)
        {
            logger?.LogWarning("[PROTON-ENV] Proton not installed, cannot create prefix");
            throw new InvalidOperationException("Proton must be installed before creating Wine prefix. Call InitializeAsync() first.");
        }
        
        if (!Directory.Exists(WinePrefix))
        {
            logger?.LogInformation("[PROTON-ENV] Creating Wine prefix at: {Prefix}", WinePrefix);
            Directory.CreateDirectory(WinePrefix);
            
            // Run wineboot to initialize prefix
            var env = GetEnvironment();
            var startInfo = new ProcessStartInfo
            {
                FileName = Wine,
                Arguments = "wineboot -i",  // Initialize prefix
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var (key, value) in env)
            {
                startInfo.Environment[key] = value;
            }

            logger?.LogDebug("[PROTON-ENV] Running wineboot to initialize prefix: {Wine} wineboot -i", Wine);
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
                logger?.LogDebug("[PROTON-ENV] Wineboot exited with code: {ExitCode}", process.ExitCode);
            }
        }
        else
        {
            logger?.LogDebug("[PROTON-ENV] Wine prefix already exists: {Prefix}", WinePrefix);
        }
        
        // Always ensure DXVK and VKD3D DLLs are installed
        await InstallDxvkDllsAsync();
    }
    
    private async Task InstallDxvkDllsAsync()
    {
        var system32Path = Path.Combine(WinePrefix, "drive_c", "windows", "system32");
        Directory.CreateDirectory(system32Path);
        
        var protonLibPath = Path.Combine(ProtonRoot, "files", "lib");
        
        // DXVK DLLs to install
        var dxvkDlls = new[] { "d3d11.dll", "dxgi.dll", "d3d10core.dll", "d3d9.dll" };
        var dxvkSourceDir = Path.Combine(protonLibPath, "wine", "dxvk", "x86_64-windows");
        
        // VKD3D DLLs to install
        var vkd3dDlls = new[] { "libvkd3d-1.dll", "libvkd3d-shader-1.dll" };
        var vkd3dSourceDir = Path.Combine(protonLibPath, "vkd3d", "x86_64-windows");
        
        logger?.LogInformation("[PROTON-ENV] Installing DXVK and VKD3D DLLs to wineprefix");
        
        // Copy DXVK DLLs
        foreach (var dll in dxvkDlls)
        {
            var sourcePath = Path.Combine(dxvkSourceDir, dll);
            var destPath = Path.Combine(system32Path, dll);
            
            if (File.Exists(sourcePath))
            {
                // Delete existing file if it exists (to avoid permission issues)
                if (File.Exists(destPath))
                {
                    try
                    {
                        File.Delete(destPath);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning("[PROTON-ENV] Could not delete existing {Dll}: {Error}", dll, ex.Message);
                    }
                }
                
                File.Copy(sourcePath, destPath, overwrite: false);
                logger?.LogDebug("[PROTON-ENV] Installed {Dll}", dll);
            }
            else
            {
                logger?.LogWarning("[PROTON-ENV] DXVK DLL not found: {Path}", sourcePath);
            }
        }
        
        // Copy VKD3D DLLs
        foreach (var dll in vkd3dDlls)
        {
            var sourcePath = Path.Combine(vkd3dSourceDir, dll);
            var destPath = Path.Combine(system32Path, dll);
            
            if (File.Exists(sourcePath))
            {
                // Delete existing file if it exists (to avoid permission issues)
                if (File.Exists(destPath))
                {
                    try
                    {
                        File.Delete(destPath);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning("[PROTON-ENV] Could not delete existing {Dll}: {Error}", dll, ex.Message);
                    }
                }
                
                File.Copy(sourcePath, destPath, overwrite: false);
                logger?.LogDebug("[PROTON-ENV] Installed {Dll}", dll);
            }
            else
            {
                logger?.LogWarning("[PROTON-ENV] VKD3D DLL not found: {Path}", sourcePath);
            }
        }
        
        logger?.LogInformation("[PROTON-ENV] DXVK and VKD3D DLLs installed successfully");
        await Task.CompletedTask;
    }

    public string GetEmulatorDirectory()
    {
        return ProtonRoot;
    }

    public Dictionary<string, string> GetEnvironment()
    {
        // Use default configuration
        return GetEnvironmentWithConfig(new ProtonConfig());
    }
    
    /// <summary>
    /// Get environment variables with specific configuration
    /// </summary>
    public Dictionary<string, string> GetEnvironmentWithConfig(ProtonConfig config)
    {
        var protonLibPath = Path.Combine(ProtonRoot, "files", "lib");
        var protonLibLinux64 = Path.Combine(protonLibPath, "x86_64-linux-gnu");
        var protonLibLinux32 = Path.Combine(protonLibPath, "i386-linux-gnu");
        
        // Build WINEDLLPATH to include vkd3d, DXVK, and wine directories
        var wineDllPath = string.Join(":", new[]
        {
            Path.Combine(protonLibPath, "wine", "dxvk", "x86_64-windows"),      // DXVK DLLs (d3d11, dxgi, etc.)
            Path.Combine(protonLibPath, "vkd3d", "x86_64-windows"),            // VKD3D DLLs
            Path.Combine(protonLibPath, "wine", "x86_64-windows"),             // Wine DLLs
            Path.Combine(protonLibPath, "wine", "x86_64-unix"),                // Wine Unix libraries
        });
        
        var env = new Dictionary<string, string>
        {
            // Basic Wine environment
            ["WINEPREFIX"] = WinePrefix,
            ["WINEARCH"] = "win64",
            ["WINE"] = Wine,
            ["WINESERVER"] = WineServer,
            
            // Wine library paths - CRITICAL for finding vkd3d and DXVK
            ["WINEDLLPATH"] = wineDllPath,
            ["LD_LIBRARY_PATH"] = $"{protonLibLinux64}:{protonLibLinux32}",
            
            // DXVK configuration
            ["DXVK_HUD"] = config.DxvkHudEnabled ? "fps,devinfo,memory" : "0",
            ["DXVK_FRAME_RATE"] = config.MaxFramerate.ToString(),
            ["DXVK_STATE_CACHE_PATH"] = WinePrefix,
            ["DXVK_LOG_PATH"] = Path.Combine(WinePrefix, "dxvk_logs"),
            
            // VKD3D (DirectX 11/12 to Vulkan)
            ["VKD3D_CONFIG"] = "dxr",
            
            // Wine synchronization
            ["WINEFSYNC"] = config.FsyncEnabled ? "1" : "0",
            ["WINEESYNC"] = config.EsyncEnabled ? "1" : "0",
            
            // Wine debug
            ["WINEDEBUG"] = string.IsNullOrWhiteSpace(config.WineDebug) ? "-all" : config.WineDebug,
            
            // DLL overrides - Use DXVK for DirectX
            ["WINEDLLOVERRIDES"] = "mscoree,mshtml=;d3d11,dxgi,d3d10core,d3d9=n,b",
            
            // Proton compatibility
            ["PROTON_NO_ESYNC"] = config.EsyncEnabled ? "0" : "1",
            ["PROTON_NO_FSYNC"] = config.FsyncEnabled ? "0" : "1",
            
            // Enable DXVK
            ["PROTON_USE_WINED3D"] = "0",
        };
        
        logger?.LogDebug("[PROTON-ENV] Generated environment - DXVK_HUD={DxvkHud}, Fsync={Fsync}, Esync={Esync}, MaxFPS={MaxFps}",
            env["DXVK_HUD"], config.FsyncEnabled, config.EsyncEnabled, config.MaxFramerate);
        
        return env;
    }

    public async Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default)
    {
        // Use default configuration for interface method
        return await ExecuteWithConfigAsync(command, args, new ProtonConfig(), cancellationToken);
    }
    
    /// <summary>
    /// Execute command with specific Proton configuration
    /// </summary>
    public async Task<ProcessResult> ExecuteWithConfigAsync(string command, string[] args, ProtonConfig config, CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("[PROTON-ENV] Executing: {Command} {Args}", command, string.Join(" ", args));
        
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Use GameMode if enabled and available
        if (config.GameModeEnabled && IsGameModeAvailable())
        {
            logger?.LogInformation("[PROTON-ENV] GameMode enabled, using gamemoderun wrapper");
            startInfo.FileName = "gamemoderun";
            startInfo.ArgumentList.Add(Wine);
        }
        else
        {
            startInfo.FileName = Wine;
            if (config.GameModeEnabled)
            {
                logger?.LogWarning("[PROTON-ENV] GameMode requested but not available on system");
            }
        }

        startInfo.ArgumentList.Add(command);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Apply environment variables
        var env = GetEnvironmentWithConfig(config);
        foreach (var (key, value) in env)
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new Exception($"Failed to start Proton process: {command}");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }
    
    /// <summary>
    /// Check if GameMode is available on the system
    /// </summary>
    private bool IsGameModeAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "gamemoderun",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null) return false;
            
            process.WaitForExit();
            var available = process.ExitCode == 0;
            
            logger?.LogDebug("[PROTON-ENV] GameMode available: {Available}", available);
            return available;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[PROTON-ENV] Failed to check GameMode availability");
            return false;
        }
    }

    public async Task<bool> IsAvailableAsync()
    {
        var status = await downloadService.GetStatusAsync();
        logger?.LogDebug("[PROTON-ENV] Proton available: {IsInstalled}", status.IsInstalled);
        return status.IsInstalled;
    }

    public string GetDebugInfo()
    {
        return $"Proton Environment (Linux)\n" +
               $"Proton Root: {ProtonRoot}\n" +
               $"Proton Bin: {ProtonBin}\n" +
               $"Wine: {Wine}\n" +
               $"Wine Prefix: {WinePrefix}";
    }

    public Task ApplyConfigAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[PROTON-ENV] Proton configuration applied via environment variables (no registry changes needed)");
        // Proton configuration is applied through environment variables in GetEnvironment()
        // Unlike macOS Wine which requires registry modifications, Linux Proton handles everything via env vars
        return Task.CompletedTask;
    }

    public void StartAudioRouter(int gamePid, bool esyncEnabled, bool msyncEnabled)
    {
        logger?.LogDebug("[PROTON-ENV] Audio router not available on Linux");
    }
}
