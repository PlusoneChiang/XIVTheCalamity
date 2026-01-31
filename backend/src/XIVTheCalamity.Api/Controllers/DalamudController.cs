using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using XIVTheCalamity.Api.Extensions;
using XIVTheCalamity.Api.Models;
using XIVTheCalamity.Dalamud.Models;
using XIVTheCalamity.Dalamud.Services;

namespace XIVTheCalamity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DalamudController : ControllerBase
{
    private readonly ILogger<DalamudController> _logger;
    private readonly DalamudUpdater _updater;
    private readonly DalamudPathService _pathService;

    public DalamudController(
        ILogger<DalamudController> logger,
        DalamudUpdater updater,
        DalamudPathService pathService)
    {
        _logger = logger;
        _updater = updater;
        _pathService = pathService;
    }

    /// <summary>
    /// Get Dalamud status
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var status = await _updater.GetStatusAsync();
            return this.SuccessResult(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Dalamud status");
            return this.InternalError("Failed to get Dalamud status", ex.Message);
        }
    }

    /// <summary>
    /// Start updating Dalamud
    /// </summary>
    [HttpPost("update")]
    public async Task<IActionResult> Update(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Dalamud update");
            var success = await _updater.UpdateAsync(cancellationToken);
            return this.SuccessResult(new { success, progress = _updater.GetProgress() });
        }
        catch (OperationCanceledException)
        {
            return this.SuccessResult(new { success = false, cancelled = true, message = "Update cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dalamud update failed");
            return this.InternalError("Dalamud update failed", ex.Message);
        }
    }

    /// <summary>
    /// Cancel update
    /// </summary>
    [HttpPost("cancel")]
    public IActionResult Cancel()
    {
        try
        {
            _updater.Cancel();
            return this.SuccessResult(new { message = "Update cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel Dalamud update");
            return this.InternalError("Failed to cancel Dalamud update", ex.Message);
        }
    }

    /// <summary>
    /// Get current progress
    /// </summary>
    [HttpGet("progress")]
    public IActionResult GetProgress()
    {
        try
        {
            var progress = _updater.GetProgress();
            return this.SuccessResult(progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Dalamud progress");
            return this.InternalError("Failed to get Dalamud progress", ex.Message);
        }
    }

    /// <summary>
    /// SSE progress push
    /// </summary>
    [HttpGet("update-stream")]
    public async Task UpdateStream(CancellationToken cancellationToken)
    {
        // Disable buffering to support SSE
        var bufferingFeature = Response.HttpContext.Features.Get<IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();
        
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no");
        
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        // Subscribe to progress events
        var tcs = new TaskCompletionSource<bool>();
        
        Action<DalamudUpdateProgress> progressHandler = async progress =>
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(progress, options);
                await Response.WriteAsync($"event: progress\n", cancellationToken);
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                
                if (progress.IsComplete || progress.HasError)
                {
                    tcs.TrySetResult(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSE push failed");
            }
        };

        _updater.OnProgress += progressHandler;

        try
        {
            // Start update
            _ = Task.Run(async () =>
            {
                await _updater.UpdateAsync(cancellationToken);
            }, cancellationToken);

            // Wait for completion or cancellation
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(30)); // 30 minutes timeout
            
            try
            {
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
        }
        finally
        {
            _updater.OnProgress -= progressHandler;
        }
    }

    /// <summary>
    /// Get Dalamud path information
    /// </summary>
    [HttpGet("paths")]
    public IActionResult GetPaths()
    {
        try
        {
            return this.SuccessResult(new
            {
                basePath = _pathService.BasePath,
                hooksPath = _pathService.HooksPath,
                runtimePath = _pathService.RuntimePath,
                assetsPath = _pathService.AssetsPath,
                configPath = _pathService.ConfigPath,
                pluginsPath = _pathService.PluginsPath,
                injectorPath = _pathService.InjectorPath
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Dalamud paths");
            return this.InternalError("Failed to get Dalamud paths", ex.Message);
        }
    }
}
