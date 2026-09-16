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
    private static int _restartScheduled;

    public static void MapEnvironmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/environment");

        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            MapRosettaEndpoints(group);

        // GET /api/environment/initialize (SSE)
        group.MapGet("/initialize", async (
            HttpContext context,
            IEnvironmentService? environmentService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("[ENV-INIT] Environment Initialize API Called");
            
            SseHelper.SetupSseResponse(context);
            await context.Response.Body.FlushAsync(cancellationToken);

            if (environmentService == null)
            {
                logger.LogWarning("[ENV-INIT] IEnvironmentService is not available");
                await SseHelper.SendEventAsync(context.Response, "error",
                    new SseError("SERVICE_UNAVAILABLE", "Environment service not available"),
                    AppJsonContext.Default.SseError, cancellationToken);
                return Results.Empty;
            }

            logger.LogInformation("[ENV-INIT] Starting environment initialization");
            
            try
            {
                await foreach (var progress in environmentService.InitializeAsync(cancellationToken))
                {
                    var eventType = SseHelper.DetermineEventType(progress.HasError, progress.IsComplete);
                    await SseHelper.SendEventAsync(context.Response, eventType, progress,
                        AppJsonContext.Default.EnvironmentProgressEvent, cancellationToken);
                }
                logger.LogInformation("[ENV-INIT] Completed Successfully");
            }
            catch (OperationCanceledException)
            {
                await SseHelper.SendEventAsync(context.Response, "cancelled",
                    new SseMessage("Operation cancelled"),
                    AppJsonContext.Default.SseMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ENV-INIT] Initialization failed");
                await SseHelper.SendEventAsync(context.Response, "error",
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
            
            logger.LogInformation("[ENV-TOOL] Launching diagnostic tool: {Tool}", tool);
            
            try
            {
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
            if (Interlocked.CompareExchange(ref _restartScheduled, 1, 0) != 0)
                return RosettaError("RESTART_IN_PROGRESS", 409);
            try
            {
                var window = MainWindowContainer.MainWindow
                    ?? throw new InvalidOperationException("找不到啟動器視窗。");
                var restart = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
                restart.ArgumentList.Add("-c");
                // 參數獨立傳入 shell，並等目前程序退出後再開啟，避免埠號衝突。
                restart.ArgumentList.Add("while /bin/kill -0 \"$1\" 2>/dev/null; do /bin/sleep 1; done; shift; exec \"$@\"");
                restart.ArgumentList.Add("xivtc-restart");
                restart.ArgumentList.Add(Environment.ProcessId.ToString());
                var bundlePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
                if (bundlePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(bundlePath))
                {
                    restart.ArgumentList.Add("/usr/bin/env");
                    restart.ArgumentList.Add("-i");
                    restart.ArgumentList.Add("/usr/bin/open");
                    restart.ArgumentList.Add("-n");
                    restart.ArgumentList.Add(bundlePath);
                }
                else
                {
                    var executable = Environment.ProcessPath
                        ?? throw new InvalidOperationException("找不到啟動器執行檔。");
                    restart.ArgumentList.Add(executable);
                    var arguments = Environment.GetCommandLineArgs();
                    if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                        restart.ArgumentList.Add(arguments[0]);
                    foreach (var argument in arguments.Skip(1))
                        restart.ArgumentList.Add(argument);
                }
                using var process = Process.Start(restart)
                    ?? throw new InvalidOperationException("無法啟動重新啟動程序。");
                context.Response.OnCompleted(() =>
                {
                    window.Invoke(() => window.Close());
                    return Task.CompletedTask;
                });
                return Results.Json(ApiResponse<string>.Ok("restarting"), AppJsonContext.Default.ApiResponseString);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _restartScheduled, 0);
                logger.LogError(ex, "[ROSETTA] 無法重新啟動 Launcher");
                return RosettaError("RESTART_FAILED", 500);
            }
        });

    }

    private static IResult RosettaError(string code, int statusCode) =>
        Results.Json(ApiErrorResponse.Create(code, $"rosetta.{code.ToLowerInvariant()}"), AppJsonContext.Default.ApiErrorResponse, statusCode: statusCode);

}
