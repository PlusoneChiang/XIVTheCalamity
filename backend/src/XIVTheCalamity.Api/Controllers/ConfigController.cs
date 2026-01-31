using Microsoft.AspNetCore.Mvc;
using XIVTheCalamity.Api.Extensions;
using XIVTheCalamity.Api.Models;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform.MacOS.Wine;

namespace XIVTheCalamity.Api.Controllers;

/// <summary>
/// Configuration management API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ConfigController(
    ILogger<ConfigController> logger,
    ConfigService configService,
    WinePrefixService winePrefixService) : ControllerBase
{
    /// <summary>
    /// Get current configuration
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetConfig()
    {
        try
        {
            logger.LogInformation("Getting application config");
            var config = await configService.LoadConfigAsync();
            return this.SuccessResult(config);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get config");
            return this.InternalError("Failed to load configuration", ex.Message);
        }
    }

    /// <summary>
    /// Update configuration (full replacement)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateConfig([FromBody] AppConfig config)
    {
        try
        {
            logger.LogInformation("Updating application config");
            await configService.SaveConfigAsync(config);
            return this.SuccessResult(config);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid config data");
            return this.BadRequestError("CONFIG_INVALID", "Invalid configuration", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update config");
            return this.InternalError("Failed to save configuration", ex.Message);
        }
    }

    /// <summary>
    /// Partial update configuration
    /// </summary>
    [HttpPatch]
    public async Task<IActionResult> PatchConfig([FromBody] AppConfig partialConfig)
    {
        try
        {
            logger.LogInformation("Patching application config");
            
            // Load existing configuration
            var currentConfig = await configService.LoadConfigAsync();
            
            // Merge changes (only update non-null properties)
            // Simplified handling, should use reflection or JSON Merge Patch
            if (!string.IsNullOrEmpty(partialConfig.Game.GamePath))
                currentConfig.Game.GamePath = partialConfig.Game.GamePath;
            
            if (partialConfig.Game.Region != "TraditionalChinese")
                currentConfig.Game.Region = partialConfig.Game.Region;
            
            // Wine configuration
            currentConfig.Wine.DxmtEnabled = partialConfig.Wine.DxmtEnabled;
            currentConfig.Wine.MetalFxSpatialEnabled = partialConfig.Wine.MetalFxSpatialEnabled;
            currentConfig.Wine.MetalFxSpatialFactor = partialConfig.Wine.MetalFxSpatialFactor;
            currentConfig.Wine.Metal3PerformanceOverlay = partialConfig.Wine.Metal3PerformanceOverlay;
            currentConfig.Wine.HudScale = partialConfig.Wine.HudScale;
            currentConfig.Wine.NativeResolution = partialConfig.Wine.NativeResolution;
            currentConfig.Wine.MaxFramerate = partialConfig.Wine.MaxFramerate;
            currentConfig.Wine.AudioRouting = partialConfig.Wine.AudioRouting;
            currentConfig.Wine.EsyncEnabled = partialConfig.Wine.EsyncEnabled;
            currentConfig.Wine.FsyncEnabled = partialConfig.Wine.FsyncEnabled;
            currentConfig.Wine.Msync = partialConfig.Wine.Msync;
            currentConfig.Wine.WineDebug = partialConfig.Wine.WineDebug;
            currentConfig.Wine.LeftOptionIsAlt = partialConfig.Wine.LeftOptionIsAlt;
            currentConfig.Wine.RightOptionIsAlt = partialConfig.Wine.RightOptionIsAlt;
            currentConfig.Wine.LeftCommandIsCtrl = partialConfig.Wine.LeftCommandIsCtrl;
            currentConfig.Wine.RightCommandIsCtrl = partialConfig.Wine.RightCommandIsCtrl;
            
            // Dalamud configuration
            currentConfig.Dalamud.Enabled = partialConfig.Dalamud.Enabled;
            currentConfig.Dalamud.InjectDelay = partialConfig.Dalamud.InjectDelay;
            currentConfig.Dalamud.SafeMode = partialConfig.Dalamud.SafeMode;
            if (!string.IsNullOrEmpty(partialConfig.Dalamud.PluginRepoUrl))
                currentConfig.Dalamud.PluginRepoUrl = partialConfig.Dalamud.PluginRepoUrl;
            
            // Launcher configuration
            currentConfig.Launcher.EncryptedArguments = partialConfig.Launcher.EncryptedArguments;
            currentConfig.Launcher.ExitWithGame = partialConfig.Launcher.ExitWithGame;
            currentConfig.Launcher.NonZeroExitError = partialConfig.Launcher.NonZeroExitError;
            currentConfig.Launcher.DevelopmentMode = partialConfig.Launcher.DevelopmentMode;
            
            await configService.SaveConfigAsync(currentConfig);
            
            // Apply Wine settings to registry and DLLs
            try
            {
                logger.LogInformation("Applying Wine settings to prefix");
                await winePrefixService.ApplyGraphicsSettingsAsync(currentConfig.Wine);
            }
            catch (Exception wineEx)
            {
                logger.LogWarning(wineEx, "Failed to apply Wine settings, but config was saved");
            }
            
            return this.SuccessResult(currentConfig);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid config data");
            return this.BadRequestError("CONFIG_INVALID", "Invalid configuration", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to patch config");
            return this.InternalError("Failed to update configuration", ex.Message);
        }
    }

    /// <summary>
    /// Reset configuration to default values
    /// </summary>
    [HttpPost("reset")]
    public async Task<IActionResult> ResetConfig()
    {
        try
        {
            logger.LogInformation("Resetting application config to default");
            var config = await configService.ResetToDefaultAsync();
            return this.SuccessResult(config);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reset config");
            return this.InternalError("Failed to reset configuration", ex.Message);
        }
    }

    /// <summary>
    /// Get configuration file path
    /// </summary>
    [HttpGet("path")]
    public IActionResult GetConfigPath()
    {
        try
        {
            var path = configService.GetConfigPath();
            return this.SuccessResult(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get config path");
            return this.InternalError("Failed to get config path", ex.Message);
        }
    }
}
