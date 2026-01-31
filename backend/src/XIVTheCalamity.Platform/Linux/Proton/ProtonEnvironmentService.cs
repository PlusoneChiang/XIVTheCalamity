using System.Diagnostics;
using Microsoft.Extensions.Logging;
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
                Percent = 5
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
                    Percent = 10
                });
                
                var downloadProgress = new Progress<DownloadProgress>(p =>
                {
                    logger?.LogDebug("[PROTON-ENV] Download progress: Stage={Stage}, Percent={Percent}%", 
                        p.Stage, p.Percent);
                    
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
                            Percent = 50
                        });
                    }
                    else
                    {
                        // Map download percent (0-100) to overall progress (10-50)
                        var overallPercent = 10 + (p.Percent * 40 / 100);
                        
                        progress?.Report(new EnvironmentInitProgress
                        {
                            Stage = p.Stage,
                            MessageKey = p.MessageKey,
                            Percent = overallPercent,
                            ExtraData = new Dictionary<string, object>
                            {
                                ["downloadedBytes"] = p.DownloadedBytes,
                                ["totalBytes"] = p.TotalBytes,
                                ["downloadedMB"] = p.DownloadedBytes / 1024.0 / 1024.0,
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
                    Percent = 50
                });
            }
            
            // Step 3: Initialize Wine Prefix (60-90%)
            progress?.Report(new EnvironmentInitProgress
            {
                Stage = "init_prefix",
                MessageKey = "progress.initializing_prefix",
                Percent = 60
            });
            
            await EnsurePrefixAsync(cancellationToken);
            
            progress?.Report(new EnvironmentInitProgress
            {
                Stage = "prefix_complete",
                MessageKey = "progress.prefix_initialized",
                Percent = 90
            });
            
            // Step 4: Complete (100%)
            progress?.Report(new EnvironmentInitProgress
            {
                Stage = "complete",
                MessageKey = "progress.complete",
                Percent = 100,
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
    }

    public string GetEmulatorDirectory()
    {
        return ProtonRoot;
    }

    public Dictionary<string, string> GetEnvironment()
    {
        return new Dictionary<string, string>
        {
            ["WINEPREFIX"] = WinePrefix,
            ["WINEARCH"] = "win64",
            ["WINE"] = Wine,
            ["WINESERVER"] = WineServer,
            ["WINEDLLOVERRIDES"] = "mscoree,mshtml=",
            ["WINEDEBUG"] = "-all"
        };
    }

    public async Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("[PROTON-ENV] Executing: {Command} {Args}", command, string.Join(" ", args));
        
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
        if (process is null)
        {
            throw new Exception($"Failed to start Proton process: {command}");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, stdout, stderr);
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
        logger?.LogInformation("[PROTON-ENV] Linux Proton configuration applied via environment variables");
        // Proton 配置通過環境變數應用，不需要像 Wine 那樣寫註冊表
        return Task.CompletedTask;
    }

    public void StartAudioRouter(int gamePid, bool esyncEnabled, bool msyncEnabled)
    {
        logger?.LogDebug("[PROTON-ENV] Audio router not available on Linux");
    }
}
