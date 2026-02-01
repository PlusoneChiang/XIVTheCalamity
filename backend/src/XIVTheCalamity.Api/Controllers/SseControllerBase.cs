using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace XIVTheCalamity.Api.Controllers;

/// <summary>
/// Base controller for SSE (Server-Sent Events) endpoints
/// Provides unified SSE setup and event sending methods
/// </summary>
public abstract class SseControllerBase : ControllerBase
{
    private readonly SemaphoreSlim _sseLock = new(1, 1);
    
    /// <summary>
    /// Setup SSE response headers
    /// Call this at the start of SSE endpoint
    /// </summary>
    protected void SetupSseResponse()
    {
        // Disable response buffering for SSE
        var bufferingFeature = HttpContext.Features.Get<IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();
        
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx buffering
    }
    
    /// <summary>
    /// Send SSE event with automatic serialization
    /// Thread-safe with locking to prevent concurrent writes
    /// </summary>
    /// <param name="eventType">Event type (e.g., "progress", "complete", "error")</param>
    /// <param name="data">Data to serialize as JSON</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected async Task SendSseEventAsync(string eventType, object data, CancellationToken cancellationToken = default)
    {
        await _sseLock.WaitAsync(cancellationToken);
        try
        {
            // Check if connection is still alive
            if (HttpContext.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            
            var json = JsonSerializer.Serialize(data, options);
            await Response.WriteAsync($"event: {eventType}\n", cancellationToken);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            _sseLock.Release();
        }
    }
    
    /// <summary>
    /// Stream progress events from IAsyncEnumerable with automatic event type detection
    /// </summary>
    /// <typeparam name="TProgress">Progress event type (must have HasError and IsComplete properties)</typeparam>
    /// <param name="progressStream">Async enumerable progress stream</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="onError">Optional error handler</param>
    protected async Task StreamProgressEventsAsync<TProgress>(
        IAsyncEnumerable<TProgress> progressStream,
        CancellationToken cancellationToken = default,
        Action<Exception>? onError = null)
        where TProgress : class
    {
        try
        {
            await foreach (var progress in progressStream.WithCancellation(cancellationToken))
            {
                // Determine event type based on progress state
                var eventType = DetermineEventType(progress);
                await SendSseEventAsync(eventType, progress, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
            await SendSseEventAsync("cancelled", new { message = "Operation cancelled by user" }, cancellationToken);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            await SendSseEventAsync("error", new 
            { 
                message = ex.Message,
                code = "OPERATION_FAILED"
            }, cancellationToken);
        }
    }
    
    /// <summary>
    /// Determine SSE event type from progress object
    /// Uses reflection to check HasError and IsComplete properties
    /// </summary>
    private static string DetermineEventType<TProgress>(TProgress progress) where TProgress : class
    {
        var type = typeof(TProgress);
        
        // Check HasError property
        var hasErrorProp = type.GetProperty("HasError");
        if (hasErrorProp != null && hasErrorProp.GetValue(progress) is bool hasError && hasError)
        {
            return "error";
        }
        
        // Check IsComplete property
        var isCompleteProp = type.GetProperty("IsComplete");
        if (isCompleteProp != null && isCompleteProp.GetValue(progress) is bool isComplete && isComplete)
        {
            return "complete";
        }
        
        return "progress";
    }
}
