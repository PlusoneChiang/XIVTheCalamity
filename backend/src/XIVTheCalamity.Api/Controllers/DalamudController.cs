using Microsoft.AspNetCore.Mvc;
using XIVTheCalamity.Api.Extensions;
using XIVTheCalamity.Api.Models;
using XIVTheCalamity.Dalamud.Models;
using XIVTheCalamity.Dalamud.Services;

namespace XIVTheCalamity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DalamudController : SseControllerBase
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
    /// Start Dalamud update via SSE
    /// </summary>
    [HttpGet("update-stream")]
    public async Task UpdateStream(CancellationToken cancellationToken)
    {
        SetupSseResponse();
        
        _logger.LogInformation("Starting Dalamud update via SSE");
        
        try
        {
            await StreamProgressEventsAsync(_updater.UpdateAsync(cancellationToken), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dalamud update failed");
            await SendSseEventAsync("error", new
            {
                message = "Dalamud update failed",
                error = ex.Message
            }, cancellationToken);
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
