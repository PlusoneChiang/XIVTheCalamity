using XIVTheCalamity.Api.NativeAOT.DTOs;
using XIVTheCalamity.Api.NativeAOT.Helpers;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Api.NativeAOT.Endpoints;

public static class EnvironmentEndpoints
{
    public static void MapEnvironmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/environment");

        // GET /api/environment/initialize (SSE)
        group.MapGet("/initialize", async (
            HttpContext context,
            IEnvironmentService? environmentService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("[ENV-INIT] Environment Initialize API Called");
            
            SseHelper.SetupSseResponse(context);
            await context.Response.Body.FlushAsync(cancellationToken);

            if (environmentService == null)
            {
                logger.LogWarning("[ENV-INIT] IEnvironmentService is not available");
                await SseHelper.SendEventAsync(context.Response, "error",
                    new SseError("SERVICE_UNAVAILABLE", "Environment service not available"),
                    AppJsonContext.Default.SseError, cancellationToken);
                return Results.Empty;
            }

            logger.LogInformation("[ENV-INIT] Starting environment initialization");
            
            try
            {
                await foreach (var progress in environmentService.InitializeAsync(cancellationToken))
                {
                    var eventType = DetermineEventType(progress);
                    await SseHelper.SendEventAsync(context.Response, eventType, progress,
                        AppJsonContext.Default.EnvironmentProgressEvent, cancellationToken);
                }
                logger.LogInformation("[ENV-INIT] Completed Successfully");
            }
            catch (OperationCanceledException)
            {
                await SseHelper.SendEventAsync(context.Response, "cancelled",
                    new SseMessage("Operation cancelled"),
                    AppJsonContext.Default.SseMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ENV-INIT] Initialization failed");
                await SseHelper.SendEventAsync(context.Response, "error",
                    new SseError("INIT_FAILED", ex.Message),
                    AppJsonContext.Default.SseError, cancellationToken);
            }
            
            return Results.Empty;
        });

        // POST /api/environment/launch-tool/{tool}
        group.MapPost("/launch-tool/{tool}", async (
            string tool,
            IEnvironmentService? environmentService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (environmentService == null)
            {
                return Results.BadRequest(ApiErrorResponse.Create("SERVICE_UNAVAILABLE", "Environment service not available"));
            }
            
            logger.LogInformation("[ENV-TOOL] Launching diagnostic tool: {Tool}", tool);
            
            try
            {
                var toolExe = tool.ToLowerInvariant() switch
                {
                    "winecfg" => "winecfg.exe",
                    "regedit" => "regedit.exe",
                    "cmd" => "cmd.exe",
                    "notepad" => "notepad.exe",
                    "explorer" => "explorer.exe",
                    _ => $"{tool}.exe"
                };
                
                var result = await environmentService.ExecuteAsync(toolExe, Array.Empty<string>(), cancellationToken);
                
                return Results.Json(new ToolLaunchResult(true, result.ExitCode, result.StandardOutput, result.StandardError),
                    AppJsonContext.Default.ToolLaunchResult);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ENV-TOOL] Failed to launch tool: {Tool}", tool);
                return Results.Json(ApiErrorResponse.Create("TOOL_LAUNCH_FAILED", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }
    
    private static string DetermineEventType(EnvironmentProgressEvent progress)
    {
        if (progress.HasError) return "error";
        if (progress.IsComplete) return "complete";
        return "progress";
    }
}
