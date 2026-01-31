using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Game.Launcher.Encryption;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Game.Launcher;

/// <summary>
/// Game launch service
/// Focuses on game launching, environment variables configured by IEnvironmentService
/// </summary>
public class GameLaunchService
{
    private readonly ILogger<GameLaunchService> _logger;
    private readonly IEnvironmentService? _environmentService;
    private Process? _gameProcess;
    
    public GameLaunchService(
        ILogger<GameLaunchService> logger,
        IEnvironmentService? environmentService = null)
    {
        _logger = logger;
        _environmentService = environmentService;
    }
    
    /// <summary>
    /// Get current game process
    /// </summary>
    public Process? GameProcess => _gameProcess;
    
    /// <summary>
    /// Check if game is running
    /// </summary>
    public bool IsGameRunning => _gameProcess != null && !_gameProcess.HasExited;
    
    /// <summary>
    /// Fake Launch - Test launch game (without Session ID)
    /// For macOS, pass WineConfig. For Linux, pass ProtonConfig (cast to object).
    /// </summary>
    public async Task<GameLaunchResult> FakeLaunchAsync(
        string gamePath,
        object? platformConfig,
        string? dalamudRuntimePath = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[GAME] Starting Fake Launch (test mode)");
        
        // Fake launch doesn't need real Session ID, use fake test ID
        var fakeSessionId = "0";
        
        return await LaunchGameInternalAsync(
            gamePath,
            fakeSessionId,
            platformConfig,
            isFakeLaunch: true,
            dalamudRuntimePath,
            cancellationToken);
    }
    
    /// <summary>
    /// Launch game officially
    /// For macOS, pass WineConfig. For Linux, pass ProtonConfig (cast to object).
    /// </summary>
    public async Task<GameLaunchResult> LaunchGameAsync(
        string gamePath,
        string sessionId,
        object? platformConfig,
        string? dalamudRuntimePath = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[GAME] Starting game with session ID");
        
        if (string.IsNullOrEmpty(sessionId))
        {
            return new GameLaunchResult
            {
                Success = false,
                ErrorMessage = "Session ID is required for game launch"
            };
        }
        
        return await LaunchGameInternalAsync(
            gamePath,
            sessionId,
            platformConfig,
            isFakeLaunch: false,
            dalamudRuntimePath,
            cancellationToken);
    }
    
    private async Task<GameLaunchResult> LaunchGameInternalAsync(
        string gamePath,
        string sessionId,
        object? platformConfig,
        bool isFakeLaunch,
        string? dalamudRuntimePath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate game path
            var exePath = Path.Combine(gamePath, "game", "ffxiv_dx11.exe");
            if (!File.Exists(exePath))
            {
                _logger.LogError("[GAME] Game executable not found: {ExePath}", exePath);
                return new GameLaunchResult
                {
                    Success = false,
                    ErrorMessage = $"Game executable not found: {exePath}"
                };
            }
            
            // Read game version
            var gameVersion = GetGameVersion(gamePath);
            _logger.LogInformation("[GAME] Game version: {Version}", gameVersion);
            
            // Build launch arguments
            var argumentBuilder = new ArgumentBuilder()
                .Append("DEV.LobbyHost01", "neolobby01.ffxiv.com.tw")
                .Append("DEV.LobbyPort01", "54994")
                .Append("DEV.GMServerHost", "frontier.ffxiv.com.tw")
                .Append("DEV.TestSID", sessionId)
                .Append("SYS.resetConfig", "0")
                .Append("DEV.SaveDataBankHost", "config-dl.ffxiv.com.tw");
            
            // On macOS/Linux, set UserPath (game config directory)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || 
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var ffxivConfigPath = GetFfxivConfigPath();
                var wineUserPath = ConvertToWinePath(ffxivConfigPath);
                argumentBuilder.Append("UserPath", wineUserPath);
                _logger.LogInformation("[GAME] UserPath: {Path}", wineUserPath);
            }
            
            // Taiwan server uses unencrypted arguments
            var arguments = argumentBuilder.Build();
            _logger.LogDebug("[GAME] Launch arguments: {Args}", arguments);
            
            // Working directory
            var workingDir = Path.Combine(gamePath, "game");
            
            // Launch based on platform
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await LaunchWindowsAsync(exePath, workingDir, arguments, cancellationToken);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || 
                     RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Use IEnvironmentService to get environment and launch
                if (_environmentService == null)
                {
                    _logger.LogError("[GAME] IEnvironmentService not available");
                    return new GameLaunchResult
                    {
                        Success = false,
                        ErrorMessage = "Environment service not configured"
                    };
                }
                
                // Get environment variables
                var baseEnvironment = _environmentService.GetEnvironment();
                
                // Add Dalamud runtime if provided
                // CRITICAL: Must convert to Wine Z:\ path format
                // Dalamud.Boot.dll passes this to hostfxr which expects Windows paths in Wine
                if (!string.IsNullOrEmpty(dalamudRuntimePath))
                {
                    var wineDalamudPath = $"Z:{dalamudRuntimePath.Replace("/", "\\")}";
                    baseEnvironment["DALAMUD_RUNTIME"] = wineDalamudPath;
                    baseEnvironment["DOTNET_ROOT"] = wineDalamudPath;  // Also set DOTNET_ROOT
                    _logger.LogInformation("[GAME] Dalamud Runtime path (Wine): {Path}", wineDalamudPath);
                }
                
                return await LaunchWithEnvironmentServiceAsync(
                    exePath, 
                    workingDir, 
                    arguments, 
                    baseEnvironment, 
                    cancellationToken);
            }
            else
            {
                return new GameLaunchResult
                {
                    Success = false,
                    ErrorMessage = "Unsupported platform"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GAME] Failed to launch game");
            return new GameLaunchResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Get game config directory (ffxivConfig)
    /// </summary>
    private static string GetFfxivConfigPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appSupport;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            appSupport = Path.Combine(homeDir, "Library", "Application Support", "XIVTheCalamity");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            appSupport = Path.Combine(homeDir, ".config", "XIVTheCalamity");
        }
        else
        {
            // Windows
            appSupport = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVTheCalamity");
        }
        
        var ffxivConfigPath = Path.Combine(appSupport, "ffxivConfig");
        
        // Ensure directory exists
        Directory.CreateDirectory(ffxivConfigPath);
        
        return ffxivConfigPath;
    }
    
    /// <summary>
    /// Convert Unix path to Wine path
    /// </summary>
    private static string ConvertToWinePath(string unixPath)
    {
        if (string.IsNullOrEmpty(unixPath))
            return unixPath;
        return "Z:" + unixPath.Replace("/", "\\");
    }
    
    private async Task<GameLaunchResult> LaunchWindowsAsync(
        string exePath,
        string workingDir,
        string arguments,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GAME] Launching on Windows: {ExePath}", exePath);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        
        _gameProcess = Process.Start(startInfo);
        
        if (_gameProcess == null)
        {
            return new GameLaunchResult
            {
                Success = false,
                ErrorMessage = "Failed to start game process"
            };
        }
        
        _logger.LogInformation("[GAME] Game started with PID: {Pid}", _gameProcess.Id);
        
        await Task.CompletedTask;
        return new GameLaunchResult
        {
            Success = true,
            ProcessId = _gameProcess.Id,
            Process = _gameProcess
        };
    }
    
    private async Task<GameLaunchResult> LaunchWithEnvironmentServiceAsync(
        string exePath,
        string workingDir,
        string arguments,
        Dictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        // Get emulator directory and wine path from environment service
        if (_environmentService == null)
        {
            _logger.LogError("[GAME] Environment service not available");
            return new GameLaunchResult
            {
                Success = false,
                ErrorMessage = "Environment service not configured"
            };
        }
        
        var emulatorDir = _environmentService.GetEmulatorDirectory();
        
        // Get wine executable path based on platform
        string winePath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // macOS & Linux: use bin/wine64
            winePath = Path.Combine(emulatorDir, "bin", "wine64");
        }
        else
        {
            // Fallback (should not reach here for non-Windows)
            winePath = Path.Combine(emulatorDir, "files", "bin", "wine");
        }
        
        if (string.IsNullOrEmpty(winePath) || !File.Exists(winePath))
        {
            _logger.LogError("[GAME] Wine executable not found: {WinePath}", winePath);
            return new GameLaunchResult
            {
                Success = false,
                ErrorMessage = $"Wine executable not found: {winePath}"
            };
        }
        
        _logger.LogInformation("[GAME] Launching with Wine: {WinePath}", winePath);
        _logger.LogInformation("[GAME] Game executable: {ExePath}", exePath);
        
        // Log environment variables (only key ones)
        _logger.LogDebug("[GAME] Wine environment:");
        foreach (var (key, value) in environment)
        {
            if (key.Contains("WINE") || key.Contains("DXMT") || key.Contains("DXVK") || 
                key.Contains("MTL") || key.Contains("PROTON") || key.Contains("DALAMUD") ||
                key.Contains("LD_LIBRARY") || key.Contains("VKD3D"))
            {
                _logger.LogDebug("[GAME]   {Key}={Value}", key, value);
            }
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = winePath,
            Arguments = $"\"{exePath}\" {arguments}",
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        
        // CRITICAL: Clear all inherited environment variables first
        // This prevents system LD_LIBRARY_PATH or other vars from interfering
        startInfo.Environment.Clear();
        
        // Apply our Wine environment variables
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }
        
        // Add essential system variables back
        var essentialVars = new[] 
        { 
            "PATH", "HOME", 
            "DISPLAY", "WAYLAND_DISPLAY", "XDG_RUNTIME_DIR",  // Display server
            "XAUTHORITY", "XDG_SESSION_TYPE",                   // X11 auth
            "LANG", "LC_ALL",                                    // Locale
            "DALAMUD_RUNTIME"                                    // Dalamud .NET Runtime path (Unix path)
        };
        
        foreach (var varName in essentialVars)
        {
            if (!startInfo.Environment.ContainsKey(varName))
            {
                var value = Environment.GetEnvironmentVariable(varName);
                if (!string.IsNullOrEmpty(value))
                {
                    startInfo.Environment[varName] = value;
                }
            }
        }
        
        // Ensure PATH is set
        if (!startInfo.Environment.ContainsKey("PATH"))
        {
            startInfo.Environment["PATH"] = "/usr/bin:/bin";
        }
        
        // Ensure HOME is set
        if (!startInfo.Environment.ContainsKey("HOME"))
        {
            startInfo.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        
        _gameProcess = Process.Start(startInfo);
        
        if (_gameProcess == null)
        {
            return new GameLaunchResult
            {
                Success = false,
                ErrorMessage = "Failed to start game process"
            };
        }
        
        // Capture output for debugging
        _ = Task.Run(() =>
        {
            while (!_gameProcess.StandardOutput.EndOfStream)
            {
                var line = _gameProcess.StandardOutput.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                    _logger.LogDebug("[GAME-OUT] {Line}", line);
            }
        });
        
        _ = Task.Run(() =>
        {
            while (!_gameProcess.StandardError.EndOfStream)
            {
                var line = _gameProcess.StandardError.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                    _logger.LogError("[GAME-ERR] {Line}", line);
            }
        });
        
        _logger.LogInformation("[GAME] Game started with PID: {Pid}", _gameProcess.Id);
        
        await Task.CompletedTask;
        return new GameLaunchResult
        {
            Success = true,
            ProcessId = _gameProcess.Id,
            Process = _gameProcess
        };
    }
    
    private async Task WriteGameLogAsync(Process process)
    {
        try
        {
            var logDir = GetLogDirectory();
            // Consolidate logs for same day
            var dateStamp = DateTime.Now.ToString("yyyyMMdd");
            var logPath = Path.Combine(logDir, $"game-{dateStamp}.log");
            
            await using var logFile = new StreamWriter(logPath, append: true);
            await logFile.WriteLineAsync();
            await logFile.WriteLineAsync($"=== Game Launch ===");
            await logFile.WriteLineAsync($"=== Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            await logFile.WriteLineAsync($"=== PID: {process.Id} ===");
            await logFile.WriteLineAsync();
            
            var stdoutTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line != null)
                    {
                        await logFile.WriteLineAsync($"[OUT] {line}");
                        await logFile.FlushAsync();
                    }
                }
            });
            
            var stderrTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line != null)
                    {
                        await logFile.WriteLineAsync($"[ERR] {line}");
                        await logFile.FlushAsync();
                    }
                }
            });
            
            await Task.WhenAll(stdoutTask, stderrTask);
            
            await logFile.WriteLineAsync();
            await logFile.WriteLineAsync($"=== Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            await logFile.WriteLineAsync($"=== Exit Code: {process.ExitCode} ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GAME] Failed to write game log");
        }
    }
    
    /// <summary>
    /// Wait for game to exit and get exit code
    /// </summary>
    public async Task<int?> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (_gameProcess == null)
        {
            return null;
        }
        
        try
        {
            await _gameProcess.WaitForExitAsync(cancellationToken);
            var exitCode = _gameProcess.ExitCode;
            _logger.LogInformation("[GAME] Game exited with code: {ExitCode}", exitCode);
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[GAME] Wait for exit was cancelled");
            return null;
        }
    }
    
    private string GetGameVersion(string gamePath)
    {
        var verPath = Path.Combine(gamePath, "game", "ffxivgame.ver");
        if (File.Exists(verPath))
        {
            return File.ReadAllText(verPath).Trim();
        }
        return "2012.01.01.0000.0000"; // Default version
    }
    
    private string GetLogDirectory()
    {
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            appSupport = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            appSupport = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }
        
        var logDir = Path.Combine(appSupport, "XIVTheCalamity", "logs");
        Directory.CreateDirectory(logDir);
        
        return logDir;
    }
}

/// <summary>
/// Game launch result
/// </summary>
public class GameLaunchResult
{
    public bool Success { get; set; }
    public int? ProcessId { get; set; }
    public string? ErrorMessage { get; set; }
    public System.Diagnostics.Process? Process { get; set; }
}
