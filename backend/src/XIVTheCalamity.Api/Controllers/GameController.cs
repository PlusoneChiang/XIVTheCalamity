using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using XIVTheCalamity.Api.Extensions;
using XIVTheCalamity.Api.Models;
using XIVTheCalamity.Dalamud.Services;
using XIVTheCalamity.Game.Launcher;
using XIVTheCalamity.Platform;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Api.Controllers;

/// <summary>
/// Game launch controller
/// </summary>
[ApiController]
[Route("api/game")]
public class GameController : ControllerBase
{
    private readonly ILogger<GameController> _logger;
    private readonly GameLaunchService _gameLaunchService;
    private readonly ConfigService _configService;
    private readonly DalamudInjectorService _dalamudInjector;
    private readonly DalamudPathService _dalamudPathService;
    private readonly IEnvironmentService _environmentService;
    
    public GameController(
        ILogger<GameController> logger,
        GameLaunchService gameLaunchService,
        ConfigService configService,
        DalamudInjectorService dalamudInjector,
        DalamudPathService dalamudPathService,
        IEnvironmentService environmentService)
    {
        _logger = logger;
        _gameLaunchService = gameLaunchService;
        _configService = configService;
        _dalamudInjector = dalamudInjector;
        _dalamudPathService = dalamudPathService;
        _environmentService = environmentService;
    }
    
    /// <summary>
    /// Fake Launch - Test game launch without login, wait for exit and return exit code
    /// </summary>
    [HttpPost("fake-launch")]
    public async Task<IActionResult> FakeLaunch(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GAME] Fake launch requested");
        
        try
        {
            var config = await _configService.LoadConfigAsync();
            
            if (string.IsNullOrEmpty(config.Game.GamePath))
            {
                return this.BadRequestError("GAME_PATH_NOT_CONFIGURED", "Game path not configured");
            }
            
            // Get Dalamud runtime path if enabled
            string? dalamudRuntimePath = null;
            if (config.Dalamud.Enabled)
            {
                dalamudRuntimePath = _dalamudPathService.RuntimePath;
                _logger.LogInformation("[GAME] Dalamud enabled, runtime path: {Path}", dalamudRuntimePath);
            }
            
            var result = await _gameLaunchService.FakeLaunchAsync(
                config.Game.GamePath,
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? config.Proton : config.Wine,
                dalamudRuntimePath,
                cancellationToken);
            
            if (result.Success && result.Process != null)
            {
                _logger.LogInformation("[GAME] Fake launch successful, PID: {Pid}", result.ProcessId);
                
                // Start audio router if enabled (via environment service)
                if (config.Wine?.AudioRouting == true && result.ProcessId.HasValue)
                {
                    _environmentService.StartAudioRouter(result.ProcessId.Value, 
                        config.Wine?.EsyncEnabled ?? false, 
                        config.Wine?.Msync ?? false);
                }
                
                // Inject Dalamud if enabled
                if (config.Dalamud.Enabled)
                {
                    _logger.LogInformation("[GAME] Dalamud enabled, starting injection...");
                    _ = InjectDalamudAsync(config.Dalamud, cancellationToken);
                }
                
                _logger.LogInformation("[GAME] Waiting for game exit...");
                
                // Wait for game to exit
                await result.Process.WaitForExitAsync(cancellationToken);
                var exitCode = result.Process.ExitCode;
                
                _logger.LogInformation("[GAME] Game exited with code: {ExitCode}", exitCode);
                
                return this.SuccessResult(new { 
                    processId = result.ProcessId,
                    exitCode = exitCode
                });
            }
            else if (result.Success)
            {
                // Process started but reference lost
                _logger.LogWarning("[GAME] Fake launch started but process reference lost");
                return this.SuccessResult(new { 
                    processId = result.ProcessId,
                    exitCode = -1
                });
            }
            else
            {
                _logger.LogError("[GAME] Fake launch failed: {Error}", result.ErrorMessage);
                return this.BadRequestError("GAME_LAUNCH_FAILED", result.ErrorMessage ?? "Failed to launch game");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GAME] Fake launch exception");
            return this.InternalError("Failed to launch game", ex.Message);
        }
    }
    
    /// <summary>
    /// Inject Dalamud (runs in background)
    /// </summary>
    private async Task InjectDalamudAsync(
        XIVTheCalamity.Core.Models.DalamudConfig dalamudConfig,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get emulator path and environment from platform service
            var emulatorDir = _environmentService.GetEmulatorDirectory();
            var winePath = Path.Combine(emulatorDir, "files", "bin", "wine");
            
            if (string.IsNullOrEmpty(emulatorDir) || !System.IO.File.Exists(winePath))
            {
                _logger.LogWarning("[GAME] Wine/Proton not available, cannot inject Dalamud");
                return;
            }
            
            var environment = _environmentService.GetEnvironment();
            
            var options = new DalamudInjectionOptions
            {
                InjectionDelayMs = dalamudConfig.InjectDelay > 0 ? dalamudConfig.InjectDelay : null,
                DelayInitializeMs = dalamudConfig.InjectDelay > 0 ? dalamudConfig.InjectDelay : null,
                NoPlugin = dalamudConfig.SafeMode,
                NoThirdPartyPlugin = dalamudConfig.SafeMode
            };
            
            var result = await _dalamudInjector.InjectAsync(winePath, environment, options, cancellationToken);
            
            if (result.Success)
            {
                _logger.LogInformation("[GAME] Dalamud injection successful!");
            }
            else
            {
                _logger.LogWarning("[GAME] Dalamud injection failed: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GAME] Dalamud injection exception");
        }
    }
    
    /// <summary>
    /// Launch game
    /// </summary>
    [HttpPost("launch")]
    public async Task<IActionResult> Launch([FromBody] LaunchRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GAME] Launch requested");
        
        try
        {
            if (string.IsNullOrEmpty(request.SessionId))
            {
                return this.BadRequestError("VALIDATION_FAILED", "Session ID is required");
            }
            
            var config = await _configService.LoadConfigAsync();
            
            if (string.IsNullOrEmpty(config.Game.GamePath))
            {
                return this.BadRequestError("GAME_PATH_NOT_CONFIGURED", "Game path not configured");
            }
            
            // Get Dalamud runtime path if enabled
            string? dalamudRuntimePath = null;
            if (config.Dalamud.Enabled)
            {
                dalamudRuntimePath = _dalamudPathService.RuntimePath;
            }
            
            var result = await _gameLaunchService.LaunchGameAsync(
                config.Game.GamePath,
                request.SessionId,
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? config.Proton : config.Wine,
                dalamudRuntimePath,
                cancellationToken);
            
            if (result.Success)
            {
                _logger.LogInformation("[GAME] Launch successful, PID: {Pid}", result.ProcessId);
                
                // Start audio router if enabled (via environment service)
                if (config.Wine?.AudioRouting == true && result.ProcessId.HasValue)
                {
                    _environmentService.StartAudioRouter(result.ProcessId.Value, 
                        config.Wine?.EsyncEnabled ?? false, 
                        config.Wine?.Msync ?? false);
                }
                
                // Inject Dalamud if enabled
                if (config.Dalamud.Enabled)
                {
                    _logger.LogInformation("[GAME] Dalamud enabled, starting injection...");
                    _ = InjectDalamudAsync(config.Dalamud, cancellationToken);
                }
                
                return this.SuccessResult(new { processId = result.ProcessId });
            }
            else
            {
                _logger.LogError("[GAME] Launch failed: {Error}", result.ErrorMessage);
                return this.BadRequestError("GAME_LAUNCH_FAILED", result.ErrorMessage ?? "Failed to launch game");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GAME] Launch exception");
            return this.InternalError("Failed to launch game", ex.Message);
        }
    }
    
    /// <summary>
    /// Get game status
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return this.SuccessResult(new {
            isRunning = _gameLaunchService.IsGameRunning,
            processId = _gameLaunchService.GameProcess?.Id
        });
    }
    
    /// <summary>
    /// Wait for game exit
    /// </summary>
    [HttpGet("wait-exit")]
    public async Task<IActionResult> WaitForExit(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GAME] Waiting for game exit");
        
        var exitCode = await _gameLaunchService.WaitForExitAsync(cancellationToken);
        
        return this.SuccessResult(new { exitCode = exitCode });
    }
    
    /// <summary>
    /// Start audio router (macOS only)
    /// </summary>
    /// <param name="gamePid">Game process ID</param>
    /// <param name="esync">Enable Esync</param>
    /// <param name="msync">Enable Msync</param>
//             _logger.LogInformation("[GAME] Audio router params - WinePath: {WinePath}, WinePrefix: {WinePrefix}", winePath, winePrefix);
//             
//             var result = audioRouter.StartRouter(gamePid, winePrefix, winePath, esync, msync);
//             if (result)
//             {
//                 _logger.LogInformation("[GAME] Audio router started successfully");
//             }
//             else
//             {
//                 _logger.LogWarning("[GAME] Audio router failed to start");
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.LogWarning(ex, "[GAME] Failed to start audio router");
//         }
//     }
}

public class LaunchRequest
{
    public string SessionId { get; set; } = string.Empty;
}
