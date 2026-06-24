using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Dalamud.Interfaces;
using XIVTheCalamity.Dalamud.Models;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Dalamud.Services;

/// <summary>
/// Windows-native implementation of IDalamudInjector
/// </summary>
public class WindowsDalamudInjector : IDalamudInjector
{
    private readonly ILogger<WindowsDalamudInjector> _logger;
    private readonly DalamudPathService _pathService;

    private const int DefaultInjectionDelayMs = 5000;
    private const int ProcessDetectionMaxRetries = 10;
    private const int ProcessDetectionRetryDelayMs = 500;
    private const int InjectorTimeoutMs = 60000;
    private const int ClientLanguageChinese = 4;

    public WindowsDalamudInjector(
        ILogger<WindowsDalamudInjector> logger,
        DalamudPathService pathService)
    {
        _logger = logger;
        _pathService = pathService;
    }

    public async Task<DalamudInjectionResult> InjectAsync(
        WineLauncher? launcher,
        Dictionary<string, string>? environment,
        DalamudInjectionOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[DALAMUD-INJECT] Starting Dalamud injection (Windows native)...");

            if (!File.Exists(_pathService.InjectorPath))
            {
                _logger.LogError("[DALAMUD-INJECT] Dalamud.Injector.exe not found at: {Path}", _pathService.InjectorPath);
                return new DalamudInjectionResult
                {
                    Success = false,
                    ErrorMessage = "Dalamud.Injector.exe not found. Please update Dalamud first."
                };
            }

            var gamePid = await WaitForGameProcessWindowsAsync(cancellationToken);
            if (gamePid == null)
            {
                _logger.LogError("[DALAMUD-INJECT] Failed to detect game process");
                return new DalamudInjectionResult
                {
                    Success = false,
                    ErrorMessage = "Failed to detect game process (ffxiv_dx11.exe)"
                };
            }

            _logger.LogInformation("[DALAMUD-INJECT] Game process detected with PID: {Pid}", gamePid);

            var delayMs = options.InjectionDelayMs ?? DefaultInjectionDelayMs;
            _logger.LogInformation("[DALAMUD-INJECT] Waiting {Delay}ms before injection...", delayMs);
            await Task.Delay(delayMs, cancellationToken);

            var injectorArgs = BuildInjectorArgumentsWindows(gamePid.Value, options);
            _logger.LogInformation("[DALAMUD-INJECT] Injector arguments: {Args}", injectorArgs);

            var injectorEnv = BuildInjectorEnvironmentWindows(environment, options);

            var result = await ExecuteInjectorWindowsAsync(injectorArgs, injectorEnv, cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[DALAMUD-INJECT] Injection cancelled");
            return new DalamudInjectionResult
            {
                Success = false,
                ErrorMessage = "Injection cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DALAMUD-INJECT] Injection failed");
            return new DalamudInjectionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DalamudInjectionResult> LaunchWithEntryPointAsync(
        WineLauncher? launcher,
        string gameExePath,
        string gameArguments,
        Dictionary<string, string>? environment,
        DalamudInjectionOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[DALAMUD-INJECT] Starting Dalamud EntryPoint launch (Windows native)...");

            if (!File.Exists(_pathService.InjectorPath))
            {
                _logger.LogError("[DALAMUD-INJECT] Dalamud.Injector.exe not found at: {Path}", _pathService.InjectorPath);
                return new DalamudInjectionResult
                {
                    Success = false,
                    ErrorMessage = "Dalamud.Injector.exe not found. Please update Dalamud first."
                };
            }

            var injectorArgs = BuildEntryPointArgumentsWindows(gameExePath, gameArguments, options);
            _logger.LogInformation("[DALAMUD-INJECT] EntryPoint arguments: {Args}", injectorArgs);
            var injectorEnv = BuildInjectorEnvironmentWindows(environment, options);

            var result = await ExecuteInjectorWindowsAsync(injectorArgs, injectorEnv, cancellationToken);
            if (!result.Success)
                return result;

            var gamePid = await WaitForGameProcessWindowsAsync(cancellationToken);
            if (gamePid == null)
            {
                _logger.LogError("[DALAMUD-INJECT] Failed to detect game process after EntryPoint launch");
                return new DalamudInjectionResult
                {
                    Success = false,
                    ErrorMessage = "Failed to detect game process (ffxiv_dx11.exe)"
                };
            }

            _logger.LogInformation("[DALAMUD-INJECT] EntryPoint launch succeeded; game PID: {Pid}", gamePid);
            result.GamePid = gamePid;
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[DALAMUD-INJECT] EntryPoint launch cancelled");
            return new DalamudInjectionResult { Success = false, ErrorMessage = "Cancelled" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DALAMUD-INJECT] EntryPoint launch failed");
            return new DalamudInjectionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public void EnsureDotnetProgramFilesSymlink(string runtimePath)
    {
        // No-op on Windows
    }

    private async Task<int?> WaitForGameProcessWindowsAsync(CancellationToken ct)
    {
        _logger.LogInformation("[DALAMUD-INJECT] Waiting for game process (Windows native)...");

        for (int i = 0; i < ProcessDetectionMaxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();

            var processes = Process.GetProcessesByName("ffxiv_dx11");
            if (processes.Length > 0)
            {
                var gameProcess = processes[processes.Length - 1]; // newest
                var pid = gameProcess.Id;
                _logger.LogInformation("[DALAMUD-INJECT] Found {Count} ffxiv_dx11.exe process(es), using PID: {Pid}", 
                    processes.Length, pid);

                foreach (var p in processes) p.Dispose();

                return pid;
            }

            _logger.LogDebug("[DALAMUD-INJECT] Game process not found, retry {Attempt}/{Max}...", 
                i + 1, ProcessDetectionMaxRetries);
            await Task.Delay(ProcessDetectionRetryDelayMs, ct);
        }

        return null;
    }

    private string BuildInjectorArgumentsWindows(int gamePid, DalamudInjectionOptions options)
    {
        var sb = new StringBuilder();
        sb.Append($"inject {gamePid}");
        sb.Append($" --dalamud-working-directory=\"{_pathService.HooksDevPath}\"");
        sb.Append($" --dalamud-configuration-path=\"{_pathService.DalamudConfigPath}\"");
        sb.Append($" --logpath=\"{_pathService.LogPath}\"");
        sb.Append($" --dalamud-plugin-directory=\"{_pathService.PluginsPath}\"");
        sb.Append($" --dalamud-asset-directory=\"{_pathService.AssetsDevPath}\"");
        sb.Append($" --dalamud-client-language={ClientLanguageChinese}");

        var delayInit = options.DelayInitializeMs ?? DefaultInjectionDelayMs;
        sb.Append($" --dalamud-delay-initialize={delayInit}");

        if (options.NoPlugin) sb.Append(" --no-plugin");
        if (options.NoThirdPartyPlugin) sb.Append(" --no-3rd-plugin");

        return sb.ToString();
    }

    private string BuildEntryPointArgumentsWindows(string gameExePath, string gameArguments, DalamudInjectionOptions options)
    {
        var sb = new StringBuilder();
        sb.Append($"launch -g \"{gameExePath}\" -m entrypoint");
        sb.Append($" --dalamud-working-directory=\"{_pathService.HooksDevPath}\"");
        sb.Append($" --dalamud-configuration-path=\"{_pathService.DalamudConfigPath}\"");
        sb.Append($" --logpath=\"{_pathService.LogPath}\"");
        sb.Append($" --dalamud-plugin-directory=\"{_pathService.PluginsPath}\"");
        sb.Append($" --dalamud-asset-directory=\"{_pathService.AssetsDevPath}\"");
        sb.Append($" --dalamud-client-language={ClientLanguageChinese}");
        sb.Append(" --dalamud-delay-initialize=0");

        if (options.NoPlugin) sb.Append(" --no-plugin");
        if (options.NoThirdPartyPlugin) sb.Append(" --no-3rd-plugin");

        sb.Append($" -- {gameArguments}");
        return sb.ToString();
    }

    private Dictionary<string, string> BuildInjectorEnvironmentWindows(Dictionary<string, string>? baseEnv, DalamudInjectionOptions? options = null)
    {
        var env = baseEnv != null ? new Dictionary<string, string>(baseEnv) : new Dictionary<string, string>();

        var runtimePath = _pathService.RuntimePath;
        env["DALAMUD_RUNTIME"] = runtimePath;
        env["DOTNET_ROOT"] = runtimePath;

        if (!string.IsNullOrWhiteSpace(options?.PluginRepoUrl))
            env["DALAMUD_MAIN_REPO_URL"] = options.PluginRepoUrl;

        _logger.LogInformation("[DALAMUD-INJECT] Windows environment: DALAMUD_RUNTIME={RuntimePath}", runtimePath);

        return env;
    }

    private async Task<DalamudInjectionResult> ExecuteInjectorWindowsAsync(
        string arguments,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var injectorPath = _pathService.InjectorPath;

        _logger.LogInformation("[DALAMUD-INJECT] Executing (Windows native): {Injector} {Args}", 
            injectorPath, arguments);

        var psi = new ProcessStartInfo
        {
            FileName = injectorPath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(injectorPath) ?? "",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }

        _logger.LogDebug("[DALAMUD-INJECT] Environment variables:");
        foreach (var (key, value) in environment)
        {
            _logger.LogDebug("[DALAMUD-INJECT]   {Key}={Value}", key, value);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = Process.Start(psi);
        if (process == null)
        {
            return new DalamudInjectionResult
            {
                Success = false,
                ErrorMessage = "Failed to start Dalamud.Injector.exe"
            };
        }

        var stdoutTask = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line != null)
                {
                    stdout.AppendLine(line);
                    _logger.LogDebug("[DALAMUD-INJECT] stdout: {Line}", line);
                }
            }
        }, ct);

        var stderrTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line != null)
                {
                    stderr.AppendLine(line);
                    _logger.LogWarning("[DALAMUD-INJECT] stderr: {Line}", line);
                }
            }
        }, ct);

        using var timeoutCts = new CancellationTokenSource(InjectorTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            var outputDrain = Task.WhenAll(stdoutTask, stderrTask);
            await Task.WhenAny(outputDrain, Task.Delay(3000, CancellationToken.None));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogError("[DALAMUD-INJECT] Injector timeout after {Timeout}ms", InjectorTimeoutMs);
            try { process.Kill(); } catch { }
            return new DalamudInjectionResult
            {
                Success = false,
                ErrorMessage = "Injector timeout"
            };
        }

        var exitCode = process.ExitCode;
        _logger.LogInformation("[DALAMUD-INJECT] Injector exited with code: {ExitCode}", exitCode);

        if (exitCode == 0)
        {
            _logger.LogInformation("[DALAMUD-INJECT] Dalamud injection successful!");
            return new DalamudInjectionResult
            {
                Success = true,
                ExitCode = exitCode,
                StdOut = stdout.ToString()
            };
        }
        else
        {
            var errorMsg = stderr.Length > 0 ? stderr.ToString() : stdout.ToString();
            _logger.LogError("[DALAMUD-INJECT] Injection failed: {Error}", errorMsg);
            return new DalamudInjectionResult
            {
                Success = false,
                ExitCode = exitCode,
                ErrorMessage = $"Injector exited with code {exitCode}: {errorMsg}"
            };
        }
    }
}
