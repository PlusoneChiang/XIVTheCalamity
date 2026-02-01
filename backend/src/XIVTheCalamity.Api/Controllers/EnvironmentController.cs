using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Api.Controllers;

/// <summary>
/// 環境初始化 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EnvironmentController(
    ILogger<EnvironmentController> logger,
    IEnvironmentService? environmentService = null
) : ControllerBase
{
    // SSE 並發寫入鎖，防止 Progress<T> 回調並發導致 ERR_INVALID_CHUNKED_ENCODING
    private readonly SemaphoreSlim _sseLock = new(1, 1);

    /// <summary>
    /// Initialize environment (Server-Sent Events)
    /// </summary>
    [HttpGet("initialize")]
    public async Task Initialize(CancellationToken cancellationToken)
    {
        logger.LogInformation("[ENV-INIT] ========== Environment Initialize API Called ==========");
        logger.LogInformation("[ENV-INIT] Client connected from: {RemoteIp}", HttpContext.Connection.RemoteIpAddress);
        
        // Disable response buffering for SSE
        var bufferingFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();
        
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx buffering

        await Response.Body.FlushAsync(cancellationToken);
        logger.LogInformation("[ENV-INIT] Response headers set, stream opened");

        // Check if service available
        if (environmentService == null)
        {
            logger.LogWarning("[ENV-INIT] IEnvironmentService is not available");
            await SendSseEvent("error", new { messageKey = "error.service_unavailable" }, cancellationToken);
            return;
        }

        try
        {
            logger.LogInformation("[ENV-INIT] Starting environment initialization with IAsyncEnumerable");
            
            // NEW: Use await foreach to consume progress events
            // Natural order guarantee - no need for TaskCompletionSource!
            await foreach (var progress in environmentService.InitializeAsync(cancellationToken))
            {
                logger.LogDebug("[ENV-INIT] Progress: Stage={Stage}, MessageKey={MessageKey}, Complete={Complete}, Error={Error}", 
                    progress.Stage, progress.MessageKey, progress.IsComplete, progress.HasError);
                
                string eventType;
                if (progress.HasError)
                {
                    eventType = "error";
                    logger.LogError("[ENV-INIT] ERROR: {MessageKey} - {Message}", progress.ErrorMessageKey, progress.ErrorMessage);
                }
                else if (progress.IsComplete)
                {
                    eventType = "complete";
                    logger.LogInformation("[ENV-INIT] Initialization COMPLETE");
                }
                else
                {
                    eventType = "progress";
                }
                
                await SendSseEvent(eventType, progress, cancellationToken);
            }
            
            logger.LogInformation("[ENV-INIT] ========== Completed Successfully ==========");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("[ENV-INIT] ========== Cancelled by client ==========");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ENV-INIT] ========== FAILED with exception ==========");
            try
            {
                await SendSseEvent("error", new 
                { 
                    stage = "error",
                    messageKey = "error.initialization_failed",
                    errorMessage = ex.Message,
                    hasError = true
                }, cancellationToken);
            }
            catch (Exception sendEx)
            {
                logger.LogWarning(sendEx, "[ENV-INIT] Failed to send error event to client (client disconnected?)");
            }
        }
    }

    /// <summary>
    /// 發送 SSE 事件（使用鎖防止並發寫入導致 chunked encoding 錯誤）
    /// </summary>
    private async Task SendSseEvent(string eventName, object data, CancellationToken cancellationToken)
    {
        await _sseLock.WaitAsync(cancellationToken);
        try
        {
            // Check if connection is still alive
            if (HttpContext.RequestAborted.IsCancellationRequested)
            {
                logger.LogDebug("[ENV-INIT] SSE connection closed, skipping event: {EventName}", eventName);
                return;
            }

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            logger.LogDebug("[ENV-INIT] >> Sending SSE: {EventName}", eventName);
            
            await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            
            logger.LogDebug("[ENV-INIT] << Sent SSE: {EventName}", eventName);
        }
        catch (ObjectDisposedException)
        {
            logger.LogDebug("[ENV-INIT] HttpContext disposed, cannot send SSE event: {EventName}", eventName);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("[ENV-INIT] SSE operation cancelled: {EventName}", eventName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ENV-INIT] Failed to send SSE event: {EventName}", eventName);
        }
        finally
        {
            _sseLock.Release();
        }
    }
    
    /// <summary>
    /// Launch diagnostic tool (winecfg, regedit, etc.)
    /// </summary>
    [HttpPost("launch-tool/{tool}")]
    public async Task<IActionResult> LaunchTool(string tool, CancellationToken cancellationToken)
    {
        if (environmentService == null)
        {
            return BadRequest(new { success = false, error = "Environment service not available" });
        }
        
        logger.LogInformation("[ENV-TOOL] Launching diagnostic tool: {Tool}", tool);
        
        try
        {
            // Map tool name to executable
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
            
            return Ok(new { 
                success = true, 
                exitCode = result.ExitCode,
                stdout = result.StandardOutput,
                stderr = result.StandardError
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ENV-TOOL] Failed to launch tool: {Tool}", tool);
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }
}
