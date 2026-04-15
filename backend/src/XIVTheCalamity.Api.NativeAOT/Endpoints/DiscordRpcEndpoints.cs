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
                logger.LogError(ex, "[XBRIDGE] Failed to get status");
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
                    return Results.BadRequest(ApiErrorResponse.Create("DISCORD_RPC_INSTALL_FAILED", result.Message));

                return Results.Ok(ApiResponse<DiscordRpcInstallResponse>.Ok(
                    new DiscordRpcInstallResponse(true, result.Message, result.Status)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[XBRIDGE] Install failed");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to install xbridge", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        group.MapPost("/remove", async (
            DiscordRpcBridgeService bridgeService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await bridgeService.RemoveAsync(cancellationToken);
                if (!result.Success)
                    return Results.BadRequest(ApiErrorResponse.Create("DISCORD_RPC_REMOVE_FAILED", result.Message));

                return Results.Ok(ApiResponse<DiscordRpcRemoveResponse>.Ok(
                    new DiscordRpcRemoveResponse(true, result.Message, result.Status)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[XBRIDGE] Remove failed");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to remove xbridge", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }
}
