using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Dalamud.Interfaces;
using XIVTheCalamity.Dalamud.Models;
using XIVTheCalamity.Platform;
using XIVTheCalamity.Platform.MacOS.Wine;

namespace XIVTheCalamity.Dalamud.Services;

/// <summary>
/// Wine-based Dalamud injector (macOS/Linux)
/// </summary>
public class WineDalamudInjector : IDalamudInjector
{
    protected readonly ILogger<WineDalamudInjector> _logger;
    protected readonly DalamudPathService _pathService;

    protected const int DefaultInjectionDelayMs = 5000;
    protected const int ProcessDetectionMaxRetries = 10;
    protected const int ProcessDetectionRetryDelayMs = 500;
    protected const int InjectorTimeoutMs = 60000;
    protected const int ClientLanguageChinese = 4;

    public WineDalamudInjector(
        ILogger<WineDalamudInjector> logger,
        DalamudPathService pathService)
    {
        _logger = logger;
        _pathService = pathService;
    }

    /// <summary>
    /// Hook executed right before injection starts (useful for Linux signature clearing)
    /// </summary>
    protected virtual void PreInjectHook()
    {
    }

    public async Task<DalamudInjectionResult> InjectAsync(
        WineLauncher? launcher,
        Dictionary<string, string>? environment,
        DalamudInjectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (launcher == null || environment == null)
        {
            throw new ArgumentNullException(nameof(launcher), "Wine injection requires launcher and environment.");
        }

        try
        {
            _logger.LogInformation("[DALAMUD-INJECT] Starting Dalamud injection (Wine)...");
            
            // Check if Dalamud is installed
            if (!File.Exists(_pathService.InjectorPath))
            {
                _logger.LogError("[DALAMUD-INJECT] Dalamud.Injector.exe not found at: {Path}", _pathService.InjectorPath);
                return new DalamudInjectionResult
                {
                    Success = false,
                    ErrorMessage = "Dalamud.Injector.exe not found. Please update Dalamud first."
                };
            }
            
            // Prepare environment variables
            var injectorEnv = new Dictionary<string, string>(environment);
            AddDalamudEnvironment(injectorEnv, options);
            
            // Ensure Wine %APPDATA%/XIVLauncherTC symlink points to our Config directory
            EnsureWineAppDataSymlink(injectorEnv);

            // Execute subclass custom hook (e.g. LinuxCachedSignatures clearing)
            PreInjectHook();
            
            // Wait for game process (using winedbg)
            var gamePid = await WaitForGameProcessAsync(launcher, injectorEnv, cancellationToken);
            if (gamePid == null)
            {
                _logger.LogError("[DALAMUD-INJECT] Failed to detect game process");
                return new DalamudInjectionResult
                {
                    Success = false,
                    ErrorMessage = "Failed to detect game process (ffxiv_dx11.exe)"
                };
            }
            
            _logger.LogInformation("[DALAMUD-INJECT] Game process detected with Wine PID: {Pid}", gamePid);
            
            // Wait for injection delay
            var delayMs = options.InjectionDelayMs ?? DefaultInjectionDelayMs;
            _logger.LogInformation("[DALAMUD-INJECT] Waiting {Delay}ms before injection...", delayMs);
            await Task.Delay(delayMs, cancellationToken);
            
            // Build injector arguments
            var injectorArgs = BuildInjectorArguments(gamePid.Value, options);
            _logger.LogInformation("[DALAMUD-INJECT] Injector arguments: {Args}", injectorArgs);
            
            // Execute injection
            var result = await ExecuteInjectorAsync(launcher, injectorArgs, injectorEnv, cancellationToken);
            
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
        if (launcher == null || environment == null)
        {
            throw new ArgumentNullException(nameof(launcher), "Wine EntryPoint launch requires launcher and environment.");
        }

        try
        {
            _logger.LogInformation("[DALAMUD-INJECT] Starting Dalamud EntryPoint launch (Wine)...");

            if (!File.Exists(_pathService.InjectorPath))
            {
                _logger.LogError("[DALAMUD-INJECT] Dalamud.Injector.exe not found at: {Path}", _pathService.InjectorPath);
                return new DalamudInjectionResult
                {
                    Success = false,
                    ErrorMessage = "Dalamud.Injector.exe not found. Please update Dalamud first."
                };
            }

            var injectorEnv = new Dictionary<string, string>(environment);
            AddDalamudEnvironment(injectorEnv, options);

            EnsureWineAppDataSymlink(injectorEnv);

            var gameExeWinePath = ConvertToWinePath(gameExePath);
            var injectorArgs = BuildEntryPointArguments(gameExeWinePath, gameArguments, options);
            _logger.LogInformation("[DALAMUD-INJECT] EntryPoint arguments: {Args}", injectorArgs);

            var injectorPath = _pathService.InjectorPath;

            if (!injectorEnv.ContainsKey("WINEDEBUG"))
                injectorEnv["WINEDEBUG"] = "-all";

            var psi = new ProcessStartInfo
            {
                FileName = launcher.Executable,
                Arguments = launcher.BuildArguments($"\"{injectorPath}\" {injectorArgs}"),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(injectorPath)
            };

            var conflictingVars = new[] { "LD_PRELOAD", "SDL_VIDEODRIVER", "QT_QPA_PLATFORM", "APPDIR", "APPIMAGE", "ARGV0", "GSETTINGS_SCHEMA_DIR", "OWD" };
            foreach (var v in conflictingVars) psi.Environment.Remove(v);

            if (psi.Environment.TryGetValue("PATH", out var envPath))
                psi.Environment["PATH"] = string.Join(":", envPath.Split(':').Where(p => !p.Contains(".mount_") && !p.Contains("/tmp/.mount")));

            if (psi.Environment.TryGetValue("XDG_DATA_DIRS", out var envXdg))
                psi.Environment["XDG_DATA_DIRS"] = string.Join(":", envXdg.Split(':').Where(d => !d.Contains(".mount_") && !d.Contains("/tmp/.mount")));

            foreach (var (key, value) in injectorEnv)
                psi.Environment[key] = value;

            _logger.LogInformation("[DALAMUD-INJECT] Executing: {Exe} \"{Injector}\" {Args}",
                launcher.Executable, injectorPath, injectorArgs);

            var process = Process.Start(psi);
            if (process == null)
                return new DalamudInjectionResult { Success = false, ErrorMessage = "Failed to start injector process" };

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        var line = await process.StandardOutput.ReadLineAsync();
                        if (line != null) _logger.LogInformation("[DALAMUD-INJECT] stdout: {Line}", line);
                    }
                }
                catch { }
            });
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!process.StandardError.EndOfStream)
                    {
                        var line = await process.StandardError.ReadLineAsync();
                        if (line != null) _logger.LogWarning("[DALAMUD-INJECT] stderr: {Line}", line);
                    }
                }
                catch { }
            });

            _logger.LogInformation("[DALAMUD-INJECT] EntryPoint launch started; injector PID {Pid} tracks game lifetime", process.Id);

            if (OperatingSystem.IsMacOS())
            {
                _logger.LogInformation("[DALAMUD-INJECT] macOS: waiting for injector to finish bootstrapping...");
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(timeoutCts.Token); } catch (OperationCanceledException) { }

                var gameProcess = await FindGameProcessOnMacOSAsync(cancellationToken);
                if (gameProcess != null)
                {
                    _logger.LogInformation("[DALAMUD-INJECT] Found game process PID {Pid} after injector exit", gameProcess.Id);
                    return new DalamudInjectionResult { Success = true, InjectorProcess = gameProcess };
                }
                _logger.LogWarning("[DALAMUD-INJECT] Could not find game process after injector exit; tracking unavailable");
                return new DalamudInjectionResult { Success = true };
            }

            return new DalamudInjectionResult { Success = true, InjectorProcess = process };
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
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        try
        {
            var winePaths = WinePathService.Instance;
            var programFiles = Path.Combine(winePaths.PrefixDriveC, "Program Files");
            var dotnetLink = Path.Combine(programFiles, "dotnet");

            if (Directory.Exists(dotnetLink) || File.Exists(dotnetLink))
            {
                var linkTarget = Directory.ResolveLinkTarget(dotnetLink, true)?.FullName;
                if (linkTarget is not null &&
                    Path.GetFullPath(linkTarget) == Path.GetFullPath(runtimePath))
                {
                    _logger.LogDebug("[DALAMUD] C:\\Program Files\\dotnet symlink already correct: {Link} -> {Target}", dotnetLink, runtimePath);
                    return;
                }

                _logger.LogInformation("[DALAMUD] Removing existing C:\\Program Files\\dotnet entry to update symlink");
                if (Directory.ResolveLinkTarget(dotnetLink, false) is not null)
                    Directory.Delete(dotnetLink);
                else
                    Directory.Delete(dotnetLink, true);
            }

            Directory.CreateDirectory(programFiles);
            Directory.CreateSymbolicLink(dotnetLink, runtimePath);
            _logger.LogInformation("[DALAMUD] Created C:\\Program Files\\dotnet symlink: {Link} -> {Target}", dotnetLink, runtimePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DALAMUD] Failed to create C:\\Program Files\\dotnet symlink (non-fatal)");
        }
    }

    private async Task<int?> WaitForGameProcessAsync(
        WineLauncher launcher, 
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        _logger.LogInformation("[DALAMUD-INJECT] Waiting for game process...");
        
        for (int i = 0; i < ProcessDetectionMaxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();
            
            var pids = await GetWineProcessIdsAsync(launcher, environment, "ffxiv_dx11.exe");
            if (pids != null && pids.Length > 0)
            {
                var winePid = pids[pids.Length - 1];
                _logger.LogInformation("[DALAMUD-INJECT] Found {Count} ffxiv_dx11.exe process(es), using Wine PID: {WinePid}", 
                    pids.Length, winePid);
                return winePid;
            }
            
            _logger.LogDebug("[DALAMUD-INJECT] Game process not found, retry {Attempt}/{Max}...", 
                i + 1, ProcessDetectionMaxRetries);
            await Task.Delay(ProcessDetectionRetryDelayMs, ct);
        }
        
        return null;
    }

    private async Task<int[]?> GetWineProcessIdsAsync(
        WineLauncher launcher,
        Dictionary<string, string> environment,
        string executableName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = launcher.Executable,
                Arguments = launcher.BuildArguments("winedbg --command \"info proc\""),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
            
            using var process = Process.Start(psi);
            if (process == null) return null;
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            var matchingLines = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Contains(executableName))
                .Where(l => l.Length > 8)
                .ToList();
            
            var pids = matchingLines
                .Select(l => 
                {
                    if (l.Length > 8)
                    {
                        var pidStr = l.Substring(1, 8).Trim();
                        if (int.TryParse(pidStr, System.Globalization.NumberStyles.HexNumber, null, out var pid))
                        {
                            return (int?)pid;
                        }
                    }
                    return null;
                })
                .Where(pid => pid.HasValue)
                .Select(pid => pid!.Value)
                .ToArray();
            
            return pids.Length > 0 ? pids : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DALAMUD-INJECT] Failed to get Wine process IDs: {Message}", ex.Message);
            return null;
        }
    }

    private void AddDalamudEnvironment(Dictionary<string, string> env, DalamudInjectionOptions? options = null)
    {
        var runtimePath = _pathService.RuntimePath;
        var wineRuntimePath = $"Z:{runtimePath.Replace("/", "\\")}";
        env["DALAMUD_RUNTIME"] = wineRuntimePath;
        env["DOTNET_ROOT"] = wineRuntimePath;
        
        env["DOTNET_EnableWriteXorExecute"] = "0";
        env["COMPlus_EnableAlternateStackCheck"] = "0";
        env["COMPlus_gcAllowVeryLargeObjects"] = "1";

        if (!string.IsNullOrWhiteSpace(options?.PluginRepoUrl))
            env["DALAMUD_MAIN_REPO_URL"] = options.PluginRepoUrl;
        
        _logger.LogInformation("[DALAMUD-INJECT] Environment configured for Dalamud injection");
    }

    private void EnsureWineAppDataSymlink(Dictionary<string, string> environment)
    {
        try
        {
            if (!environment.TryGetValue("WINEPREFIX", out var winePrefix) || string.IsNullOrEmpty(winePrefix))
            {
                _logger.LogWarning("[DALAMUD-INJECT] WINEPREFIX not set, skipping AppData symlink");
                return;
            }
            
            var username = Environment.UserName;
            var appDataRoaming = Path.Combine(winePrefix, "drive_c", "users", username, "AppData", "Roaming");
            var xivLauncherTCPath = Path.Combine(appDataRoaming, "XIVLauncherTC");
            var targetConfigDir = _pathService.ConfigPath;
            
            Directory.CreateDirectory(targetConfigDir);
            
            var linkInfo = new FileInfo(xivLauncherTCPath);
            if (linkInfo.Exists || Directory.Exists(xivLauncherTCPath))
            {
                if (linkInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var currentTarget = Path.GetFullPath(Directory.ResolveLinkTarget(xivLauncherTCPath, true)?.FullName ?? "");
                    var expectedTarget = Path.GetFullPath(targetConfigDir);
                    if (currentTarget == expectedTarget) return;
                    Directory.Delete(xivLauncherTCPath);
                }
                else
                {
                    foreach (var file in Directory.GetFiles(xivLauncherTCPath))
                    {
                        var destFile = Path.Combine(targetConfigDir, Path.GetFileName(file));
                        if (!File.Exists(destFile)) File.Move(file, destFile);
                    }
                    foreach (var dir in Directory.GetDirectories(xivLauncherTCPath))
                    {
                        var destDir = Path.Combine(targetConfigDir, Path.GetFileName(dir));
                        if (!Directory.Exists(destDir)) Directory.Move(dir, destDir);
                    }
                    Directory.Delete(xivLauncherTCPath, true);
                }
            }
            
            Directory.CreateDirectory(appDataRoaming);
            Directory.CreateSymbolicLink(xivLauncherTCPath, targetConfigDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DALAMUD-INJECT] Failed to create AppData symlink (non-fatal)");
        }
    }

    private string BuildInjectorArguments(int gamePid, DalamudInjectionOptions options)
    {
        var sb = new StringBuilder();
        sb.Append($"inject {gamePid}");
        
        var workingDir = ConvertToWinePath(_pathService.HooksDevPath);
        sb.Append($" --dalamud-working-directory=\"{workingDir}\"");
        
        var configFile = ConvertToWinePath(_pathService.DalamudConfigPath);
        sb.Append($" --dalamud-configuration-path=\"{configFile}\"");
        
        var logDir = ConvertToWinePath(_pathService.LogPath);
        sb.Append($" --logpath=\"{logDir}\"");
        
        var pluginDir = ConvertToWinePath(_pathService.PluginsPath);
        sb.Append($" --dalamud-plugin-directory=\"{pluginDir}\"");
        
        var assetDir = ConvertToWinePath(_pathService.AssetsDevPath);
        sb.Append($" --dalamud-asset-directory=\"{assetDir}\"");
        
        sb.Append($" --dalamud-client-language={ClientLanguageChinese}");
        
        var delayInit = options.DelayInitializeMs ?? DefaultInjectionDelayMs;
        sb.Append($" --dalamud-delay-initialize={delayInit}");
        
        if (options.NoPlugin) sb.Append(" --no-plugin");
        if (options.NoThirdPartyPlugin) sb.Append(" --no-3rd-plugin");
        
        return sb.ToString();
    }

    private string BuildEntryPointArguments(string gameExeWinePath, string gameArguments, DalamudInjectionOptions options)
    {
        var sb = new StringBuilder();
        sb.Append($"launch -g \"{gameExeWinePath}\" -m entrypoint");

        var workingDir = ConvertToWinePath(_pathService.HooksDevPath);
        sb.Append($" --dalamud-working-directory=\"{workingDir}\"");

        var configFile = ConvertToWinePath(_pathService.DalamudConfigPath);
        sb.Append($" --dalamud-configuration-path=\"{configFile}\"");

        var logDir = ConvertToWinePath(_pathService.LogPath);
        sb.Append($" --logpath=\"{logDir}\"");

        var pluginDir = ConvertToWinePath(_pathService.PluginsPath);
        sb.Append($" --dalamud-plugin-directory=\"{pluginDir}\"");

        var assetDir = ConvertToWinePath(_pathService.AssetsDevPath);
        sb.Append($" --dalamud-asset-directory=\"{assetDir}\"");

        sb.Append($" --dalamud-client-language={ClientLanguageChinese}");
        sb.Append(" --dalamud-delay-initialize=0");

        if (options.NoPlugin) sb.Append(" --no-plugin");
        if (options.NoThirdPartyPlugin) sb.Append(" --no-3rd-plugin");

        sb.Append($" -- {gameArguments}");
        return sb.ToString();
    }

    private static string ConvertToWinePath(string unixPath)
    {
        return "Z:" + unixPath.Replace("/", "\\");
    }

    private async Task<DalamudInjectionResult> ExecuteInjectorAsync(
        WineLauncher launcher,
        string arguments,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var injectorPath = _pathService.InjectorPath;
        if (!environment.ContainsKey("WINEDEBUG"))
        {
            environment["WINEDEBUG"] = "-all";
        }
        
        var psi = new ProcessStartInfo
        {
            FileName = launcher.Executable,
            Arguments = launcher.BuildArguments($"\"{injectorPath}\" {arguments}"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(injectorPath)
        };
        
        var conflictingVars = new[] { "LD_PRELOAD", "SDL_VIDEODRIVER", "QT_QPA_PLATFORM", "APPDIR", "APPIMAGE", "ARGV0", "GSETTINGS_SCHEMA_DIR", "OWD" };
        foreach (var varName in conflictingVars) psi.Environment.Remove(varName);
        
        if (psi.Environment.ContainsKey("PATH"))
        {
            var path = psi.Environment["PATH"];
            var paths = path.Split(':').Where(p => !p.Contains(".mount_") && !p.Contains("/tmp/.mount")).ToArray();
            psi.Environment["PATH"] = string.Join(":", paths);
        }
        
        if (psi.Environment.ContainsKey("XDG_DATA_DIRS"))
        {
            var xdgData = psi.Environment["XDG_DATA_DIRS"];
            var dirs = xdgData.Split(':').Where(d => !d.Contains(".mount_") && !d.Contains("/tmp/.mount")).ToArray();
            psi.Environment["XDG_DATA_DIRS"] = string.Join(":", dirs);
        }
        
        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }
        
        _logger.LogInformation("[DALAMUD-INJECT] Executing: {Exe} \"{Injector}\" {Args}", 
            launcher.Executable, injectorPath, arguments);
        
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        
        using var process = Process.Start(psi);
        if (process == null)
            return new DalamudInjectionResult { Success = false, ErrorMessage = "Failed to start injector process" };
        
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
            return new DalamudInjectionResult { Success = false, ErrorMessage = "Injector timeout" };
        }
        
        var exitCode = process.ExitCode;
        if (exitCode == 0)
        {
            return new DalamudInjectionResult { Success = true, ExitCode = exitCode, StdOut = stdout.ToString() };
        }
        else
        {
            var errorMsg = stderr.Length > 0 ? stderr.ToString() : stdout.ToString();
            return new DalamudInjectionResult { Success = false, ExitCode = exitCode, ErrorMessage = $"Injector exited with code {exitCode}: {errorMsg}" };
        }
    }

    private async Task<Process?> FindGameProcessOnMacOSAsync(CancellationToken ct)
    {
        for (int i = 0; i < ProcessDetectionMaxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/pgrep",
                Arguments = "-f ffxiv_dx11",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);

                var pid = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => int.TryParse(l.Trim(), out var p) ? (int?)p : null)
                    .Where(p => p.HasValue)
                    .Select(p => p!.Value)
                    .OrderByDescending(p => p)
                    .FirstOrDefault();

                if (pid > 0)
                {
                    try
                    {
                        return Process.GetProcessById(pid);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[DALAMUD-INJECT] pgrep returned PID {Pid} but failed to attach", pid);
                    }
                }
            }

            await Task.Delay(ProcessDetectionRetryDelayMs, ct);
        }
        return null;
    }
}
