using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Dalamud.Services;

/// <summary>
/// Dalamud injection service
/// Injects Dalamud into game process after game launch
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
    private const int ClientLanguageChinese = 4;
    
    public DalamudInjectorService(
        ILogger<DalamudInjectorService> logger,
        DalamudPathService pathService)
    {
        _logger = logger;
        _pathService = pathService;
    }
    
    /// <summary>
    /// Inject Dalamud into game process
    /// </summary>
    /// <param name="winePath">Wine executable path</param>
    /// <param name="environment">Wine environment variables</param>
    /// <param name="options">Injection options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<DalamudInjectionResult> InjectAsync(
        string winePath,
        Dictionary<string, string> environment,
        DalamudInjectionOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[DALAMUD-INJECT] Starting Dalamud injection...");
            
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
            AddDalamudEnvironment(injectorEnv, winePath);
            
            // Wait for game process (using winedbg)
            var gamePid = await WaitForGameProcessAsync(winePath, injectorEnv, cancellationToken);
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
            var result = await ExecuteInjectorAsync(winePath, injectorArgs, injectorEnv, cancellationToken);
            
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
    /// Wait for game process to appear (using winedbg)
    /// </summary>
    private async Task<int?> WaitForGameProcessAsync(
        string winePath, 
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        _logger.LogInformation("[DALAMUD-INJECT] Waiting for game process...");
        
        for (int i = 0; i < ProcessDetectionMaxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();
            
            var pids = await GetWineProcessIdsAsync(winePath, environment, "ffxiv_dx11.exe");
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
        string winePath,
        Dictionary<string, string> environment,
        string executableName)
    {
        try
        {
            // winedbg must be executed via wine64
            var psi = new ProcessStartInfo
            {
                FileName = winePath,
                Arguments = "winedbg --command \"info proc\"",
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
            
            _logger.LogDebug("[DALAMUD-INJECT] Running: {Wine} winedbg --command \"info proc\"", winePath);
            
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
    private void AddDalamudEnvironment(Dictionary<string, string> env, string winePath)
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
        
        // Enable detailed .NET Core Host tracing for debugging
        env["COREHOST_TRACE"] = "1";
        env["COREHOST_TRACEFILE"] = $"{_pathService.LogPath}/corehost.log";
        
        // CRITICAL: Prepend system library paths for ICU
        // .NET Runtime needs libicuuc from system libraries
        if (env.ContainsKey("LD_LIBRARY_PATH"))
        {
            env["LD_LIBRARY_PATH"] = $"/usr/lib64:/usr/lib:{env["LD_LIBRARY_PATH"]}";
        }
        else
        {
            env["LD_LIBRARY_PATH"] = "/usr/lib64:/usr/lib";
        }
        
        _logger.LogDebug("[DALAMUD-INJECT] DALAMUD_RUNTIME={Path} (Wine Z:\\ path)", wineRuntimePath);
        _logger.LogDebug("[DALAMUD-INJECT] DOTNET_ROOT={Path} (same as DALAMUD_RUNTIME)", wineRuntimePath);
        _logger.LogDebug("[DALAMUD-INJECT] COMPlus settings configured for Wine compatibility");
        _logger.LogDebug("[DALAMUD-INJECT] COREHOST_TRACE=1 (detailed .NET diagnostics)");
        _logger.LogDebug("[DALAMUD-INJECT] LD_LIBRARY_PATH={Path}", env["LD_LIBRARY_PATH"]);
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
        
        // Configuration directory
        var configDir = ConvertToWinePath(_pathService.ConfigPath);
        sb.Append($" --dalamud-configuration-path=\"{configDir}\"");
        
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
        string winePath,
        string arguments,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var injectorPath = _pathService.InjectorPath;
        
        // Enable Wine debugging for .NET Runtime loading
        environment["WINEDEBUG"] = "+module,+loaddll";
        
        var psi = new ProcessStartInfo
        {
            FileName = winePath,
            Arguments = $"\"{injectorPath}\" {arguments}",
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
        
        _logger.LogInformation("[DALAMUD-INJECT] Executing: {Wine} \"{Injector}\" {Args}", 
            winePath, injectorPath, arguments);
        
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
            await Task.WhenAll(stdoutTask, stderrTask);
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
                ExitCode = exitCode
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

/// <summary>
/// Injection options
/// </summary>
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
}

/// <summary>
/// Injection result
/// </summary>
public class DalamudInjectionResult
{
    public bool Success { get; set; }
    public int? ExitCode { get; set; }
    public string? ErrorMessage { get; set; }
}
