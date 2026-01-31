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

        // 檢查環境服務是否可用
        if (environmentService == null)
        {
            logger.LogWarning("[ENV-INIT] IEnvironmentService is not available");
            await SendSseEvent("error", new { messageKey = "error.service_unavailable" }, cancellationToken);
            return;
        }

        try
        {
            logger.LogInformation("[ENV-INIT] Starting environment initialization via IEnvironmentService");
            
            // 使用 Progress<T> 報告進度
            var progress = new Progress<EnvironmentInitProgress>(async p =>
            {
                try
                {
                    logger.LogDebug("[ENV-INIT] Progress: Stage={Stage}, MessageKey={MessageKey}, Percent={Percent}%, Complete={Complete}, Error={Error}", 
                        p.Stage, p.MessageKey, p.Percent, p.IsComplete, p.HasError);
                    
                    if (p.HasError)
                    {
                        logger.LogError("[ENV-INIT] ERROR: {MessageKey} - {Message}", p.MessageKey, p.ErrorMessage);
                        await SendSseEvent("error", p, cancellationToken);
                    }
                    else if (p.IsComplete)
                    {
                        logger.LogInformation("[ENV-INIT] Initialization COMPLETE");
                        await SendSseEvent("complete", p, cancellationToken);
                    }
                    else
                    {
                        await SendSseEvent("progress", p, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[ENV-INIT] Failed to send progress event");
                }
            });

            // 呼叫環境服務初始化
            await environmentService.InitializeAsync(progress, cancellationToken);
            
            logger.LogInformation("[ENV-INIT] ========== Completed Successfully ==========");
            
            // Wait a bit to ensure all progress callbacks are processed
            await Task.Delay(100, cancellationToken);
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
}
