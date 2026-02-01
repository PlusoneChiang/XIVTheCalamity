using Microsoft.AspNetCore.Mvc;
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
) : SseControllerBase
{
    /// <summary>
    /// Initialize environment (Server-Sent Events)
    /// </summary>
    [HttpGet("initialize")]
    public async Task Initialize(CancellationToken cancellationToken)
    {
        logger.LogInformation("[ENV-INIT] ========== Environment Initialize API Called ==========");
        logger.LogInformation("[ENV-INIT] Client connected from: {RemoteIp}", HttpContext.Connection.RemoteIpAddress);
        
        SetupSseResponse();
        await Response.Body.FlushAsync(cancellationToken);
        logger.LogInformation("[ENV-INIT] Response headers set, stream opened");

        // Check if service available
        if (environmentService == null)
        {
            logger.LogWarning("[ENV-INIT] IEnvironmentService is not available");
            await SendSseEventAsync("error", new { messageKey = "error.service_unavailable" }, cancellationToken);
            return;
        }

        logger.LogInformation("[ENV-INIT] Starting environment initialization with IAsyncEnumerable");
        await StreamProgressEventsAsync(
            environmentService.InitializeAsync(cancellationToken),
            cancellationToken,
            onError: ex => logger.LogError(ex, "[ENV-INIT] Initialization failed")
        );
        logger.LogInformation("[ENV-INIT] ========== Completed Successfully ==========");
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
