using XIVTheCalamity.Core.Services;
using XIVTheCalamity.DTOs;
using XIVTheCalamity.Helpers;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Dalamud.Models;
using XIVTheCalamity.Dalamud.Services;

namespace XIVTheCalamity.Endpoints;

public static class DalamudEndpoints
{
    public static void MapDalamudEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dalamud");

        // GET /api/dalamud/status
        group.MapGet("/status", async (
            DalamudUpdater updater,
            bool? useLatestPreRelease,
            ILogger<Program> logger) =>
        {
            try
            {
                var status = await updater.GetStatusAsync(useLatestPreRelease);
                return Results.Ok(ApiResponse<DalamudStatus>.Ok(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get Dalamud status");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to get Dalamud status", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // GET /api/dalamud/update-stream (IPC events)
        group.MapGet("/update-stream", async (
            HttpContext context,
            DalamudUpdater updater,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            IpcEventHelper.SetupStream(context);
            
            logger.LogInformation("Starting Dalamud update via IPC events");
            
            try
            {
                await foreach (var progress in updater.UpdateAsync(cancellationToken))
                {
                    var eventType = IpcEventHelper.DetermineEventType(progress.HasError, progress.IsComplete);
                    await IpcEventHelper.SendAsync(context.Response, eventType, progress,
                        AppJsonContext.Default.DalamudUpdateProgress, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                await IpcEventHelper.SendAsync(context.Response, "cancelled",
                    new SseMessage("Operation cancelled"),
                    AppJsonContext.Default.SseMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dalamud update failed");
                await IpcEventHelper.SendAsync(context.Response, "error",
                    new SseError("DALAMUD_UPDATE_FAILED", ex.Message),
                    AppJsonContext.Default.SseError, cancellationToken);
            }
            
            return Results.Empty;
        });

        // GET /api/dalamud/paths
        group.MapGet("/paths", async (
            DalamudPathService pathService,
            ConfigService configService,
            bool? useLatestPreRelease,
            ILogger<Program> logger) =>
        {
            try
            {
                var usePreRelease = useLatestPreRelease ?? (await configService.LoadConfigAsync()).Dalamud.UseLatestPreRelease;
                var paths = new DalamudPathsResponse(
                    pathService.BasePath,
                    pathService.HooksPath,
                    pathService.RuntimePath,
                    pathService.AssetsPath,
                    pathService.ConfigPath,
                    pathService.PluginsPath,
                    pathService.GetInjectorPath(usePreRelease));
                return Results.Ok(ApiResponse<DalamudPathsResponse>.Ok(paths));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get Dalamud paths");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to get Dalamud paths", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }
}
