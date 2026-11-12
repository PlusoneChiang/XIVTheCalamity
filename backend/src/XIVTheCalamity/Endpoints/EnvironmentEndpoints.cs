using System.Runtime.InteropServices;
using System.Diagnostics;
using XIVTheCalamity.DTOs;
using XIVTheCalamity.Helpers;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Platform;
using XIVTheCalamity.Platform.MacOS.Wine;
using XIVTheCalamity.Services;

namespace XIVTheCalamity.Endpoints;

public static class EnvironmentEndpoints
{
    private static readonly SemaphoreSlim RosettaInstallLock = new(1, 1);

    public static void MapEnvironmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/environment");

        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            MapRosettaEndpoints(group);

        // GET /api/environment/initialize (IPC events)
        group.MapGet("/initialize", async (
            HttpContext context,
            IEnvironmentService? environmentService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("[ENV-INIT] Environment Initialize API Called");
            
            IpcEventHelper.SetupStream(context);

            if (environmentService == null)
            {
                logger.LogWarning("[ENV-INIT] IEnvironmentService is not available");
                await IpcEventHelper.SendAsync(context.Response, "error",
                    new SseError("SERVICE_UNAVAILABLE", "Environment service not available"),
                    AppJsonContext.Default.SseError, cancellationToken);
                return Results.Empty;
            }

            logger.LogInformation("[ENV-INIT] Starting environment initialization");
            
            try
            {
                await foreach (var progress in environmentService.InitializeAsync(cancellationToken))
                {
                    var eventType = IpcEventHelper.DetermineEventType(progress.HasError, progress.IsComplete);
                    await IpcEventHelper.SendAsync(context.Response, eventType, progress,
                        AppJsonContext.Default.EnvironmentProgressEvent, cancellationToken);
                }
                logger.LogInformation("[ENV-INIT] Completed Successfully");
            }
            catch (OperationCanceledException)
            {
                await IpcEventHelper.SendAsync(context.Response, "cancelled",
                    new SseMessage("Operation cancelled"),
                    AppJsonContext.Default.SseMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ENV-INIT] Initialization failed");
                await IpcEventHelper.SendAsync(context.Response, "error",
                    new SseError("INIT_FAILED", ex.Message),
                    AppJsonContext.Default.SseError, cancellationToken);
            }
            
            return Results.Empty;
        });

        // POST /api/environment/launch-tool/{tool}
        group.MapPost("/launch-tool/{tool}", async (
            string tool,
            IEnvironmentService? environmentService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (environmentService == null)
            {
                return Results.BadRequest(ApiErrorResponse.Create("SERVICE_UNAVAILABLE", "Environment service not available"));
            }
            
            try
            {
                var toolPath = tool switch
                {
                    "winecfg" => @"C:\windows\system32\winecfg.exe",
                    "regedit" => @"C:\windows\system32\regedit.exe",
                    "cmd" => @"C:\windows\system32\cmd.exe",
                    "notepad" => @"C:\windows\system32\notepad.exe",
                    "explorer" => @"C:\windows\system32\explorer.exe",
                    _ => null
                };
                if (toolPath == null)
                {
                    return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Unsupported diagnostic tool"));
                }

                logger.LogInformation("[ENV-TOOL] Launching diagnostic tool: {Tool}", tool);
                var result = await environmentService.ExecuteAsync(toolPath, Array.Empty<string>(), cancellationToken);
                
                return Results.Json(new ToolLaunchResult(true, result.ExitCode, result.StandardOutput, result.StandardError),
                    AppJsonContext.Default.ToolLaunchResult);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ENV-TOOL] Failed to launch tool: {Tool}", tool);
                return Results.Json(ApiErrorResponse.Create("TOOL_LAUNCH_FAILED", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }

    private static void MapRosettaEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/install-rosetta", async (HttpContext context, ILogger<Program> logger) =>
        {
            if (!context.Request.HasJsonContentType())
                return RosettaError("INVALID_REQUEST", 400);
            if (!await RosettaInstallLock.WaitAsync(0))
                return RosettaError("INSTALL_IN_PROGRESS", 409);

            try
            {
                if (RosettaAvailabilityCheck.Check(default) != RosettaAvailability.Available)
                {
                    var startInfo = new ProcessStartInfo("/usr/sbin/softwareupdate")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    startInfo.ArgumentList.Add("--install-rosetta");
                    startInfo.ArgumentList.Add("--agree-to-license");
                    using var process = Process.Start(startInfo)
                        ?? throw new InvalidOperationException("無法啟動 Rosetta 安裝程式。");
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    // 安裝不因瀏覽器斷線而中斷，避免重試時同時啟動多個安裝程序。
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                    {
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                        await Task.WhenAll(output, error);
                        throw new TimeoutException("Rosetta 安裝逾時。");
                    }
                    await Task.WhenAll(output, error);
                    if (process.ExitCode != 0 || RosettaAvailabilityCheck.Check(default) != RosettaAvailability.Available)
                    {
                        logger.LogError("[ROSETTA] 安裝失敗，結束碼 {ExitCode}: {Output} {Error}",
                            process.ExitCode, output.Result, error.Result);
                        return RosettaError("INSTALL_FAILED", 500);
                    }
                }
                return Results.Json(ApiResponse<string>.Ok("installed"), AppJsonContext.Default.ApiResponseString);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ROSETTA] 無法安裝 Rosetta");
                return RosettaError("INSTALL_FAILED", 500);
            }
            finally
            {
                RosettaInstallLock.Release();
            }
        });

        group.MapPost("/restart", (HttpContext context, ILogger<Program> logger) =>
        {
            if (!context.Request.HasJsonContentType() ||
                RosettaAvailabilityCheck.Check(default) != RosettaAvailability.Available)
                return RosettaError("NOT_READY", 400);
            try
            {
                LauncherRestart.Schedule(context);
                return Results.Json(ApiResponse<string>.Ok("restarting"), AppJsonContext.Default.ApiResponseString);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ROSETTA] 無法重新啟動 Launcher");
                return RosettaError("RESTART_FAILED", 500);
            }
        });

    }

    private static IResult RosettaError(string code, int statusCode) =>
        Results.Json(ApiErrorResponse.Create(code, $"rosetta.{code.ToLowerInvariant()}"), AppJsonContext.Default.ApiErrorResponse, statusCode: statusCode);

}
