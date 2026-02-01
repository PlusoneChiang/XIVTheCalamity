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
    AudioRouterService? audioRouterService = null,
    ILogger<WineEnvironmentService>? logger = null
) : IEnvironmentService
{
    private readonly WinePathService _paths = WinePathService.Instance;
    private readonly WinePrefixService _prefixService = new();

    public async IAsyncEnumerable<EnvironmentProgressEvent> InitializeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WINE-ENV] Starting Wine environment initialization");
        
        yield return new EnvironmentProgressEvent
        {
            Stage = "checking",
            MessageKey = "progress.checking_wine",
            Percentage = 10
        };
        
        // Delegate to WinePrefixService and convert progress events
        await foreach (var wineProgress in _prefixService.InitializePrefixAsyncEnumerable(cancellationToken))
        {
            // Convert WineInitProgress to EnvironmentProgressEvent
            var percent = wineProgress.Stage switch
            {
                WineInitStage.Checking => 10,
                WineInitStage.CreatingPrefix => 30,
                WineInitStage.ConfiguringMedia => 50,
                WineInitStage.InstallingFonts => 70,
                WineInitStage.SettingLocale => 90,
                WineInitStage.Complete => 100,
                _ => 0
            };
            
            yield return new EnvironmentProgressEvent
            {
                Stage = wineProgress.Stage.ToString().ToLower(),
                MessageKey = wineProgress.MessageKey,
                CompletedItems = percent,
                TotalItems = 100,
                IsComplete = wineProgress.IsComplete,
                HasError = wineProgress.HasError,
                ErrorMessageKey = wineProgress.ErrorMessageKey,
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
        
        // Esync
        if (config.EsyncEnabled)
        {
            env["WINEESYNC"] = "1";
            logger?.LogDebug("[WINE-ENV] Esync enabled");
        }
        
        // Msync
        if (config.Msync)
        {
            env["WINEMSYNC"] = "1";
            logger?.LogDebug("[WINE-ENV] Msync enabled");
        }
        
        // DXMT configuration
        if (config.DxmtEnabled)
        {
            env["XL_DXMT_ENABLED"] = "1";
            env["MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS"] = "1";
            env["DXMT_CONFIG"] = $"d3d11.metalSpatialUpscaleFactor={config.MetalFxSpatialFactor};d3d11.preferredMaxFrameRate={config.MaxFramerate};";
            env["DXMT_METALFX_SPATIAL_SWAPCHAIN"] = config.MetalFxSpatialEnabled ? "1" : "0";
            logger?.LogDebug("[WINE-ENV] DXMT enabled with MetalFX={MetalFx}, Framerate={Framerate}", 
                config.MetalFxSpatialEnabled, config.MaxFramerate);
        }
        else
        {
            env["XL_DXMT_ENABLED"] = "0";
            logger?.LogDebug("[WINE-ENV] DXMT disabled (DXVK mode)");
        }
        
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
        
        // DLL Overrides - select based on DXMT setting
        var dxgiOverride = config.DxmtEnabled ? "n" : "b";  // native for DXMT, builtin for DXVK
        env["WINEDLLOVERRIDES"] = $"msquic=,mscoree=n,b;d3d9,d3d10core=n;d3d11=n;dxgi={dxgiOverride}";
        logger?.LogDebug("[WINE-ENV] DLL overrides: dxgi={DxgiOverride}", dxgiOverride);
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

    public string GetDebugInfo()
    {
        return $"Wine Environment (macOS)\n" +
               $"Wine Root: {_paths.WineRoot}\n" +
               $"Wine Prefix: {_paths.WinePrefix}\n" +
               $"Wine Executable: {_paths.Wine}\n" +
               $"Prefix Initialized: {_prefixService.IsPrefixInitialized()}";
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

    public void StartAudioRouter(int gamePid, bool esyncEnabled, bool msyncEnabled)
    {
        if (audioRouterService == null)
        {
            logger?.LogWarning("[WINE-ENV] AudioRouterService not available");
            return;
        }

        try
        {
            logger?.LogInformation("[WINE-ENV] Starting audio router for game PID: {Pid}, Esync: {Esync}, Msync: {Msync}", 
                gamePid, esyncEnabled, msyncEnabled);
            logger?.LogInformation("[WINE-ENV] Audio router params - WinePath: {WinePath}, WinePrefix: {WinePrefix}", 
                _paths.Wine, _paths.WinePrefix);
            
            var result = audioRouterService.StartRouter(gamePid, _paths.WinePrefix, _paths.Wine, esyncEnabled, msyncEnabled);
            
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

    // Expose WinePrefixService methods for backward compatibility
    public WinePrefixService GetPrefixService() => _prefixService;
}
