using XIVTheCalamity.Api.NativeAOT.DTOs;
using XIVTheCalamity.Api.NativeAOT.Helpers;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Dalamud.Models;
using XIVTheCalamity.Dalamud.Services;

namespace XIVTheCalamity.Api.NativeAOT.Endpoints;

public static class DalamudEndpoints
{
    public static void MapDalamudEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dalamud");

        // GET /api/dalamud/status
        group.MapGet("/status", async (
            DalamudUpdater updater,
            ILogger<Program> logger) =>
        {
            try
            {
                var status = await updater.GetStatusAsync();
                return Results.Ok(ApiResponse<DalamudStatus>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get Dalamud status");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to get Dalamud status", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // GET /api/dalamud/update-stream (SSE)
        group.MapGet("/update-stream", async (
            HttpContext context,
            DalamudUpdater updater,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            SseHelper.SetupSseResponse(context);
            
            logger.LogInformation("Starting Dalamud update via SSE");
            
            try
            {
                await foreach (var progress in updater.UpdateAsync(cancellationToken))
                {
                    var eventType = DetermineEventType(progress);
                    await SseHelper.SendEventAsync(context.Response, eventType, progress,
                        AppJsonContext.Default.DalamudUpdateProgress, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                await SseHelper.SendEventAsync(context.Response, "cancelled",
                    new SseMessage("Operation cancelled"),
                    AppJsonContext.Default.SseMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dalamud update failed");
                await SseHelper.SendEventAsync(context.Response, "error",
                    new SseError("DALAMUD_UPDATE_FAILED", ex.Message),
                    AppJsonContext.Default.SseError, cancellationToken);
            }
            
            return Results.Empty;
        });

        // GET /api/dalamud/paths
        group.MapGet("/paths", (
            DalamudPathService pathService,
            ILogger<Program> logger) =>
        {
            try
            {
                var paths = new
                {
                    basePath = pathService.BasePath,
                    hooksPath = pathService.HooksPath,
                    runtimePath = pathService.RuntimePath,
                    assetsPath = pathService.AssetsPath,
                    configPath = pathService.ConfigPath,
                    pluginsPath = pathService.PluginsPath,
                    injectorPath = pathService.InjectorPath
                };
                return Results.Ok(ApiResponse<object>.Ok(paths));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get Dalamud paths");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to get Dalamud paths", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }
    
    private static string DetermineEventType(DalamudUpdateProgress progress)
    {
        if (progress.HasError) return "error";
        if (progress.IsComplete) return "complete";
        return "progress";
    }
}
