using System.Runtime.InteropServices;
using XIVTheCalamity.Api.NativeAOT.DTOs;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Dalamud.Services;
using XIVTheCalamity.Game.Launcher;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Api.NativeAOT.Endpoints;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/game");

        // POST /api/game/fake-launch
        group.MapPost("/fake-launch", async (
            GameLaunchService gameLaunchService,
            ConfigService configService,
            DalamudInjectorService dalamudInjector,
            DalamudPathService dalamudPathService,
            IEnvironmentService environmentService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("[GAME] Fake launch requested");
            
            try
            {
                var config = await configService.LoadConfigAsync();
                
                if (string.IsNullOrEmpty(config.Game.GamePath))
                {
                    return Results.BadRequest(ApiErrorResponse.Create("GAME_PATH_NOT_CONFIGURED", "Game path not configured"));
                }
                
                string? dalamudRuntimePath = null;
                if (config.Dalamud.Enabled)
                {
                    dalamudRuntimePath = dalamudPathService.RuntimePath;
                    logger.LogInformation("[GAME] Dalamud enabled, runtime path: {Path}", dalamudRuntimePath);
                }
                
                var result = await gameLaunchService.FakeLaunchAsync(
                    config.Game.GamePath,
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? config.WineXIV : config.Wine,
                    dalamudRuntimePath,
                    cancellationToken);
                
                if (result.Success && result.Process != null)
                {
                    logger.LogInformation("[GAME] Fake launch successful, PID: {Pid}", result.ProcessId);
                    
                    if (config.Wine?.AudioRouting == true && result.ProcessId.HasValue)
                    {
                        environmentService.StartAudioRouter(result.ProcessId.Value,
                            config.Wine?.EsyncEnabled ?? false,
                            config.Wine?.Msync ?? false);
                    }
                    
                    if (config.Dalamud.Enabled)
                    {
                        logger.LogInformation("[GAME] Dalamud enabled, starting injection...");
                        _ = InjectDalamudAsync(
                            config.Dalamud,
                            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? config.WineXIV : config.Wine,
                            environmentService,
                            dalamudInjector,
                            logger,
                            cancellationToken);
                    }
                    
                    logger.LogInformation("[GAME] Waiting for game exit...");
                    await result.Process.WaitForExitAsync(cancellationToken);
                    var exitCode = result.Process.ExitCode;
                    
                    logger.LogInformation("[GAME] Game exited with code: {ExitCode}", exitCode);
                    
                    return Results.Ok(ApiResponse<object>.Ok(new { processId = result.ProcessId, exitCode }));
                }
                else if (result.Success)
                {
                    logger.LogWarning("[GAME] Fake launch started but process reference lost");
                    return Results.Ok(ApiResponse<object>.Ok(new { processId = result.ProcessId, exitCode = -1 }));
                }
                else
                {
                    logger.LogError("[GAME] Fake launch failed: {Error}", result.ErrorMessage);
                    return Results.BadRequest(ApiErrorResponse.Create("GAME_LAUNCH_FAILED", result.ErrorMessage ?? "Failed to launch game"));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[GAME] Fake launch exception");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to launch game", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // POST /api/game/launch
        group.MapPost("/launch", async (
            LaunchRequest request,
            GameLaunchService gameLaunchService,
            ConfigService configService,
            DalamudInjectorService dalamudInjector,
            DalamudPathService dalamudPathService,
            IEnvironmentService environmentService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("[GAME] Launch requested");
            
            try
            {
                if (string.IsNullOrEmpty(request.SessionId))
                {
                    return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Session ID is required"));
                }
                
                var config = await configService.LoadConfigAsync();
                
                if (string.IsNullOrEmpty(config.Game.GamePath))
                {
                    return Results.BadRequest(ApiErrorResponse.Create("GAME_PATH_NOT_CONFIGURED", "Game path not configured"));
                }
                
                string? dalamudRuntimePath = null;
                if (config.Dalamud.Enabled)
                {
                    dalamudRuntimePath = dalamudPathService.RuntimePath;
                }
                
                var result = await gameLaunchService.LaunchGameAsync(
                    config.Game.GamePath,
                    request.SessionId,
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? config.WineXIV : config.Wine,
                    dalamudRuntimePath,
                    cancellationToken);
                
                if (result.Success)
                {
                    logger.LogInformation("[GAME] Launch successful, PID: {Pid}", result.ProcessId);
                    
                    if (config.Wine?.AudioRouting == true && result.ProcessId.HasValue)
                    {
                        environmentService.StartAudioRouter(result.ProcessId.Value,
                            config.Wine?.EsyncEnabled ?? false,
                            config.Wine?.Msync ?? false);
                    }
                    
                    if (config.Dalamud.Enabled)
                    {
                        logger.LogInformation("[GAME] Dalamud enabled, starting injection...");
                        _ = InjectDalamudAsync(
                            config.Dalamud,
                            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? config.WineXIV : config.Wine,
                            environmentService,
                            dalamudInjector,
                            logger,
                            cancellationToken);
                    }
                    
                    return Results.Ok(ApiResponse<object>.Ok(new { processId = result.ProcessId }));
                }
                else
                {
                    logger.LogError("[GAME] Launch failed: {Error}", result.ErrorMessage);
                    return Results.BadRequest(ApiErrorResponse.Create("GAME_LAUNCH_FAILED", result.ErrorMessage ?? "Failed to launch game"));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[GAME] Launch exception");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to launch game", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // GET /api/game/status
        group.MapGet("/status", (GameLaunchService gameLaunchService) =>
        {
            return Results.Ok(ApiResponse<GameStatusResponse>.Ok(new GameStatusResponse(
                gameLaunchService.IsGameRunning,
                gameLaunchService.GameProcess?.Id)));
        });

        // GET /api/game/wait-exit
        group.MapGet("/wait-exit", async (
            GameLaunchService gameLaunchService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("[GAME] Waiting for game exit");
            var exitCode = await gameLaunchService.WaitForExitAsync(cancellationToken);
            return Results.Ok(ApiResponse<object>.Ok(new { exitCode }));
        });
    }
    
    private static async Task InjectDalamudAsync(
        XIVTheCalamity.Core.Models.DalamudConfig dalamudConfig,
        object? emulatorConfig,
        IEnvironmentService environmentService,
        DalamudInjectorService dalamudInjector,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var emulatorDir = environmentService.GetEmulatorDirectory();
            
            string winePath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                winePath = Path.Combine(emulatorDir, "bin", "wine64");
            }
            else
            {
                throw new PlatformNotSupportedException("Wine is only supported on macOS and Linux");
            }
            
            if (string.IsNullOrEmpty(emulatorDir) || !File.Exists(winePath))
            {
                logger.LogWarning("[GAME] Wine/Wine-XIV not available, cannot inject Dalamud");
                return;
            }
            
            var environment = environmentService.GetEnvironment();
            
            var options = new DalamudInjectionOptions
            {
                InjectionDelayMs = dalamudConfig.InjectDelay > 0 ? dalamudConfig.InjectDelay : null,
                DelayInitializeMs = dalamudConfig.InjectDelay > 0 ? dalamudConfig.InjectDelay : null,
                NoPlugin = dalamudConfig.SafeMode,
                NoThirdPartyPlugin = dalamudConfig.SafeMode
            };
            
            var result = await dalamudInjector.InjectAsync(winePath, environment, options, cancellationToken);
            
            if (result.Success)
            {
                logger.LogInformation("[GAME] Dalamud injection successful!");
            }
            else
            {
                logger.LogWarning("[GAME] Dalamud injection failed: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GAME] Dalamud injection exception");
        }
    }
}
