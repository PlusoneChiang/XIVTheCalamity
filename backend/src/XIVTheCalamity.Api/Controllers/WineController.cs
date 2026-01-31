using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using XIVTheCalamity.Api.Extensions;
using XIVTheCalamity.Api.Models;
using XIVTheCalamity.Platform.MacOS.Wine;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Api.Controllers;

/// <summary>
/// Wine 管理 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WineController : ControllerBase
{
    private readonly ILogger<WineController> _logger;
    private readonly WineConfigService? _wineConfigService;
    private readonly WinePrefixService? _winePrefixService;
    private readonly ConfigService _configService;

    public WineController(
        ILogger<WineController> logger,
        ConfigService configService,
        WineConfigService? wineConfigService = null,
        WinePrefixService? winePrefixService = null)
    {
        _logger = logger;
        _configService = configService;
        _wineConfigService = wineConfigService;
        _winePrefixService = winePrefixService;
    }

    /// <summary>
    /// Launch Wine tool
    /// </summary>
    /// <param name="tool">Tool name (winecfg, regedit, cmd, explorer, etc.)</param>
    [HttpPost("launch/{tool}")]
    public async Task<IActionResult> LaunchTool(string tool, CancellationToken cancellationToken)
    {
        // Windows not supported
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("Wine tool '{Tool}' requested on Windows platform", tool);
            return this.BadRequestError("VALIDATION_FAILED", "Wine is not available on Windows");
        }

        if (_wineConfigService == null)
        {
            _logger.LogError("WineConfigService not available");
            return this.InternalError("WineConfigService not available", "Service not initialized");
        }

        try
        {
            // Get current Wine configuration
            var config = await _configService.LoadConfigAsync();
            var wineConfig = config.Wine;
            
            _logger.LogInformation("Launching Wine tool with config: WINEDEBUG={WineDebug}, Esync={Esync}, Msync={Msync}", 
                wineConfig.WineDebug, wineConfig.EsyncEnabled, wineConfig.Msync);

            var pid = await _wineConfigService.LaunchToolAsync(tool, wineConfig, cancellationToken);

            if (pid.HasValue)
            {
                return this.SuccessResult(new { success = true, message = $"{tool} launched successfully", pid = pid.Value });
            }
            else
            {
                return this.InternalError($"Failed to launch {tool}", "Process not started");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch {Tool}", tool);
            return this.InternalError($"Failed to launch {tool}", ex.Message);
        }
    }

    // Backward compatible legacy API endpoints
    [HttpPost("open-winecfg")]
    public async Task<IActionResult> LaunchWinecfg(CancellationToken cancellationToken)
        => await LaunchTool("winecfg", cancellationToken);

    [HttpPost("open-regedit")]
    public async Task<IActionResult> LaunchRegedit(CancellationToken cancellationToken)
        => await LaunchTool("regedit", cancellationToken);

    [HttpPost("open-cmd")]
    public async Task<IActionResult> LaunchCmd(CancellationToken cancellationToken)
        => await LaunchTool("wineconsole", cancellationToken);

    [HttpPost("open-wineconsole")]
    public async Task<IActionResult> LaunchWineconsole(CancellationToken cancellationToken)
        => await LaunchTool("wineconsole", cancellationToken);

    [HttpPost("config")]
    public async Task<IActionResult> LaunchConfig(CancellationToken cancellationToken)
        => await LaunchTool("winecfg", cancellationToken);

    /// <summary>
    /// Apply Wine settings to Wine prefix
    /// </summary>
    [HttpPost("apply-settings")]
    public async Task<IActionResult> ApplySettings(CancellationToken cancellationToken)
    {
        // Windows not supported
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("Wine settings requested on Windows platform");
            return this.BadRequestError("VALIDATION_FAILED", "Wine is not available on Windows");
        }

        if (_winePrefixService == null)
        {
            _logger.LogError("WinePrefixService not available");
            return this.InternalError("WinePrefixService not available", "Service not initialized");
        }

        try
        {
            _logger.LogInformation("Applying Wine settings to registry");

            // Get current configuration
            var config = await _configService.LoadConfigAsync();
            var wineConfig = config.Wine;

            // Start batch mode
            await _winePrefixService.BeginBatchAsync(cancellationToken);

            // Apply native resolution setting (RetinaMode) to Wine registry
            _logger.LogInformation("Applying NativeResolution (RetinaMode={Value})", wineConfig.NativeResolution ? "y" : "n");
            await _winePrefixService.AddRegAsync(
                "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                "RetinaMode",
                wineConfig.NativeResolution ? "y" : "n",
                cancellationToken);

            // Apply keyboard mapping settings
            _logger.LogInformation("Applying keyboard mapping: LeftOption={LeftOption}, RightOption={RightOption}, LeftCommand={LeftCommand}, RightCommand={RightCommand}",
                wineConfig.LeftOptionIsAlt, wineConfig.RightOptionIsAlt, wineConfig.LeftCommandIsCtrl, wineConfig.RightCommandIsCtrl);
            
            await _winePrefixService.AddRegAsync(
                "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                "LeftOptionIsAlt",
                wineConfig.LeftOptionIsAlt ? "y" : "n",
                cancellationToken);
            
            await _winePrefixService.AddRegAsync(
                "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                "RightOptionIsAlt",
                wineConfig.RightOptionIsAlt ? "y" : "n",
                cancellationToken);
            
            await _winePrefixService.AddRegAsync(
                "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                "LeftCommandIsCtrl",
                wineConfig.LeftCommandIsCtrl ? "y" : "n",
                cancellationToken);
            
            await _winePrefixService.AddRegAsync(
                "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                "RightCommandIsCtrl",
                wineConfig.RightCommandIsCtrl ? "y" : "n",
                cancellationToken);

            // Commit batch changes
            await _winePrefixService.CommitBatchAsync(cancellationToken);

            _logger.LogInformation("Wine settings applied successfully");
            return this.SuccessResult(new { success = true, message = "Wine settings applied successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply Wine settings");
            return this.InternalError("Failed to apply Wine settings", ex.Message);
        }
    }
}
