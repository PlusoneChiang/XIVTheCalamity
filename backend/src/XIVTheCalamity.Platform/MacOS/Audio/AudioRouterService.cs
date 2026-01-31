using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace XIVTheCalamity.Platform.MacOS.Audio;

/// <summary>
/// macOS audio routing service
/// Manages XTCAudioRouter CLI tool startup and management
/// </summary>
public class AudioRouterService
{
    private readonly ILogger<AudioRouterService> _logger;
    private Process? _routerProcess;
    
    public AudioRouterService(ILogger<AudioRouterService> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Start audio router
    /// </summary>
    /// <param name="gamePid">Game process PID</param>
    /// <param name="winePrefix">Wine prefix path</param>
    /// <param name="winePath">Wine executable path</param>
    /// <param name="esync">Enable Esync</param>
    /// <param name="msync">Enable Msync</param>
    /// <returns>Whether startup succeeded</returns>
    public bool StartRouter(int gamePid, string winePrefix, string winePath, bool esync = true, bool msync = true)
    {
        try
        {
            var routerPath = GetRouterPath();
            if (string.IsNullOrEmpty(routerPath) || !File.Exists(routerPath))
            {
                _logger.LogWarning("[AUDIO-ROUTER] XTCAudioRouter not found at: {Path}", routerPath);
                return false;
            }
            
            _logger.LogInformation("[AUDIO-ROUTER] Starting XTCAudioRouter...");
            _logger.LogDebug("[AUDIO-ROUTER] Path: {Path}", routerPath);
            _logger.LogDebug("[AUDIO-ROUTER] PID: {Pid}, Prefix: {Prefix}, Wine: {Wine}, Esync: {Esync}, Msync: {Msync}", 
                gamePid, winePrefix, winePath, esync, msync);
            
            // Build arguments with esync/msync flags
            var args = $"--pid {gamePid} --wineprefix \"{winePrefix}\" --wine \"{winePath}\"";
            if (esync) args += " --esync";
            if (msync) args += " --msync";
            
            var startInfo = new ProcessStartInfo
            {
                FileName = routerPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            // Ensure environment variables are passed to child process
            // This is necessary as CLI needs to inherit WINEPREFIX and other variables
            startInfo.Environment["WINEPREFIX"] = winePrefix;
            startInfo.Environment["WINEDEBUG"] = "-all";
            
            _routerProcess = Process.Start(startInfo);
            
            if (_routerProcess != null)
            {
                _logger.LogInformation("[AUDIO-ROUTER] Started with PID: {Pid}", _routerProcess.Id);
                
                // Read output asynchronously (for debugging)
                _ = LogRouterOutputAsync(_routerProcess);
                
                return true;
            }
            else
            {
                _logger.LogError("[AUDIO-ROUTER] Failed to start process");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUDIO-ROUTER] Exception starting router");
            return false;
        }
    }
    
    /// <summary>
    /// Stop audio router
    /// </summary>
    public void StopRouter()
    {
        if (_routerProcess != null && !_routerProcess.HasExited)
        {
            try
            {
                _logger.LogInformation("[AUDIO-ROUTER] Stopping router...");
                _routerProcess.Kill();
                _routerProcess.WaitForExit(5000);
                _logger.LogInformation("[AUDIO-ROUTER] Router stopped");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AUDIO-ROUTER] Error stopping router");
            }
        }
        _routerProcess = null;
    }
    
    /// <summary>
    /// Get XTCAudioRouter executable path
    /// </summary>
    private string? GetRouterPath()
    {
        var appDir = AppContext.BaseDirectory;
        
        // Strategy 1: Bundle environment
        // XIVTheCalamity.app/Contents/MacOS/backend/ -> XIVTheCalamity.app/Contents/Resources/resources/bin/
        var bundlePath = Path.Combine(appDir, "..", "..", "Resources", "resources", "bin", "XTCAudioRouter");
        bundlePath = Path.GetFullPath(bundlePath);
        if (File.Exists(bundlePath))
        {
            return bundlePath;
        }
        
        // Strategy 2: Dev environment - search upward for shared/resources/bin
        var currentDir = new DirectoryInfo(appDir);
        while (currentDir != null)
        {
            var devPath = Path.Combine(currentDir.FullName, "shared", "resources", "bin", "XTCAudioRouter");
            if (File.Exists(devPath))
            {
                return devPath;
            }
            currentDir = currentDir.Parent;
        }
        
        return null;
    }
    
    /// <summary>
    /// Log router output asynchronously
    /// </summary>
    private async Task LogRouterOutputAsync(Process process)
    {
        try
        {
            var stdoutTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        _logger.LogInformation("[AUDIO-ROUTER-OUT] {Line}", line);
                    }
                }
            });
            
            var stderrTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        _logger.LogWarning("[AUDIO-ROUTER-ERR] {Line}", line);
                    }
                }
            });
            
            await Task.WhenAll(stdoutTask, stderrTask);
            
            await process.WaitForExitAsync();
            _logger.LogInformation("[AUDIO-ROUTER] Router exited with code: {ExitCode}", process.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AUDIO-ROUTER] Error reading output");
        }
    }
}
