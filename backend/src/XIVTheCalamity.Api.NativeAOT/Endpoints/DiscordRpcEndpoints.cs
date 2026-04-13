using XIVTheCalamity.Api.NativeAOT.DTOs;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Dalamud.Services;
using XIVTheCalamity.Platform.MacOS.Discord;

namespace XIVTheCalamity.Api.NativeAOT.Endpoints;

public static class DiscordRpcEndpoints
{
    public static void MapDiscordRpcEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/discord-rpc");

        group.MapGet("/status", async (
            DiscordRpcBridgeService bridgeService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await bridgeService.GetStatusAsync(cancellationToken);
                return Results.Ok(ApiResponse<DiscordRpcStatusResponse>.Ok(new DiscordRpcStatusResponse(status)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DISCORD-RPC] Failed to get status");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to get Discord RPC status", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        group.MapPost("/install", async (
            DiscordRpcBridgeService bridgeService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await bridgeService.EnsureReadyAsync(forceInstall: true, cancellationToken);
                if (!result.Success)
                {
                    return Results.BadRequest(ApiErrorResponse.Create("DISCORD_RPC_INSTALL_FAILED", result.Message));
                }

                return Results.Ok(ApiResponse<DiscordRpcInstallResponse>.Ok(
                    new DiscordRpcInstallResponse(true, result.Message, result.Status)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DISCORD-RPC] Install failed");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to install Discord RPC bridge", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        group.MapGet("/producer-status", async (
            ConfigService configService,
            DalamudPathService dalamudPathService,
            DiscordRpcBridgeService bridgeService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var config = await configService.LoadConfigAsync();
                var bridgeStatus = await bridgeService.GetStatusAsync(cancellationToken);
                var richPresencePluginInstalled = Directory.Exists(
                    Path.Combine(dalamudPathService.PluginsPath, "Dalamud.RichPresence"));

                var useInGameProducer =
                    bridgeStatus.Enabled &&
                    bridgeStatus.Supported &&
                    bridgeStatus.PrefixBridgeInstalled &&
                    config.Dalamud.Enabled &&
                    richPresencePluginInstalled;

                var reason = useInGameProducer
                    ? "In-game DRP producer is ready."
                    : "Falling back to launcher producer because one or more prerequisites are missing.";

                return Results.Ok(ApiResponse<DiscordRpcProducerStatusResponse>.Ok(
                    new DiscordRpcProducerStatusResponse(
                        useInGameProducer,
                        bridgeStatus.Enabled,
                        bridgeStatus.Supported,
                        bridgeStatus.PrefixBridgeInstalled,
                        config.Dalamud.Enabled,
                        richPresencePluginInstalled,
                        reason)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DISCORD-RPC] Failed to evaluate producer status");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to evaluate Discord RPC producer status", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }
}
