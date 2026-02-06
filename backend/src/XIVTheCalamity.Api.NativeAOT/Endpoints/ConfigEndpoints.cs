using XIVTheCalamity.Api.NativeAOT.DTOs;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Api.NativeAOT.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/config");

        // GET /api/config
        group.MapGet("/", async (
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Getting application config");
                var config = await configService.LoadConfigAsync();
                return Results.Ok(ApiResponse<AppConfig>.Ok(config));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get config");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to load configuration", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // PUT /api/config
        group.MapPut("/", async (
            AppConfig config,
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Updating application config");
                await configService.SaveConfigAsync(config);
                return Results.Ok(ApiResponse<AppConfig>.Ok(config));
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Invalid config data");
                return Results.BadRequest(ApiErrorResponse.Create("CONFIG_INVALID", "Invalid configuration", ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update config");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to save configuration", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // PATCH /api/config
        group.MapPatch("/", async (
            AppConfig partialConfig,
            ConfigService configService,
            IEnvironmentService environmentService,
            ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Patching application config");
                
                var currentConfig = await configService.LoadConfigAsync();
                
                // Merge changes
                if (!string.IsNullOrEmpty(partialConfig.Game.GamePath))
                    currentConfig.Game.GamePath = partialConfig.Game.GamePath;
                
                if (partialConfig.Game.Region != "TraditionalChinese")
                    currentConfig.Game.Region = partialConfig.Game.Region;
                
                // Wine configuration
                if (partialConfig.Wine != null)
                {
                    currentConfig.Wine.DxmtEnabled = partialConfig.Wine.DxmtEnabled;
                    currentConfig.Wine.MetalFxSpatialEnabled = partialConfig.Wine.MetalFxSpatialEnabled;
                    currentConfig.Wine.MetalFxSpatialFactor = partialConfig.Wine.MetalFxSpatialFactor;
                    currentConfig.Wine.Metal3PerformanceOverlay = partialConfig.Wine.Metal3PerformanceOverlay;
                    currentConfig.Wine.HudScale = partialConfig.Wine.HudScale;
                    currentConfig.Wine.NativeResolution = partialConfig.Wine.NativeResolution;
                    currentConfig.Wine.MaxFramerate = partialConfig.Wine.MaxFramerate;
                    currentConfig.Wine.AudioRouting = partialConfig.Wine.AudioRouting;
                    currentConfig.Wine.EsyncEnabled = partialConfig.Wine.EsyncEnabled;
                    currentConfig.Wine.Msync = partialConfig.Wine.Msync;
                    currentConfig.Wine.WineDebug = partialConfig.Wine.WineDebug;
                    currentConfig.Wine.LeftOptionIsAlt = partialConfig.Wine.LeftOptionIsAlt;
                    currentConfig.Wine.RightOptionIsAlt = partialConfig.Wine.RightOptionIsAlt;
                    currentConfig.Wine.LeftCommandIsCtrl = partialConfig.Wine.LeftCommandIsCtrl;
                    currentConfig.Wine.RightCommandIsCtrl = partialConfig.Wine.RightCommandIsCtrl;
                }
                
                // Dalamud configuration
                if (partialConfig.Dalamud != null)
                {
                    currentConfig.Dalamud.Enabled = partialConfig.Dalamud.Enabled;
                    currentConfig.Dalamud.InjectDelay = partialConfig.Dalamud.InjectDelay;
                    currentConfig.Dalamud.SafeMode = partialConfig.Dalamud.SafeMode;
                    if (!string.IsNullOrEmpty(partialConfig.Dalamud.PluginRepoUrl))
                        currentConfig.Dalamud.PluginRepoUrl = partialConfig.Dalamud.PluginRepoUrl;
                }
                
                // Launcher configuration
                if (partialConfig.Launcher != null)
                {
                    currentConfig.Launcher.EncryptedArguments = partialConfig.Launcher.EncryptedArguments;
                    currentConfig.Launcher.ExitWithGame = partialConfig.Launcher.ExitWithGame;
                    currentConfig.Launcher.NonZeroExitError = partialConfig.Launcher.NonZeroExitError;
                    currentConfig.Launcher.DevelopmentMode = partialConfig.Launcher.DevelopmentMode;
                    currentConfig.Launcher.ShowDalamudTab = partialConfig.Launcher.ShowDalamudTab;
                }
                
                await configService.SaveConfigAsync(currentConfig);
                
                try
                {
                    logger.LogInformation("Applying platform configuration");
                    await environmentService.ApplyConfigAsync();
                }
                catch (Exception envEx)
                {
                    logger.LogWarning(envEx, "Failed to apply platform configuration, but config was saved");
                }
                
                return Results.Ok(ApiResponse<AppConfig>.Ok(currentConfig));
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Invalid config data");
                return Results.BadRequest(ApiErrorResponse.Create("CONFIG_INVALID", "Invalid configuration", ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to patch config");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to update configuration", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // POST /api/config/reset
        group.MapPost("/reset", async (
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Resetting application config to default");
                var config = await configService.ResetToDefaultAsync();
                return Results.Ok(ApiResponse<AppConfig>.Ok(config));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reset config");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to reset configuration", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // GET /api/config/path
        group.MapGet("/path", (
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                var path = configService.GetConfigPath();
                return Results.Ok(ApiResponse<ConfigPathResponse>.Ok(new ConfigPathResponse(path)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get config path");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to get config path", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }
}
