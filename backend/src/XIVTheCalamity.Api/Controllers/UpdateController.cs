using Microsoft.AspNetCore.Mvc;
using XIVTheCalamity.Api.Extensions;
using XIVTheCalamity.Api.Models;
using XIVTheCalamity.Game.Models;
using XIVTheCalamity.Game.Services;

namespace XIVTheCalamity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UpdateController : ControllerBase
{
    private readonly ILogger<UpdateController> _logger;
    private readonly UpdateManager _updateManager;
    private readonly GameVersionService _versionService;

    public UpdateController(
        ILogger<UpdateController> logger,
        UpdateManager updateManager,
        GameVersionService versionService)
    {
        _logger = logger;
        _updateManager = updateManager;
        _versionService = versionService;
    }

    /// <summary>
    /// Check local game version
    /// </summary>
    [HttpGet("version")]
    public IActionResult GetLocalVersion([FromQuery] string gamePath)
    {
        try
        {
            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                return this.BadRequestError("VALIDATION_FAILED", "Invalid game path");
            }

            var versions = _versionService.GetLocalVersions(gamePath);
            return this.SuccessResult(versions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read game version");
            return this.InternalError("Failed to read game version", ex.Message);
        }
    }

    /// <summary>
    /// Check and install updates (using official API, no login required)
    /// </summary>
    [HttpPost("check")]
    public async Task<IActionResult> CheckAndInstallUpdates([FromBody] CheckUpdateRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.GamePath) || !Directory.Exists(request.GamePath))
            {
                return this.BadRequestError("VALIDATION_FAILED", "Invalid game path");
            }

            var result = await _updateManager.CheckAndInstallUpdatesAsync(
                request.GamePath,
                progress: null); // SSE progress push

            return this.SuccessResult(result);
        }
        catch (OperationCanceledException)
        {
            return this.SuccessResult(new { cancelled = true, message = "Update check cancelled by user" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check updates");
            return this.InternalError("Failed to check updates", ex.Message);
        }
    }

    /// <summary>
    /// Cancel download
    /// </summary>
    [HttpPost("cancel")]
    public IActionResult CancelDownload()
    {
        try
        {
            _updateManager.OnUserClickLogin();
            return this.SuccessResult(new { message = "Download cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel download");
            return this.InternalError("Failed to cancel download", ex.Message);
        }
    }

    /// <summary>
    /// Get current download progress
    /// </summary>
    [HttpGet("progress")]
    public IActionResult GetProgress()
    {
        try
        {
            var progress = _updateManager.GetCurrentProgress();
            if (progress == null)
            {
                return this.SuccessResult(new { isDownloading = false });
            }

            return this.SuccessResult(new
            {
                isDownloading = _updateManager.IsDownloading,
                progress = progress
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get progress");
            return this.InternalError("Failed to get progress", ex.Message);
        }
    }

    /// <summary>
    /// SSE progress push
    /// </summary>
    [HttpGet("progress-stream")]
    public async Task GetProgressStream(CancellationToken cancellationToken)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var progress = _updateManager.GetCurrentProgress();
                if (progress != null)
                {
                    // Use camelCase serialization options
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    };
                    var json = System.Text.Json.JsonSerializer.Serialize(progress, options);
                    _logger.LogInformation("[SSE] Sending progress: {Json}", json);
                    await Response.WriteAsync($"event: progress\n", cancellationToken);
                    await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

                await Task.Delay(1000, cancellationToken); // Update every second
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
    }

    // Keep old endpoints for backward compatibility (deprecated)
    
    /// <summary>
    /// [Deprecated] Background update: Please use POST /api/update/check
    /// </summary>
    [HttpPost("check-background")]
    public async Task<IActionResult> CheckUpdatePlanA([FromBody] CheckUpdateRequest request)
    {
        _logger.LogWarning("check-background is deprecated, use check instead");
        return await CheckAndInstallUpdates(request);
    }

    /// <summary>
    /// [Deprecated] Update after login: Please use POST /api/update/check (SessionId no longer required)
    /// </summary>
    [HttpPost("check-login")]
    public async Task<IActionResult> CheckUpdatePlanB([FromBody] CheckUpdatePlanBRequest request)
    {
        _logger.LogWarning("check-login is deprecated, use check instead (sessionId is no longer required)");
        return await CheckAndInstallUpdates(new CheckUpdateRequest { GamePath = request.GamePath });
    }
}

/// <summary>
/// Check update request
/// </summary>
public class CheckUpdateRequest
{
    public string GamePath { get; set; } = string.Empty;
}

/// <summary>
/// [Deprecated] Update after login check update request
/// </summary>
public class CheckUpdatePlanBRequest
{
    public string GamePath { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}
