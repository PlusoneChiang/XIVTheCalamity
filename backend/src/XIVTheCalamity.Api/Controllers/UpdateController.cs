using Microsoft.AspNetCore.Mvc;
using XIVTheCalamity.Api.Extensions;
using XIVTheCalamity.Api.Models;
using XIVTheCalamity.Game.Models;
using XIVTheCalamity.Game.Services;

namespace XIVTheCalamity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UpdateController : SseControllerBase
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
    /// Check for updates only (no installation)
    /// </summary>
    [HttpPost("check-only")]
    public async Task<IActionResult> CheckUpdates([FromBody] CheckUpdateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(request.GamePath) || !Directory.Exists(request.GamePath))
            {
                return this.BadRequestError("VALIDATION_FAILED", "Invalid game path");
            }

            var result = await _updateManager.CheckUpdatesAsync(request.GamePath, cancellationToken);
            return this.SuccessResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check updates");
            return this.InternalError("Failed to check updates", ex.Message);
        }
    }

    /// <summary>
    /// Check and install updates with SSE progress streaming
    /// Uses IAsyncEnumerable for progress reporting
    /// </summary>
    [HttpGet("install")]
    public async Task InstallUpdates([FromQuery] string gamePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[UPDATE-SSE] Install endpoint called, gamePath: {GamePath}", gamePath);
        
        SetupSseResponse();
        _logger.LogInformation("[UPDATE-SSE] Headers set, starting SSE stream");

        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
        {
            _logger.LogWarning("[UPDATE-SSE] Invalid game path: {GamePath}", gamePath);
            await SendSseEventAsync("error", new { 
                message = "Invalid game path",
                code = "VALIDATION_FAILED"
            }, cancellationToken);
            return;
        }

        _logger.LogInformation("[UPDATE-SSE] Starting CheckAndInstallUpdatesAsync");
        await StreamProgressEventsAsync(
            _updateManager.CheckAndInstallUpdatesAsync(gamePath, cancellationToken),
            cancellationToken,
            onError: ex => _logger.LogError(ex, "[UPDATE-SSE] Update installation failed")
        );
        _logger.LogInformation("[UPDATE-SSE] Stream completed");
    }
}

/// <summary>
/// Check update request
/// </summary>
public class CheckUpdateRequest
{
    public string GamePath { get; set; } = string.Empty;
}
