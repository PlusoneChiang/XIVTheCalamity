using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Features;

namespace XIVTheCalamity.Api.NativeAOT.Helpers;

/// <summary>
/// Helper methods for Server-Sent Events (SSE)
/// </summary>
public static class SseHelper
{
    private static readonly SemaphoreSlim _sseLock = new(1, 1);
    
    /// <summary>
    /// Setup SSE response headers
    /// </summary>
    public static void SetupSseResponse(HttpContext context)
    {
        var bufferingFeature = context.Features.Get<IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();
        
        context.Response.Headers["Content-Type"] = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";
    }
    
    /// <summary>
    /// Send SSE event with JSON data using JsonTypeInfo (AOT compatible)
    /// </summary>
    public static async Task SendEventAsync<T>(
        HttpResponse response, 
        string eventType, 
        T data,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default)
    {
        await _sseLock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(data, jsonTypeInfo);
            await response.WriteAsync($"event: {eventType}\n", ct);
            await response.WriteAsync($"data: {json}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
        finally
        {
            _sseLock.Release();
        }
    }
}
