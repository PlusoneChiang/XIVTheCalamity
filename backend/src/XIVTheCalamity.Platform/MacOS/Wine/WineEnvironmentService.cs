using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform.MacOS.Audio;

namespace XIVTheCalamity.Platform.MacOS.Wine;

/// <summary>
/// Wine environment service for macOS
/// Implements IEnvironmentService interface
/// </summary>
public class WineEnvironmentService(
    ConfigService configService,
    WineMacOSDownloadService downloadService,
    AudioRouterService? audioRouterService = null,
    ILogger<WineEnvironmentService>? logger = null
) : IEnvironmentService
{
    private WinePathService _paths = WinePathService.Instance;
    private readonly WinePrefixService _prefixService = new();

    public async IAsyncEnumerable<EnvironmentProgressEvent> InitializeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WINE-ENV] Starting Wine environment initialization");
        
        yield return new EnvironmentProgressEvent
        {
            Stage = "checking",
            MessageKey = "progress.checking_wine",
            Percentage = 5
        };
        
        // Step 1: Check if Wine is installed, download if needed
        // If download needed: download=5-65%, prefix init=70-100%
        // If already installed: prefix init=10-100%
        var needsDownload = !downloadService.IsInstalled();
        
        if (needsDownload)
        {
            logger?.LogInformation("[WINE-ENV] Wine not found, starting download");
            
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
            
            // Reset path service to pick up newly downloaded Wine
            WinePathService.Reset();
            _paths = WinePathService.Instance;
            
            logger?.LogInformation("[WINE-ENV] Wine downloaded successfully to: {Path}", _paths.WineRoot);
        }
        
        // Step 2: Initialize Wine prefix
        // Prefix stages map to different ranges depending on whether download occurred
        var prefixStart = needsDownload ? 70 : 10;
        var prefixRange = needsDownload ? 30 : 90; // remaining percentage for prefix init
        
        await foreach (var wineProgress in _prefixService.InitializePrefixAsyncEnumerable(needsDownload, cancellationToken))
        {
            // Map prefix stage (0-100) to remaining percentage range
            var stagePercent = wineProgress.Stage switch
            {
                WineInitStage.Checking => 0,
                WineInitStage.CreatingPrefix => 15,
                WineInitStage.InstallingFonts => 40,
                WineInitStage.SettingLocale => 60,
                WineInitStage.ConfiguringMedia => 80,
                WineInitStage.Complete => 100,
                _ => 0
            };
            
            var percent = wineProgress.IsComplete 
                ? 100 
                : prefixStart + (int)(stagePercent * prefixRange / 100.0);
            
            yield return new EnvironmentProgressEvent
            {
                Stage = wineProgress.Stage.ToString().ToLower(),
                MessageKey = wineProgress.MessageKey,
                CompletedItems = percent,
                TotalItems = 100,
                IsComplete = wineProgress.IsComplete,
                HasError = wineProgress.HasError,
                ErrorMessageKey = wineProgress.ErrorMessageKey,
                Params = wineProgress.ErrorParams,
                ExtraData = wineProgress.ErrorParams
            };
            
            if (wineProgress.HasError && !string.IsNullOrEmpty(wineProgress.ErrorParams?["message"]?.ToString()))
            {
                logger?.LogError("[WINE-ENV] Error: {Error}", wineProgress.ErrorParams["message"]);
            }
        }
        
        logger?.LogInformation("[WINE-ENV] Wine environment initialization complete");
    }

    public async Task EnsurePrefixAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("[WINE-ENV] EnsurePrefixAsync called");
        await _prefixService.EnsurePrefixAsync(cancellationToken);
    }

    public string GetEmulatorDirectory()
    {
        return _paths.WineRoot;
    }

    public string GetWineExecutablePath()
    {
        return _paths.Wine;
    }

    public Dictionary<string, string> GetEnvironment()
    {
        // Get base environment from paths
        var env = _paths.GetEnvironment();
        
        // Load Wine configuration and apply it
        var config = configService.LoadConfigAsync().GetAwaiter().GetResult();
        var wineConfig = config.Wine;
        
        if (wineConfig != null)
        {
            ApplyWineConfigToEnvironment(env, wineConfig);
        }
        else
        {
            logger?.LogWarning("[WINE-ENV] No Wine configuration found, using defaults");
            // Apply minimal defaults if no config
            ApplyWineConfigToEnvironment(env, new WineConfig());
        }
        
        return env;
    }
    
    /// <summary>
    /// Apply Wine configuration to environment variables
    /// </summary>
    private void ApplyWineConfigToEnvironment(Dictionary<string, string> env, WineConfig config)
    {
        // Wine Debug
        if (!string.IsNullOrWhiteSpace(config.WineDebug))
        {
            env["WINEDEBUG"] = config.WineDebug;
            logger?.LogDebug("[WINE-ENV] Setting WINEDEBUG={WineDebug}", config.WineDebug);
        }
        
        // Msync
        if (config.Msync)
        {
            env["WINEMSYNC"] = "1";
            logger?.LogDebug("[WINE-ENV] Msync enabled");
        }
        
        // Always use native DXMT on macOS.
        env["XL_DXMT_ENABLED"] = "1";
        env["DXMT_ENABLE_NVEXT"] = "1";
        env["MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS"] = "1";
        env["DXMT_CONFIG"] = $"d3d11.metalSpatialUpscaleFactor={config.MetalFxSpatialFactor};d3d11.preferredMaxFrameRate={config.MaxFramerate};";
        env["DXMT_METALFX_SPATIAL_SWAPCHAIN"] = config.MetalFxSpatialEnabled ? "1" : "0";
        logger?.LogDebug("[WINE-ENV] DXMT enabled with MetalFX={MetalFx}, Framerate={Framerate}",
            config.MetalFxSpatialEnabled, config.MaxFramerate);
        
        // Metal HUD
        if (config.Metal3PerformanceOverlay)
        {
            env["MTL_HUD_ENABLED"] = "1";
            logger?.LogDebug("[WINE-ENV] Metal HUD enabled");
        }
        
        // Native Resolution: true = use retina mode (high res), false = use scaling
        if (config.NativeResolution)
        {
            env["WINE_RETINA_MODE"] = "1";
            logger?.LogDebug("[WINE-ENV] Retina mode enabled");
        }
        

        
        // env["WINEDLLOVERRIDES"] = "msquic=,mscoree=n,b;d3d9,d3d10core=n;d3d11=n;dxgi=n";
        // logger?.LogDebug("[WINE-ENV] DLL overrides: d3d9,d3d10core,d3d11,dxgi=n");
    }

    public async Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("[WINE-ENV] Executing: {Command} {Args}", command, string.Join(" ", args));
        
        var startInfo = new ProcessStartInfo
        {
            FileName = _paths.Wine,
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
            throw new Exception($"Failed to start Wine process: {command}");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    public Task<bool> IsAvailableAsync()
    {
        var available = _prefixService.IsWineInstalled();
        logger?.LogDebug("[WINE-ENV] Wine available: {Available}", available);
        return Task.FromResult(available);
    }

    public async Task ApplyConfigAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WINE-ENV] Applying Wine configuration");
        var config = await configService.LoadConfigAsync();
        if (config.Wine != null)
        {
            await _prefixService.ApplyGraphicsSettingsAsync(config.Wine);
            logger?.LogInformation("[WINE-ENV] Wine configuration applied successfully");
        }
        else
        {
            logger?.LogWarning("[WINE-ENV] No Wine configuration found, skipping apply");
        }
    }

    public void StartAudioRouter(int gamePid, bool msyncEnabled)
    {
        if (audioRouterService == null)
        {
            logger?.LogWarning("[WINE-ENV] AudioRouterService not available");
            return;
        }

        try
        {
            logger?.LogInformation("[WINE-ENV] Starting audio router for game PID: {Pid}, Msync: {Msync}", 
                gamePid, msyncEnabled);
            logger?.LogInformation("[WINE-ENV] Audio router params - WinePath: {WinePath}, WinePrefix: {WinePrefix}", 
                _paths.Wine, _paths.WinePrefix);
            
            var result = audioRouterService.StartRouter(gamePid, _paths.WinePrefix, _paths.Wine, msyncEnabled);
            
            if (result)
            {
                logger?.LogInformation("[WINE-ENV] Audio router started successfully");
            }
            else
            {
                logger?.LogWarning("[WINE-ENV] Audio router failed to start");
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[WINE-ENV] Failed to start audio router");
        }
    }
}
