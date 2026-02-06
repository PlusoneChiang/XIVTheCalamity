using XIVTheCalamity.Api.NativeAOT.DTOs;
using XIVTheCalamity.Api.NativeAOT.Helpers;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Game.Services;

namespace XIVTheCalamity.Api.NativeAOT.Endpoints;

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

        // POST /api/update/check-only
        group.MapPost("/check-only", async (
            CheckUpdateRequest request,
            UpdateManager updateManager,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (string.IsNullOrEmpty(request.GamePath) || !Directory.Exists(request.GamePath))
                {
                    return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Invalid game path"));
                }

                var result = await updateManager.CheckUpdatesAsync(request.GamePath, cancellationToken);
                return Results.Ok(ApiResponse<object>.Ok(result));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to check updates");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to check updates", ex.Message), 
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
                    var eventType = DetermineEventType(progress);
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
    }
    
    private static string DetermineEventType(PatchProgressEvent progress)
    {
        if (progress.HasError) return "error";
        if (progress.IsComplete) return "complete";
        return "progress";
    }
}
