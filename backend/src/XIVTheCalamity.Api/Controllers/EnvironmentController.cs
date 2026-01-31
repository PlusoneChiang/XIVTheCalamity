using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using System.Text.Json;
using XIVTheCalamity.Api.Extensions;
using XIVTheCalamity.Api.Models;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Platform.MacOS.Wine;

namespace XIVTheCalamity.Api.Controllers;

/// <summary>
/// 環境初始化 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EnvironmentController : ControllerBase
{
    private readonly ILogger<EnvironmentController> _logger;
    private readonly WinePrefixService? _winePrefixService;

    public EnvironmentController(
        ILogger<EnvironmentController> logger,
        WinePrefixService? winePrefixService = null)
    {
        _logger = logger;
        _winePrefixService = winePrefixService;
    }

    /// <summary>
    /// Initialize environment (Server-Sent Events)
    /// </summary>
    [HttpGet("initialize")]
    public async Task Initialize(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[ENV-INIT] ========== Environment Initialize API Called ==========");
        _logger.LogInformation("[ENV-INIT] Client connected from: {RemoteIp}", HttpContext.Connection.RemoteIpAddress);
        
        // Disable response buffering for SSE
        var bufferingFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();
        
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx buffering

        await Response.Body.FlushAsync(cancellationToken);
        _logger.LogInformation("[ENV-INIT] Response headers set, stream opened");

        try
        {
            // Windows does not need initialization
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger.LogInformation("[ENV-INIT] Windows platform detected, skipping Wine initialization");
                await SendSseEvent("complete", new { isComplete = true, messageKey = "progress.skip_windows" }, cancellationToken);
                _logger.LogInformation("[ENV-INIT] ========== Completed (Windows) ==========");
                return;
            }

            // macOS/Linux execute Wine initialization
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _logger.LogInformation("[ENV-INIT] Platform: {Platform}", RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Linux");
                
                if (_winePrefixService == null)
                {
                    _logger.LogError("[ENV-INIT] WinePrefixService is NULL!");
                    throw new InvalidOperationException("WinePrefixService not available");
                }

                _logger.LogInformation("[ENV-INIT] WinePrefixService available, starting initialization");

                // Use SemaphoreSlim to ensure progress callback completes before method returns
                var progressSemaphore = new SemaphoreSlim(0, 1);
                
                var progress = new Progress<WineInitProgress>(async p =>
                {
                    try
                    {
                        _logger.LogDebug("[ENV-INIT] Progress update: Stage={Stage}, MessageKey={MessageKey}, Complete={Complete}, Error={Error}", 
                            p.Stage, p.MessageKey, p.IsComplete, p.HasError);
                        
                        if (p.HasError)
                        {
                            _logger.LogError("[ENV-INIT] ERROR reported: {ErrorMessageKey}", p.ErrorMessageKey);
                            await SendSseEvent("error", p, cancellationToken);
                            progressSemaphore.Release();
                        }
                        else if (p.IsComplete)
                        {
                            _logger.LogInformation("[ENV-INIT] Initialization COMPLETE");
                            await SendSseEvent("complete", p, cancellationToken);
                            progressSemaphore.Release();
                        }
                        else
                        {
                            await SendSseEvent("progress", p, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[ENV-INIT] Failed to send SSE event");
                        progressSemaphore.Release();
                    }
                });

                _logger.LogInformation("[ENV-INIT] Calling WinePrefixService.InitializePrefixAsync()");
                await _winePrefixService.InitializePrefixAsync(progress, cancellationToken);
                
                // Wait for the final progress event (complete or error) to be sent
                _logger.LogDebug("[ENV-INIT] Waiting for final progress event to be sent...");
                await progressSemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                
                _logger.LogInformation("[ENV-INIT] ========== Completed Successfully ==========");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[ENV-INIT] ========== Cancelled by client ==========");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ENV-INIT] ========== FAILED with exception ==========");
            try
            {
                await SendSseEvent("error", new 
                { 
                    hasError = true, 
                    errorMessageKey = "error.init_exception",
                    errorParams = new Dictionary<string, object> { { "message", ex.Message } }
                }, cancellationToken);
            }
            catch (Exception sendEx)
            {
                _logger.LogWarning(sendEx, "[ENV-INIT] Failed to send error event to client (client disconnected?)");
            }
        }
    }

    private async Task SendSseEvent(string eventName, object data, CancellationToken cancellationToken)
    {
        try
        {
            // Check if connection is still alive
            if (HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogDebug("[ENV-INIT] SSE connection closed, skipping event: {EventName}", eventName);
                return;
            }

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            _logger.LogDebug("[ENV-INIT] Sending SSE event: {EventName}, Data: {Json}", eventName, json);
            
            await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            
            _logger.LogDebug("[ENV-INIT] SSE event sent successfully: {EventName}", eventName);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("[ENV-INIT] HttpContext disposed, cannot send SSE event: {EventName}", eventName);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[ENV-INIT] SSE operation cancelled: {EventName}", eventName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ENV-INIT] Failed to send SSE event: {EventName}", eventName);
        }
    }
}
