using XIVTheCalamity.DTOs;
using XIVTheCalamity.Helpers;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Game.Services;
using XIVTheCalamity.Services;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Endpoints;

public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/update");

        // GET /api/update/version
        group.MapGet("/version", (
            string gamePath,
            GameVersionService versionService,
            ILogger<Program> logger) =>
        {
            try
            {
                if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
                {
                    return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Invalid game path"));
                }

                var versions = versionService.GetLocalVersions(gamePath);
                return Results.Ok(ApiResponse<object>.Ok(versions));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to read game version");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to read game version", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // GET /api/update/install (SSE)
        group.MapGet("/install", async (
            HttpContext context,
            string gamePath,
            UpdateManager updateManager,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("[UPDATE-SSE] Install endpoint called, gamePath: {GamePath}", gamePath);
            
            SseHelper.SetupSseResponse(context);
            logger.LogInformation("[UPDATE-SSE] Headers set, starting SSE stream");

            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                logger.LogWarning("[UPDATE-SSE] Invalid game path: {GamePath}", gamePath);
                await SseHelper.SendEventAsync(context.Response, "error", 
                    new SseError("VALIDATION_FAILED", "Invalid game path"),
                    AppJsonContext.Default.SseError, cancellationToken);
                return Results.Empty;
            }

            logger.LogInformation("[UPDATE-SSE] Starting CheckAndInstallUpdatesAsync");
            
            try
            {
                await foreach (var progress in updateManager.CheckAndInstallUpdatesAsync(gamePath, cancellationToken))
                {
                    var eventType = SseHelper.DetermineEventType(progress.HasError, progress.IsComplete);
                    await SseHelper.SendEventAsync(context.Response, eventType, progress,
                        AppJsonContext.Default.PatchProgressEvent, cancellationToken);
                }
                logger.LogInformation("[UPDATE-SSE] Stream completed");
            }
            catch (OperationCanceledException)
            {
                await SseHelper.SendEventAsync(context.Response, "cancelled", 
                    new SseMessage("Operation cancelled"),
                    AppJsonContext.Default.SseMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[UPDATE-SSE] Update installation failed");
                await SseHelper.SendEventAsync(context.Response, "error",
                    new SseError("UPDATE_FAILED", ex.Message),
                    AppJsonContext.Default.SseError, cancellationToken);
            }
            
            return Results.Empty;
        });

        // GET /api/update/check
        group.MapGet("/check", async (
            AppUpdaterService appUpdaterService,
            ILogger<Program> logger) =>
        {
            try
            {
                var result = await appUpdaterService.CheckForUpdatesAsync();
                return Results.Ok(ApiResponse<AppUpdateCheckResult>.Ok(result));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to check for launcher updates");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to check updates", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // POST /api/update/download
        group.MapPost("/download", (
            string downloadUrl,
            AppUpdaterService appUpdaterService,
            ILogger<Program> logger) =>
        {
            try
            {
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Download URL is required"));
                }

                // 在背景執行下載
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await appUpdaterService.DownloadUpdateAsync(downloadUrl);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[AppUpdater-API] Background download failed");
                    }
                });

                return Results.Ok(ApiResponse<string>.Ok("Download started"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start update download");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to start download", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // POST /api/update/install
        group.MapPost("/install", (
            AppUpdaterService appUpdaterService,
            ILogger<Program> logger) =>
        {
            try
            {
                appUpdaterService.InstallUpdate();
                return Results.Ok(ApiResponse<string>.Ok("Install initiated"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to execute installer");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to execute installer", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }
}
