using System.Runtime.InteropServices;
using XIVTheCalamity.Api.NativeAOT.DTOs;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform.MacOS.Wine;

namespace XIVTheCalamity.Api.NativeAOT.Endpoints;

public static class WineEndpoints
{
    public static void MapWineEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/wine");

        // POST /api/wine/launch/{tool}
        group.MapPost("/launch/{tool}", async (
            string tool,
            WineConfigService? wineConfigService,
            ConfigService configService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                logger.LogWarning("Wine tool '{Tool}' requested on Windows platform", tool);
                return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Wine is not available on Windows"));
            }

            if (wineConfigService == null)
            {
                logger.LogError("WineConfigService not available");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "WineConfigService not available"), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }

            try
            {
                var config = await configService.LoadConfigAsync();
                var wineConfig = config.Wine;
                
                logger.LogInformation("Launching Wine tool with config: WINEDEBUG={WineDebug}, Esync={Esync}, Msync={Msync}",
                    wineConfig.WineDebug, wineConfig.EsyncEnabled, wineConfig.Msync);

                var pid = await wineConfigService.LaunchToolAsync(tool, wineConfig, cancellationToken);

                if (pid.HasValue)
                {
                    return Results.Ok(ApiResponse<object>.Ok(new { success = true, message = $"{tool} launched successfully", pid = pid.Value }));
                }
                else
                {
                    return Results.Json(ApiErrorResponse.Create("LAUNCH_FAILED", $"Failed to launch {tool}"), 
                        AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to launch {Tool}", tool);
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", $"Failed to launch {tool}", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // Legacy endpoints for backward compatibility
        group.MapPost("/open-winecfg", async (
            WineConfigService? wineConfigService,
            ConfigService configService,
            ILogger<Program> logger,
            CancellationToken ct) =>
            await LaunchToolInternal("winecfg", wineConfigService, configService, logger, ct));

        group.MapPost("/open-regedit", async (
            WineConfigService? wineConfigService,
            ConfigService configService,
            ILogger<Program> logger,
            CancellationToken ct) =>
            await LaunchToolInternal("regedit", wineConfigService, configService, logger, ct));

        group.MapPost("/open-cmd", async (
            WineConfigService? wineConfigService,
            ConfigService configService,
            ILogger<Program> logger,
            CancellationToken ct) =>
            await LaunchToolInternal("wineconsole", wineConfigService, configService, logger, ct));

        group.MapPost("/open-wineconsole", async (
            WineConfigService? wineConfigService,
            ConfigService configService,
            ILogger<Program> logger,
            CancellationToken ct) =>
            await LaunchToolInternal("wineconsole", wineConfigService, configService, logger, ct));

        group.MapPost("/config", async (
            WineConfigService? wineConfigService,
            ConfigService configService,
            ILogger<Program> logger,
            CancellationToken ct) =>
            await LaunchToolInternal("winecfg", wineConfigService, configService, logger, ct));

        // POST /api/wine/apply-settings
        group.MapPost("/apply-settings", async (
            WinePrefixService? winePrefixService,
            ConfigService configService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                logger.LogWarning("Wine settings requested on Windows platform");
                return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Wine is not available on Windows"));
            }

            if (winePrefixService == null)
            {
                logger.LogError("WinePrefixService not available");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "WinePrefixService not available"), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }

            try
            {
                logger.LogInformation("Applying Wine settings to registry");

                var config = await configService.LoadConfigAsync();
                var wineConfig = config.Wine;

                await winePrefixService.BeginBatchAsync(cancellationToken);

                logger.LogInformation("Applying NativeResolution (RetinaMode={Value})", wineConfig.NativeResolution ? "y" : "n");
                await winePrefixService.AddRegAsync(
                    "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                    "RetinaMode",
                    wineConfig.NativeResolution ? "y" : "n",
                    cancellationToken);

                logger.LogInformation("Applying keyboard mapping");
                await winePrefixService.AddRegAsync(
                    "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                    "LeftOptionIsAlt",
                    wineConfig.LeftOptionIsAlt ? "y" : "n",
                    cancellationToken);
                
                await winePrefixService.AddRegAsync(
                    "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                    "RightOptionIsAlt",
                    wineConfig.RightOptionIsAlt ? "y" : "n",
                    cancellationToken);
                
                await winePrefixService.AddRegAsync(
                    "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                    "LeftCommandIsCtrl",
                    wineConfig.LeftCommandIsCtrl ? "y" : "n",
                    cancellationToken);
                
                await winePrefixService.AddRegAsync(
                    "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver",
                    "RightCommandIsCtrl",
                    wineConfig.RightCommandIsCtrl ? "y" : "n",
                    cancellationToken);

                await winePrefixService.CommitBatchAsync(cancellationToken);

                logger.LogInformation("Wine settings applied successfully");
                return Results.Ok(ApiResponse<object>.Ok(new { success = true, message = "Wine settings applied successfully" }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply Wine settings");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to apply Wine settings", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }
    
    private static async Task<IResult> LaunchToolInternal(
        string tool,
        WineConfigService? wineConfigService,
        ConfigService configService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Wine is not available on Windows"));
        }

        if (wineConfigService == null)
        {
            return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "WineConfigService not available"), 
                AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
        }

        try
        {
            var config = await configService.LoadConfigAsync();
            var pid = await wineConfigService.LaunchToolAsync(tool, config.Wine, cancellationToken);

            if (pid.HasValue)
            {
                return Results.Ok(ApiResponse<object>.Ok(new { success = true, message = $"{tool} launched successfully", pid = pid.Value }));
            }
            else
            {
                return Results.Json(ApiErrorResponse.Create("LAUNCH_FAILED", $"Failed to launch {tool}"), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch {Tool}", tool);
            return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", $"Failed to launch {tool}", ex.Message), 
                AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
        }
    }
}
