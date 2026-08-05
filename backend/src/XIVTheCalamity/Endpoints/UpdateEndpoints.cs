using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using XIVTheCalamity.DTOs;
using XIVTheCalamity.Helpers;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Game.Services;
using XIVTheCalamity.Services;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Endpoints;

public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/update");

        // GET /api/update/version
        group.MapGet("/version", (
            string gamePath,
            GameVersionService versionService,
            ILogger<Program> logger) =>
        {
            try
            {
                if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
                {
                    return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Invalid game path"));
                }

                var versions = versionService.GetLocalVersions(gamePath);
                return Results.Ok(ApiResponse<object>.Ok(versions));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to read game version");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to read game version", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // GET /api/update/install (SSE)
        group.MapGet("/install", async (
            HttpContext context,
            string gamePath,
            UpdateManager updateManager,
            ConfigService configService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("[UPDATE-SSE] Install endpoint called, gamePath: {GamePath}", gamePath);
            
            SseHelper.SetupSseResponse(context);
            logger.LogInformation("[UPDATE-SSE] Headers set, starting SSE stream");

            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                logger.LogWarning("[UPDATE-SSE] Invalid game path: {GamePath}", gamePath);
                await SseHelper.SendEventAsync(context.Response, "error", 
                    new SseError("VALIDATION_FAILED", "Invalid game path"),
                    AppJsonContext.Default.SseError, cancellationToken);
                return Results.Empty;
            }

            if (OperatingSystem.IsWindows())
            {
                var configuredGamePath = (await configService.LoadConfigAsync()).Game.GamePath;
                if (!PathsEqual(gamePath, configuredGamePath))
                {
                    logger.LogWarning("[UPDATE-SSE] Requested game path does not match configured path: {GamePath}", gamePath);
                    await SseHelper.SendEventAsync(context.Response, "error",
                        new SseError("VALIDATION_FAILED", "Game path does not match the configured directory"),
                        AppJsonContext.Default.SseError, cancellationToken);
                    return Results.Empty;
                }

                var permissionError = await EnsureGameDirectoryWriteAccessAsync(gamePath, cancellationToken);
                if (permissionError != null)
                {
                    logger.LogWarning("[UPDATE-SSE] Game directory authorization failed: {Error}", permissionError);
                    await SseHelper.SendEventAsync(context.Response, "error",
                        new SseError("GAME_DIRECTORY_PERMISSION_DENIED", permissionError),
                        AppJsonContext.Default.SseError, cancellationToken);
                    return Results.Empty;
                }
            }

            logger.LogInformation("[UPDATE-SSE] Starting CheckAndInstallUpdatesAsync");
            
            try
            {
                await foreach (var progress in updateManager.CheckAndInstallUpdatesAsync(gamePath, cancellationToken))
                {
                    var eventType = SseHelper.DetermineEventType(progress.HasError, progress.IsComplete);
                    await SseHelper.SendEventAsync(context.Response, eventType, progress,
                        AppJsonContext.Default.PatchProgressEvent, cancellationToken);
                }
                logger.LogInformation("[UPDATE-SSE] Stream completed");
            }
            catch (OperationCanceledException)
            {
                await SseHelper.SendEventAsync(context.Response, "cancelled", 
                    new SseMessage("Operation cancelled"),
                    AppJsonContext.Default.SseMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[UPDATE-SSE] Update installation failed");
                await SseHelper.SendEventAsync(context.Response, "error",
                    new SseError("UPDATE_FAILED", ex.Message),
                    AppJsonContext.Default.SseError, cancellationToken);
            }
            
            return Results.Empty;
        });

        // GET /api/update/check
        group.MapGet("/check", async (
            AppUpdaterService appUpdaterService,
            ILogger<Program> logger) =>
        {
            try
            {
                var result = await appUpdaterService.CheckForUpdatesAsync();
                return Results.Ok(ApiResponse<AppUpdateCheckResult>.Ok(result));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to check for launcher updates");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to check updates", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // POST /api/update/download
        group.MapPost("/download", (
            string downloadUrl,
            AppUpdaterService appUpdaterService,
            ILogger<Program> logger) =>
        {
            try
            {
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Download URL is required"));
                }

                // 在背景執行下載
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await appUpdaterService.DownloadUpdateAsync(downloadUrl);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[AppUpdater-API] Background download failed");
                    }
                });

                return Results.Ok(ApiResponse<string>.Ok("Download started"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start update download");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to start download", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // POST /api/update/install
        group.MapPost("/install", (
            AppUpdaterService appUpdaterService,
            ILogger<Program> logger) =>
        {
            try
            {
                appUpdaterService.InstallUpdate();
                return Results.Ok(ApiResponse<string>.Ok("Install initiated"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to execute installer");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to execute installer", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(right)) return false;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanWriteGameDirectory(string gamePath) =>
        CanWriteDirectory(gamePath) &&
        CanWriteDirectory(Path.Combine(gamePath, "boot")) &&
        CanWriteDirectory(Path.Combine(gamePath, "game"));

    private static bool CanWriteDirectory(string directory)
    {
        var probePath = Path.Combine(directory, $".xivtc-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose);
            probe.WriteByte(0);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<string?> EnsureGameDirectoryWriteAccessAsync(
        string gamePath,
        CancellationToken cancellationToken)
    {
        if (CanWriteGameDirectory(gamePath)) return null;

        var fullGamePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath));
        if (PathsEqual(fullGamePath, Path.GetPathRoot(fullGamePath)!))
            return "The game directory cannot be the root of a drive";

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value;
        if (string.IsNullOrEmpty(sid)) return "Unable to identify the current Windows user";

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "icacls.exe"),
                Arguments = $"\"{fullGamePath}\" /grant *{sid}:(OI)(CI)M",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process == null) return "Unable to start the Windows permission prompt";
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0) return $"Windows permission authorization failed ({process.ExitCode})";
            return CanWriteGameDirectory(gamePath) ? null : "The game directory is still not writable after authorization";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "Windows permission authorization was cancelled";
        }
    }
}
