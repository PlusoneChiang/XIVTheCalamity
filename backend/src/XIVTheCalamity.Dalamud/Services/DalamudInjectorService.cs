using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Dalamud.Services;

/// <summary>
/// Dalamud injection service
/// Injects Dalamud into game process after game launch
/// Supports both Wine-based injection (macOS/Linux) and native injection (Windows)
/// </summary>
public class DalamudInjectorService
{
    private readonly ILogger<DalamudInjectorService> _logger;
    private readonly DalamudPathService _pathService;
    
    // Default values
    private const int DefaultInjectionDelayMs = 5000;
    private const int ProcessDetectionMaxRetries = 10;
    private const int ProcessDetectionRetryDelayMs = 500;
    private const int InjectorTimeoutMs = 60000;
    
    // Taiwan server language code
    private const int ClientLanguageChinese = 5;
    
    public DalamudInjectorService(
        ILogger<DalamudInjectorService> logger,
        DalamudPathService pathService)
    {
        _logger = logger;
        _pathService = pathService;
    }
    
    /// <summary>
    /// Inject Dalamud into game process (Wine-based, for macOS/Linux)
    /// </summary>
    /// <param name="launcher">Wine launcher command</param>
    /// <param name="environment">Wine environment variables</param>
    /// <param name="options">Injection options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<DalamudInjectionResult> InjectAsync(
        WineLauncher launcher,
        Dictionary<string, string> environment,
        DalamudInjectionOptions options,
        CancellationToken cancellationToken = default)
    {
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
            
                        // Prepare environment variables (add DALAMUD_RUNTIME first, as winedbg needs same environment)
            var injectorEnv = new Dictionary<string, string>(environment);
            AddDalamudEnvironment(injectorEnv, options);
            
            // Ensure Wine %APPDATA%/XIVLauncherTC symlink points to our Config directory
            // Dalamud hardcodes %APPDATA%/XIVLauncherTC for safe mode, log commands, etc.
            EnsureWineAppDataSymlink(injectorEnv);

            // Linux mitigation: force fresh signature scan each launch.
            // This avoids reusing stale cached callsite signatures that can crash Reloaded.AsmHook.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                ClearLinuxCachedSignatures();
            }
            
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
    
    /// <summary>
    /// Inject Dalamud into game process (native Windows, no Wine)
    /// </summary>
    /// <param name="options">Injection options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<DalamudInjectionResult> InjectNativeAsync(
        DalamudInjectionOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[DALAMUD-INJECT] Starting Dalamud injection (Windows native)...");
            
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
            
            // Wait for game process (using .NET Process API)
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
            
            // Wait for injection delay
            var delayMs = options.InjectionDelayMs ?? DefaultInjectionDelayMs;
            _logger.LogInformation("[DALAMUD-INJECT] Waiting {Delay}ms before injection...", delayMs);
            await Task.Delay(delayMs, cancellationToken);
            
            // Build injector arguments (Windows native paths, no Wine conversion)
            var injectorArgs = BuildInjectorArgumentsWindows(gamePid.Value, options);
            _logger.LogInformation("[DALAMUD-INJECT] Injector arguments: {Args}", injectorArgs);
            
            // Build environment variables
            var injectorEnv = BuildInjectorEnvironmentWindows(options);
            
            // Execute injection (directly, no Wine)
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

    /// <summary>
    /// Launch game with Dalamud loaded at entry point (Wine-based, macOS/Linux).
    /// Dalamud.Injector starts the game directly using "launch -m entrypoint".
    /// </summary>
    public async Task<DalamudInjectionResult> LaunchWithEntryPointAsync(
        WineLauncher launcher,
        string gameExePath,
        string gameArguments,
        Dictionary<string, string> environment,
        DalamudInjectionOptions options,
        CancellationToken cancellationToken = default)
    {
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

            // Without --no-wait, the injector stays alive until the game exits (bwrap keeps the
            // pressure-vessel container alive). Use this process as a game-lifetime proxy.
            // NOTE: no `using` — intentionally kept alive
            var process = Process.Start(psi);
            if (process == null)
                return new DalamudInjectionResult { Success = false, ErrorMessage = "Failed to start injector process" };

            // Drain stderr to prevent pipe-buffer deadlock (game inherits injector's pipes)
            _ = Task.Run(async () =>
            {
                try { while (!process.StandardOutput.EndOfStream) await process.StandardOutput.ReadLineAsync(); } catch { }
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

            // On macOS, wine64 is a thin wrapper that exits after spawning the game inside Wine.
            // We must wait for the injector to finish, then find the actual game OS process.
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

    /// <summary>
    /// Launch game with Dalamud loaded at entry point (native Windows).
    /// Dalamud.Injector starts the game directly using "launch -m entrypoint".
    /// </summary>
    public async Task<DalamudInjectionResult> LaunchWithEntryPointNativeAsync(
        string gameExePath,
        string gameArguments,
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
            var injectorEnv = BuildInjectorEnvironmentWindows(options);

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

    /// <summary>
    /// <summary>
    /// <summary>
    /// Resolve the Linux game PID from Dalamud.Injector's JSON stdout output.
    /// Dalamud.Injector writes {"pid": &lt;WinePID&gt;, "handle": ...} to stdout when it starts the game.
    /// We parse the Wine PID, then use winedbg "info procmap" (same as XIVLauncher) to get the real Linux PID.
    /// Falls back to /proc cmdline scan if winedbg fails.
    /// </summary>
    private async Task<int?> TryGetGamePidFromEntryPointOutputAsync(
        WineLauncher launcher,
        Dictionary<string, string> environment,
        string? injectorStdOut,
        CancellationToken ct)
    {
        // Step 1: parse Wine PID from {"pid": X, "handle": Y}
        int? winePid = null;
        if (!string.IsNullOrWhiteSpace(injectorStdOut))
        {
            foreach (var line in injectorStdOut.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("{\"pid\":", StringComparison.Ordinal))
                    continue;
                try
                {
                    // Minimal JSON parse: extract numeric value after "pid":
                    var afterKey = trimmed.AsSpan(trimmed.IndexOf(':') + 1);
                    var comma = afterKey.IndexOf(',');
                    var pidStr = (comma >= 0 ? afterKey[..comma] : afterKey).Trim();
                    if (int.TryParse(pidStr, out var parsed))
                    {
                        winePid = parsed;
                        _logger.LogInformation("[DALAMUD-INJECT] Parsed Wine PID from injector output: {WinePid}", winePid);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DALAMUD-INJECT] Failed to parse Wine PID from line: {Line}", trimmed);
                }
                break;
            }
        }

        // Step 2: winedbg "info procmap" — Wine PID → Linux PID (XIVLauncher approach)
        if (winePid.HasValue)
        {
            var linuxPid = await GetUnixProcessIdAsync(launcher, environment, winePid.Value, ct);
            if (linuxPid.HasValue)
                return linuxPid.Value;

            _logger.LogWarning("[DALAMUD-INJECT] winedbg procmap could not resolve Wine PID {WinePid} to Linux PID", winePid.Value);
        }
        else
        {
            _logger.LogWarning("[DALAMUD-INJECT] No Wine PID found in injector stdout; falling back to /proc scan");
        }

        // Step 3: fallback — scan /proc for ffxiv_dx11.exe in cmdline
        return await TryFindGamePidFromProcAsync(ct);
    }

    /// <summary>
    /// Convert a Wine process ID to the Linux process ID using winedbg "info procmap".
    /// Output format per line: " WWWWWWWW UUUUUUUU processname" (hex values)
    /// Same approach as XIVLauncher's CompatibilityTools.GetUnixProcessId().
    /// </summary>
    private async Task<int?> GetUnixProcessIdAsync(
        WineLauncher launcher,
        Dictionary<string, string> environment,
        int winePid,
        CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = launcher.Executable,
                Arguments = launcher.BuildArguments("winedbg --command \"info procmap\""),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;

            _logger.LogDebug("[DALAMUD-INJECT] Running: winedbg --command \"info procmap\" to resolve Wine PID {WinePid}", winePid);

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (output.Contains("syntax error"))
            {
                _logger.LogWarning("[DALAMUD-INJECT] winedbg info procmap returned syntax error");
                return null;
            }

            // Each data line: " WWWWWWWW UUUUUUUU processname"
            // position 1..8 = Wine PID hex, position 10..17 = Unix PID hex
            foreach (var line in output.Split('\n').Skip(1))
            {
                if (line.Length < 18) continue;
                if (!int.TryParse(line.Substring(1, 8), System.Globalization.NumberStyles.HexNumber, null, out var linWinePid))
                    continue;
                if (linWinePid != winePid) continue;
                if (!int.TryParse(line.Substring(10, 8), System.Globalization.NumberStyles.HexNumber, null, out var unixPid))
                    continue;
                _logger.LogInformation("[DALAMUD-INJECT] Resolved Wine PID {WinePid:X} → Linux PID {UnixPid}", winePid, unixPid);
                return unixPid;
            }

            _logger.LogDebug("[DALAMUD-INJECT] Wine PID {WinePid} not found in procmap output", winePid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DALAMUD-INJECT] winedbg procmap failed: {Message}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// macOS: use pgrep to find the wine process running ffxiv_dx11.exe, retrying until found.
    /// </summary>
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
                        var gameProcess = Process.GetProcessById(pid);
                        _logger.LogInformation("[DALAMUD-INJECT] Found ffxiv_dx11.exe via pgrep, macOS PID: {Pid}", pid);
                        return gameProcess;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[DALAMUD-INJECT] pgrep returned PID {Pid} but failed to attach", pid);
                    }
                }
            }

            _logger.LogDebug("[DALAMUD-INJECT] ffxiv_dx11.exe not found via pgrep, retry {Attempt}/{Max}...", i + 1, ProcessDetectionMaxRetries);
            await Task.Delay(ProcessDetectionRetryDelayMs, ct);
        }

        return null;
    }

    /// <summary>
    /// Fallback: scan /proc/*/cmdline for ffxiv_dx11.exe to find its Linux PID.
    /// </summary>
    private async Task<int?> TryFindGamePidFromProcAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        for (int i = 0; i < ProcessDetectionMaxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();

            var pid = FindGamePidFromProc();
            if (pid.HasValue)
            {
                _logger.LogInformation("[DALAMUD-INJECT] Found ffxiv_dx11.exe via /proc scan, Linux PID: {Pid}", pid.Value);
                return pid.Value;
            }

            _logger.LogDebug("[DALAMUD-INJECT] ffxiv_dx11.exe not in /proc, retry {Attempt}/{Max}...", i + 1, ProcessDetectionMaxRetries);
            await Task.Delay(ProcessDetectionRetryDelayMs, ct);
        }

        return null;
    }

    private static int? FindGamePidFromProc()
    {
        // Return the newest (highest PID) ffxiv_dx11.exe.
        // Wine uses prctl(PR_SET_NAME) to set the process comm to the exe name,
        // so we read /proc/[pid]/comm — same as what 'pgrep ffxiv_dx11.exe' does.
        // cmdline may still contain wine64 as argv[0], so comm is more reliable.
        int? newestPid = null;
        try
        {
            foreach (var procDir in Directory.GetDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(procDir), out var pid))
                    continue;

                var commPath = Path.Combine(procDir, "comm");
                if (!File.Exists(commPath))
                    continue;

                var comm = File.ReadAllText(commPath).Trim();
                if (comm.Equals("ffxiv_dx11.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (!newestPid.HasValue || pid > newestPid.Value)
                        newestPid = pid;
                }
            }
        }
        catch { /* /proc entries may disappear mid-scan */ }

        return newestPid;
    }

    /// <summary>
    /// Wait for game process to appear (using winedbg "info proc") — used by Inject mode.
    /// </summary>
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
                // Get last (newest) process
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
    
    /// <summary>
    /// Get Wine process PID using winedbg
    /// winedbg is a Windows program, must be executed via Wine
    /// </summary>
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
            
            // Do NOT clear environment - inherit parent environment and override Wine variables
            // This preserves system library loader variables that .NET Runtime needs
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
            
            _logger.LogDebug("[DALAMUD-INJECT] Running: {Exe} winedbg --command \"info proc\"", launcher.Executable);
            
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("[DALAMUD-INJECT] Failed to start winedbg process");
                return null;
            }
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            _logger.LogDebug("[DALAMUD-INJECT] winedbg output ({Length} chars)", output.Length);
            
            // Parse output, find matching processes
            // Format: " 00000084 0 ffxiv_dx11.exe"
            // PID at position 1-8 (hexadecimal)
            var matchingLines = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Contains(executableName))
                .Where(l => l.Length > 8)
                .ToList();
            
            if (matchingLines.Count > 0)
            {
                _logger.LogDebug("[DALAMUD-INJECT] Found {Count} matching lines for {Exe}", 
                    matchingLines.Count, executableName);
            }
            
            var pids = matchingLines
                .Select(l => 
                {
                    // Try to parse hexadecimal PID
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
    
    /// <summary>
    /// Add Dalamud environment variables
    /// CRITICAL: Must use Wine Z:\ path format for DALAMUD_RUNTIME and DOTNET_ROOT
    /// Dalamud passes this path to hostfxr, which needs Windows-style path in Wine
    /// </summary>
    private void AddDalamudEnvironment(Dictionary<string, string> env, DalamudInjectionOptions? options = null)
    {
        var runtimePath = _pathService.RuntimePath;
        
        // Convert Unix path to Wine Z:\ path
        // Dalamud.Boot will pass this to hostfxr, which expects Windows paths
        var wineRuntimePath = $"Z:{runtimePath.Replace("/", "\\")}";
        env["DALAMUD_RUNTIME"] = wineRuntimePath;
        env["DOTNET_ROOT"] = wineRuntimePath;  // XIVLauncher.Core sets this
        
        // Important: .NET Runtime configuration
        env["DOTNET_EnableWriteXorExecute"] = "0";  // Disable W^X for Apple Silicon compatibility
        env["COMPlus_EnableAlternateStackCheck"] = "0";  // Disable stack checks that may fail in Wine
        env["COMPlus_gcAllowVeryLargeObjects"] = "1";  // Allow large objects

        if (!string.IsNullOrWhiteSpace(options?.PluginRepoUrl))
            env["DALAMUD_MAIN_REPO_URL"] = options.PluginRepoUrl;
        
        _logger.LogInformation("[DALAMUD-INJECT] Environment configured for Dalamud injection");
    }
    
    /// <summary>
    /// Ensure Wine %APPDATA%/XIVLauncherTC is a symlink pointing to our Dalamud Config directory.
    /// Dalamud hardcodes paths like %APPDATA%/XIVLauncherTC/.dalamud_safemode and
    /// %APPDATA%/XIVLauncherTC/dalamud.log internally. This symlink ensures those
    /// accesses resolve to the same directory used by --dalamud-configuration-path.
    /// </summary>
    private void EnsureWineAppDataSymlink(Dictionary<string, string> environment)
    {
        try
        {
            if (!environment.TryGetValue("WINEPREFIX", out var winePrefix) || string.IsNullOrEmpty(winePrefix))
            {
                _logger.LogWarning("[DALAMUD-INJECT] WINEPREFIX not set, skipping AppData symlink");
                return;
            }
            
            // Wine maps %APPDATA% to drive_c/users/{username}/AppData/Roaming
            var username = Environment.UserName;
            var appDataRoaming = Path.Combine(winePrefix, "drive_c", "users", username, "AppData", "Roaming");
            var xivLauncherTCPath = Path.Combine(appDataRoaming, "XIVLauncherTC");
            var targetConfigDir = _pathService.ConfigPath;
            
            // Ensure the config directory exists
            Directory.CreateDirectory(targetConfigDir);
            
            // Check current state
            var linkInfo = new FileInfo(xivLauncherTCPath);
            if (linkInfo.Exists || Directory.Exists(xivLauncherTCPath))
            {
                // If it's already a symlink pointing to our config dir, we're done
                if (linkInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var currentTarget = Path.GetFullPath(Directory.ResolveLinkTarget(xivLauncherTCPath, true)?.FullName ?? "");
                    var expectedTarget = Path.GetFullPath(targetConfigDir);
                    if (currentTarget == expectedTarget)
                    {
                        _logger.LogDebug("[DALAMUD-INJECT] AppData symlink already correct: {Link} -> {Target}", xivLauncherTCPath, targetConfigDir);
                        return;
                    }
                    
                    // Symlink points elsewhere, remove and recreate
                    _logger.LogInformation("[DALAMUD-INJECT] Updating AppData symlink target from {Old} to {New}", currentTarget, expectedTarget);
                    Directory.Delete(xivLauncherTCPath);
                }
                else
                {
                    // It's a real directory — move its contents to our config dir, then replace with symlink
                    _logger.LogInformation("[DALAMUD-INJECT] Migrating existing XIVLauncherTC directory to config path");
                    foreach (var file in Directory.GetFiles(xivLauncherTCPath))
                    {
                        var destFile = Path.Combine(targetConfigDir, Path.GetFileName(file));
                        if (!File.Exists(destFile))
                        {
                            File.Move(file, destFile);
                        }
                    }
                    foreach (var dir in Directory.GetDirectories(xivLauncherTCPath))
                    {
                        var destDir = Path.Combine(targetConfigDir, Path.GetFileName(dir));
                        if (!Directory.Exists(destDir))
                        {
                            Directory.Move(dir, destDir);
                        }
                    }
                    Directory.Delete(xivLauncherTCPath, true);
                }
            }
            
            // Ensure parent directory exists
            Directory.CreateDirectory(appDataRoaming);
            
            // Create symlink: XIVLauncherTC -> our Config directory
            Directory.CreateSymbolicLink(xivLauncherTCPath, targetConfigDir);
            _logger.LogInformation("[DALAMUD-INJECT] Created AppData symlink: {Link} -> {Target}", xivLauncherTCPath, targetConfigDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DALAMUD-INJECT] Failed to create AppData symlink (non-fatal)");
        }
    }

    /// <summary>
    /// Clear cached Dalamud signature files on Linux (except cs.json).
    /// This is a defensive mitigation for intermittent Reloaded.AsmHook crashes on relaunch.
    /// </summary>
    private void ClearLinuxCachedSignatures()
    {
        var cacheDir = Path.Combine(_pathService.HooksDevPath, "cachedSigs");
        if (!Directory.Exists(cacheDir))
        {
            _logger.LogDebug("[DALAMUD-INJECT] cachedSigs directory not found: {CacheDir}", cacheDir);
            return;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(cacheDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "cs.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deleted++;
                _logger.LogWarning("[DALAMUD-INJECT] Cleared cached signature file: {File}", file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DALAMUD-INJECT] Failed to remove cached signature file: {File}", file);
            }
        }

        if (deleted > 0)
        {
            _logger.LogWarning("[DALAMUD-INJECT] Cleared {Count} cached signature file(s) in {CacheDir}", deleted, cacheDir);
        }
        else
        {
            _logger.LogDebug("[DALAMUD-INJECT] No removable cached signature files found in {CacheDir}", cacheDir);
        }
    }
    
    /// <summary>
    /// Build injector arguments
    /// </summary>
    private string BuildInjectorArguments(int gamePid, DalamudInjectionOptions options)
    {
        var sb = new StringBuilder();
        
        // inject command and PID
        sb.Append($"inject {gamePid}");
        
        // Working directory (Hooks/dev)
        var workingDir = ConvertToWinePath(_pathService.HooksDevPath);
        sb.Append($" --dalamud-working-directory=\"{workingDir}\"");
        
        // Configuration file path (Dalamud expects a file path, not a directory)
        var configFile = ConvertToWinePath(_pathService.DalamudConfigPath);
        sb.Append($" --dalamud-configuration-path=\"{configFile}\"");
        
        // Log directory
        var logDir = ConvertToWinePath(_pathService.LogPath);
        sb.Append($" --logpath=\"{logDir}\"");
        
        // Plugin directory
        var pluginDir = ConvertToWinePath(_pathService.PluginsPath);
        sb.Append($" --dalamud-plugin-directory=\"{pluginDir}\"");
        
        // Assets directory
        var assetDir = ConvertToWinePath(_pathService.AssetsDevPath);
        sb.Append($" --dalamud-asset-directory=\"{assetDir}\"");
        
        // Language setting (Taiwan server = 4)
        sb.Append($" --dalamud-client-language={ClientLanguageChinese}");
        
        // Delay initialization
        var delayInit = options.DelayInitializeMs ?? DefaultInjectionDelayMs;
        sb.Append($" --dalamud-delay-initialize={delayInit}");
        
        // Safe Mode 選項
        if (options.NoPlugin)
        {
            sb.Append(" --no-plugin");
        }
        if (options.NoThirdPartyPlugin)
        {
            sb.Append(" --no-3rd-plugin");
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Convert Unix path to Wine path
    /// /Users/xxx/path → Z:\Users\xxx\path
    /// </summary>
    private static string ConvertToWinePath(string unixPath)
    {
        // Convert Unix path to Wine Z: drive path
        return "Z:" + unixPath.Replace("/", "\\");
    }
    
    /// <summary>
    /// Execute injector
    /// </summary>
    private async Task<DalamudInjectionResult> ExecuteInjectorAsync(
        WineLauncher launcher,
        string arguments,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var injectorPath = _pathService.InjectorPath;
        
        // WINEDEBUG comes from environment service (configured in settings)
        // Don't override it here unless it's not set
        if (!environment.ContainsKey("WINEDEBUG"))
        {
            environment["WINEDEBUG"] = "-all";  // Default: suppress all Wine debug output
        }
        
        var psi = new ProcessStartInfo
        {
            FileName = launcher.Executable,
            Arguments = launcher.BuildArguments($"\"{injectorPath}\" {arguments}"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(injectorPath) // Set working directory to Dalamud.Injector.exe location
        };
        
        // Remove potentially conflicting variables first
        var conflictingVars = new[] 
        { 
            "LD_PRELOAD", "SDL_VIDEODRIVER", "QT_QPA_PLATFORM",
            // AppImage variables that conflict with Wine
            "APPDIR", "APPIMAGE", "ARGV0", "GSETTINGS_SCHEMA_DIR", "OWD"
        };
        foreach (var varName in conflictingVars)
        {
            psi.Environment.Remove(varName);
        }
        
        // Clean PATH - remove AppImage mount point
        if (psi.Environment.ContainsKey("PATH"))
        {
            var path = psi.Environment["PATH"];
            var paths = path.Split(':')
                .Where(p => !p.Contains(".mount_") && !p.Contains("/tmp/.mount"))
                .ToArray();
            psi.Environment["PATH"] = string.Join(":", paths);
        }
        
        // Clean XDG_DATA_DIRS - remove AppImage mount point
        if (psi.Environment.ContainsKey("XDG_DATA_DIRS"))
        {
            var xdgData = psi.Environment["XDG_DATA_DIRS"];
            var dirs = xdgData.Split(':')
                .Where(d => !d.Contains(".mount_") && !d.Contains("/tmp/.mount"))
                .ToArray();
            psi.Environment["XDG_DATA_DIRS"] = string.Join(":", dirs);
        }
        
        // Override with Wine environment variables
        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }
        
        // Remove Wine debug now that we found the issue
        // psi.Environment["WINEDEBUG"] = "+loaddll,+process";
        
        // DEBUG: Log ALL environment variables - REMOVE IN PRODUCTION
        /* 
        _logger.LogWarning("[DALAMUD-INJECT] === ALL ENVIRONMENT VARIABLES ===");
        foreach (var kvp in psi.Environment.OrderBy(x => x.Key))
        {
            _logger.LogWarning("[DALAMUD-INJECT] {Key}={Value}", kvp.Key, kvp.Value);
        }
        _logger.LogWarning("[DALAMUD-INJECT] === END ENVIRONMENT ===");
        */
        
        // Log environment variables for debugging
        _logger.LogDebug("[DALAMUD-INJECT] Environment variables:");
        foreach (var (key, value) in psi.Environment)
        {
            if (key.Contains("WINE") || key.Contains("DALAMUD") || key.Contains("DOTNET") || 
                key.Contains("LD_LIBRARY") || key.Contains("VKD3D"))
            {
                _logger.LogDebug("[DALAMUD-INJECT]   {Key}={Value}", key, value);
            }
        }
        
        // Log exit code explicitly for debugging
        _logger.LogDebug("[DALAMUD-INJECT] Total environment variables: {Count}", psi.Environment.Count);
        
        _logger.LogInformation("[DALAMUD-INJECT] Executing: {Exe} \"{Injector}\" {Args}", 
            launcher.Executable, injectorPath, arguments);
        
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        
        using var process = Process.Start(psi);
        if (process == null)
        {
            return new DalamudInjectionResult
            {
                Success = false,
                ErrorMessage = "Failed to start injector process"
            };
        }
        
        // Read output asynchronously
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
                    // Log ALL stderr output, not just debug level
                    _logger.LogWarning("[DALAMUD-INJECT] stderr: {Line}", line);
                }
            }
        }, ct);
        
        // Wait for completion (with timeout)
        using var timeoutCts = new CancellationTokenSource(InjectorTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            
            // In EntryPoint mode, the game process may inherit the injector's stdout/stderr
            // pipe handles, keeping them open until the game exits. Drain with a short timeout.
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
    
    #region Windows Native Injection
    
    /// <summary>
    /// Wait for game process to appear (Windows native, using Process API)
    /// </summary>
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
                
                // Dispose all process handles
                foreach (var p in processes) p.Dispose();
                
                return pid;
            }
            
            _logger.LogDebug("[DALAMUD-INJECT] Game process not found, retry {Attempt}/{Max}...", 
                i + 1, ProcessDetectionMaxRetries);
            await Task.Delay(ProcessDetectionRetryDelayMs, ct);
        }
        
        return null;
    }
    
    /// <summary>
    /// Build injector arguments for Windows (native paths, no Wine conversion)
    /// </summary>
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
        
        if (options.NoPlugin)
        {
            sb.Append(" --no-plugin");
        }
        if (options.NoThirdPartyPlugin)
        {
            sb.Append(" --no-3rd-plugin");
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Build environment variables for Windows native injection
    /// </summary>
    private Dictionary<string, string> BuildInjectorEnvironmentWindows(DalamudInjectionOptions? options = null)
    {
        var env = new Dictionary<string, string>();
        
        var runtimePath = _pathService.RuntimePath;
        env["DALAMUD_RUNTIME"] = runtimePath;
        env["DOTNET_ROOT"] = runtimePath;

        if (!string.IsNullOrWhiteSpace(options?.PluginRepoUrl))
            env["DALAMUD_MAIN_REPO_URL"] = options.PluginRepoUrl;
        
        _logger.LogInformation("[DALAMUD-INJECT] Windows environment: DALAMUD_RUNTIME={RuntimePath}", runtimePath);
        
        return env;
    }

    /// <summary>
    /// Build injector arguments for "launch -m entrypoint" (Wine path variant)
    /// </summary>
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

        // No initialization delay — Dalamud loads before the game loop starts
        sb.Append(" --dalamud-delay-initialize=0");

        if (options.NoPlugin)
            sb.Append(" --no-plugin");
        if (options.NoThirdPartyPlugin)
            sb.Append(" --no-3rd-plugin");

        sb.Append($" -- {gameArguments}");

        return sb.ToString();
    }

    /// <summary>
    /// Build injector arguments for "launch -m entrypoint" (Windows native path variant)
    /// </summary>
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

        if (options.NoPlugin)
            sb.Append(" --no-plugin");
        if (options.NoThirdPartyPlugin)
            sb.Append(" --no-3rd-plugin");

        sb.Append($" -- {gameArguments}");

        return sb.ToString();
    }
    
    /// <summary>
    /// Execute Dalamud.Injector.exe directly on Windows (no Wine)
    /// </summary>
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
        
        // Set environment variables
        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }
        
        // Log relevant environment variables
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
        
        // Read output asynchronously
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
        
        // Wait for completion (with timeout)
        using var timeoutCts = new CancellationTokenSource(InjectorTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            
            // In EntryPoint mode, the game process may inherit the injector's stdout/stderr
            // pipe handles, keeping them open until the game exits. Drain with a short timeout.
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
    
    #endregion
}
public class DalamudInjectionOptions
{
    /// <summary>Wait time before injection (milliseconds)</summary>
    public int? InjectionDelayMs { get; set; }
    
    /// <summary>Dalamud delay initialization time (milliseconds)</summary>
    public int? DelayInitializeMs { get; set; }
    
    /// <summary>Do not load any plugins</summary>
    public bool NoPlugin { get; set; }
    
    /// <summary>Do not load third-party plugins</summary>
    public bool NoThirdPartyPlugin { get; set; }

    /// <summary>Main plugin repository URL (passed as DALAMUD_MAIN_REPO_URL)</summary>
    public string? PluginRepoUrl { get; set; }
}

/// <summary>
/// Injection result
/// </summary>
public class DalamudInjectionResult
{
    public bool Success { get; set; }
    public int? ExitCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Game process PID — populated by EntryPoint mode after Dalamud.Injector starts the game.
    /// </summary>
    public int? GamePid { get; set; }

    /// <summary>
    /// The umu-run process handle (valid only when running without --no-wait).
    /// The injector stays alive until the game exits, so this process acts as a
    /// game-lifetime proxy — monitor it instead of scanning /proc.
    /// </summary>
    public Process? InjectorProcess { get; set; }

    /// <summary>
    /// Raw stdout from the injector process. In EntryPoint mode contains the JSON line
    /// {"pid": &lt;WinePID&gt;, "handle": &lt;handle&gt;} output by Dalamud.Injector.
    /// </summary>
    public string? StdOut { get; set; }
}
