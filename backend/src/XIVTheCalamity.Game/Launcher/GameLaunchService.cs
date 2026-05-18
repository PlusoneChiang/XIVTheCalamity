using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Services;
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
    /// Check if game is running — tracks both the managed process and any wine process in /proc.
    /// </summary>
    public bool IsGameRunning =>
        _gameProcess != null && !_gameProcess.HasExited;
    
    /// <summary>
    /// Fake Launch - Test launch game (without Session ID)
    /// For macOS, pass WineConfig. For Linux, pass ProtonGeConfig (cast to object).
    /// </summary>
    public async Task<GameLaunchResult> FakeLaunchAsync(
        string gamePath,
        object? platformConfig,
        string? dalamudRuntimePath = null,
        string? pluginRepoUrl = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[GAME] Starting Fake Launch (test mode)");
        
        var fakeSessionId = "0";
        
        return await LaunchGameInternalAsync(
            gamePath,
            fakeSessionId,
            platformConfig,
            isFakeLaunch: true,
            dalamudRuntimePath,
            pluginRepoUrl,
            cancellationToken);
    }
    
    /// <summary>
    /// Launch game officially
    /// For macOS, pass WineConfig. For Linux, pass ProtonGeConfig (cast to object).
    /// </summary>
    public async Task<GameLaunchResult> LaunchGameAsync(
        string gamePath,
        string sessionId,
        object? platformConfig,
        string? dalamudRuntimePath = null,
        string? pluginRepoUrl = null,
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
            pluginRepoUrl,
            cancellationToken);
    }
    
    private async Task<GameLaunchResult> LaunchGameInternalAsync(
        string gamePath,
        string sessionId,
        object? platformConfig,
        bool isFakeLaunch,
        string? dalamudRuntimePath,
        string? pluginRepoUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            gamePath = HomePathService.MapToEffectiveHomePath(gamePath);

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
            
            // Set UserPath (game config directory) to keep configs under our app data
            var ffxivConfigPath = GetFfxivConfigPath();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || 
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var wineUserPath = ConvertToWinePath(ffxivConfigPath);
                argumentBuilder.Append("UserPath", wineUserPath);
                _logger.LogInformation("[GAME] UserPath: {Path}", wineUserPath);
            }
            else
            {
                argumentBuilder.Append("UserPath", ffxivConfigPath);
                _logger.LogInformation("[GAME] UserPath: {Path}", ffxivConfigPath);
            }
            
            // Taiwan server uses unencrypted arguments
            var arguments = argumentBuilder.Build();
            _logger.LogDebug("[GAME] Launch arguments: {Args}", arguments);
            
            // Working directory
            var workingDir = Path.Combine(gamePath, "game");
            
            // Launch based on platform
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await LaunchWindowsAsync(exePath, workingDir, arguments, dalamudRuntimePath, pluginRepoUrl, cancellationToken);
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

                var consistencyError = ValidateMacPathConsistency(gamePath, ffxivConfigPath, baseEnvironment);
                if (!string.IsNullOrEmpty(consistencyError))
                {
                    _logger.LogError("[GAME] {Error}", consistencyError);
                    return new GameLaunchResult
                    {
                        Success = false,
                        ErrorMessage = consistencyError
                    };
                }
                
                // Add Dalamud runtime if provided
                // CRITICAL: Must convert to Wine Z:\ path format
                // Dalamud.Boot.dll passes this to hostfxr which expects Windows paths in Wine
                if (!string.IsNullOrEmpty(dalamudRuntimePath))
                {
                    var wineDalamudPath = $"Z:{dalamudRuntimePath.Replace("/", "\\")}";
                    baseEnvironment["DALAMUD_RUNTIME"] = wineDalamudPath;
                    baseEnvironment["DOTNET_ROOT"] = wineDalamudPath;  // Also set DOTNET_ROOT
                    
                    // Keep launch-time runtime settings aligned with injector environment.
                    // Dalamud.Boot executes inside the game process and needs these variables.
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        baseEnvironment["DALAMUD_FORCE_MINHOOK"] = "true";
                        baseEnvironment["DOTNET_EnableWriteXorExecute"] = "0";
                        baseEnvironment["COMPlus_EnableAlternateStackCheck"] = "0";
                        baseEnvironment["COMPlus_gcAllowVeryLargeObjects"] = "1";
                        // NOTE: Do NOT set DOTNET_SYSTEM_GLOBALIZATION_INVARIANT — Dalamud
                        // requires zh-hant culture (CultureInfo.GetCultureInfo("zh-hant")),
                        // which fails in invariant mode.
                        // NOTE: Do NOT prepend /usr/lib64:/usr/lib — system ICU 75 uses versioned
                        // symbols (u_charsToUChars_75) that Wine cannot resolve. Proton-GE's own
                        // lib paths from GetEnvironment() are sufficient.
                        
                        _logger.LogInformation("[GAME] Applied Linux Dalamud runtime environment overrides");
                    }
                    
                    // CRITICAL: Enable .NET 7+ on Apple Silicon (for Dalamud only)
                    // This MUST be set only when Dalamud is enabled, not globally
                    // Setting it globally causes Wine processes to crash with exit code 136
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        baseEnvironment["DOTNET_EnableWriteXorExecute"] = "0";
                        _logger.LogDebug("[GAME] Set DOTNET_EnableWriteXorExecute=0 for Dalamud on Apple Silicon");
                    }
                    
                    _logger.LogInformation("[GAME] Dalamud Runtime path (Wine): {Path}", wineDalamudPath);
                }

                // Set plugin repo URL so Dalamud reads it from game process env (works for both inject and entrypoint modes)
                if (!string.IsNullOrWhiteSpace(pluginRepoUrl))
                {
                    baseEnvironment["DALAMUD_MAIN_REPO_URL"] = pluginRepoUrl;
                    _logger.LogInformation("[GAME] DALAMUD_MAIN_REPO_URL set: {Url}", pluginRepoUrl);
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
        var homeDir = HomePathService.GetEffectiveHomePath();
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

    private static string? ValidateMacPathConsistency(
        string gamePath,
        string ffxivConfigPath,
        IReadOnlyDictionary<string, string> environment)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return null;
        }

        var homeAlias = Environment.GetEnvironmentVariable("XIV_HOME_ALIAS");
        var realHome = Environment.GetEnvironmentVariable("XIV_REAL_HOME");
        if (string.IsNullOrWhiteSpace(homeAlias) || string.IsNullOrWhiteSpace(realHome))
        {
            return null;
        }

        var pathsToCheck = new List<string>
        {
            gamePath,
            ffxivConfigPath
        };

        if (environment.TryGetValue("WINEPREFIX", out var winePrefix) && !string.IsNullOrWhiteSpace(winePrefix))
        {
            pathsToCheck.Add(winePrefix);
        }

        if (environment.TryGetValue("HOME", out var homePath) && !string.IsNullOrWhiteSpace(homePath))
        {
            pathsToCheck.Add(homePath);
        }

        var hasAliasPath = pathsToCheck.Any(p => IsPathUnderRoot(p, homeAlias));
        var hasRealPath = pathsToCheck.Any(p => IsPathUnderRoot(p, realHome));

        if (hasAliasPath && hasRealPath)
        {
            return "Detected mixed Home path roots (alias and real) in launch parameters. Please reopen launcher or disable Home alias compatibility mode.";
        }

        return null;
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal) ||
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
    
    private async Task<GameLaunchResult> LaunchWindowsAsync(
        string exePath,
        string workingDir,
        string arguments,
        string? dalamudRuntimePath,
        string? pluginRepoUrl,
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
        
        // Set DALAMUD_RUNTIME so Dalamud.Boot can find .NET runtime inside the game process
        if (!string.IsNullOrEmpty(dalamudRuntimePath))
        {
            startInfo.Environment["DALAMUD_RUNTIME"] = dalamudRuntimePath;
            startInfo.Environment["DOTNET_ROOT"] = dalamudRuntimePath;
            _logger.LogInformation("[GAME] Dalamud Runtime path: {Path}", dalamudRuntimePath);
        }

        // Set plugin repo URL so Dalamud reads it from game process env (works for both inject and entrypoint modes)
        if (!string.IsNullOrWhiteSpace(pluginRepoUrl))
        {
            startInfo.Environment["DALAMUD_MAIN_REPO_URL"] = pluginRepoUrl;
            _logger.LogInformation("[GAME] DALAMUD_MAIN_REPO_URL set: {Url}", pluginRepoUrl);
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
        
        var launcher = _environmentService.GetLauncherCommand();

        if (!launcher.IsValid)
        {
            _logger.LogError("[GAME] Wine/launcher executable not found: {Exe}", launcher.Executable);
            return new GameLaunchResult
            {
                Success = false,
                ErrorMessage = $"Wine executable not found: {launcher.Executable}"
            };
        }

        _logger.LogInformation("[GAME] Launching with: {Exe} (prefix args: {Prefix})", launcher.Executable, string.Join(" ", launcher.PrefixArgs));
        _logger.LogInformation("[GAME] Game executable: {ExePath}", exePath);
        
        // Log environment variables (only key ones)
        _logger.LogDebug("[GAME] Wine environment:");
        foreach (var (key, value) in environment)
        {
            if (key.Contains("WINE") || key.Contains("DXMT") || key.Contains("DXVK") || 
                key.Contains("MTL") || key.Contains("DALAMUD") ||
                key.Contains("LD_LIBRARY") || key.Contains("VKD3D"))
            {
                _logger.LogDebug("[GAME]   {Key}={Value}", key, value);
            }
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = launcher.Executable,
            Arguments = launcher.BuildArguments($"\"{exePath}\" {arguments}"),
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        
        // Keep inherited environment variables and override with launcher-managed values.
        // This matches behavior where system GPU/driver-related variables remain available.
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        SanitizeLinuxLaunchEnvironment(startInfo);
        
        // Ensure PATH is set
        if (!startInfo.Environment.ContainsKey("PATH"))
        {
            startInfo.Environment["PATH"] = "/usr/bin:/bin";
        }
        
        // Ensure HOME is set
        if (!startInfo.Environment.ContainsKey("HOME"))
        {
            startInfo.Environment["HOME"] = HomePathService.GetEffectiveHomePath();
        }

        ApplyLinuxXModifiersEnvironment(startInfo);
        
        _gameProcess = Process.Start(startInfo);
        
        if (_gameProcess == null)
        {
            return new GameLaunchResult
            {
                Success = false,
                ErrorMessage = "Failed to start game process"
            };
        }
        
        // Capture output for debugging - write to both log file and logger
        _ = WriteGameLogAsync(_gameProcess);
        
        _logger.LogInformation("[GAME] Game started with PID: {Pid}", _gameProcess.Id);
        
        await Task.CompletedTask;
        return new GameLaunchResult
        {
            Success = true,
            ProcessId = _gameProcess.Id,
            Process = _gameProcess,
            LaunchEnvironment = new Dictionary<string, string>(environment)
        };
    }

    private void SanitizeLinuxLaunchEnvironment(ProcessStartInfo startInfo)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        if (startInfo.Environment.TryGetValue("LD_PRELOAD", out var ldPreload) &&
            !string.IsNullOrWhiteSpace(ldPreload))
        {
            _logger.LogWarning("[GAME] Removing inherited LD_PRELOAD for Wine launch: {LdPreload}", ldPreload);
            startInfo.Environment.Remove("LD_PRELOAD");
        }

        var conflictingVars = new[]
        {
            "SDL_VIDEODRIVER", "QT_QPA_PLATFORM",
            "APPDIR", "APPIMAGE", "ARGV0", "GSETTINGS_SCHEMA_DIR", "OWD"
        };

        foreach (var varName in conflictingVars)
        {
            if (startInfo.Environment.Remove(varName))
            {
                _logger.LogDebug("[GAME] Removed conflicting env var: {VarName}", varName);
            }
        }

        var steamAndOverlayVars = new[]
        {
            "SteamAppId", "SteamGameId",
            "VK_LAYER_PATH", "VK_ADD_LAYER_PATH", "VK_INSTANCE_LAYERS",
            "DISABLE_VK_LAYER_VALVE_steam_overlay_1",
            "ENABLE_VKBASALT",
            "MANGOHUD", "MANGOHUD_DLSYM", "MANGOHUD_CONFIG", "MANGOHUD_CONFIGFILE"
        };
        foreach (var varName in steamAndOverlayVars)
        {
            if (startInfo.Environment.Remove(varName))
            {
                _logger.LogDebug("[GAME] Removed Steam/overlay env var: {VarName}", varName);
            }
        }

        RemoveEnvironmentByPrefix(startInfo, "STEAM_");
        RemoveEnvironmentByPrefix(startInfo, "PRESSURE_VESSEL_");
        RemoveEnvironmentByPrefix(startInfo, "MANGOHUD");

        SanitizeColonSeparatedEnvironment(startInfo, "PATH");
        SanitizeColonSeparatedEnvironment(startInfo, "XDG_DATA_DIRS");
    }

    private void RemoveEnvironmentByPrefix(ProcessStartInfo startInfo, string prefix)
    {
        var keysToRemove = new List<string>();
        foreach (var key in startInfo.Environment.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            startInfo.Environment.Remove(key);
            _logger.LogDebug("[GAME] Removed env var by prefix {Prefix}: {Key}", prefix, key);
        }
    }

    private void SanitizeColonSeparatedEnvironment(ProcessStartInfo startInfo, string variableName)
    {
        if (!startInfo.Environment.TryGetValue(variableName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var segments = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var sanitizedSegments = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            if (segment.Contains(".mount_") || segment.Contains("/tmp/.mount"))
            {
                continue;
            }

            sanitizedSegments.Add(segment);
        }

        if (sanitizedSegments.Count != segments.Length)
        {
            startInfo.Environment[variableName] = string.Join(":", sanitizedSegments);
            _logger.LogDebug("[GAME] Sanitized {VariableName} by removing AppImage mount paths", variableName);
        }
    }

    private void ApplyLinuxXModifiersEnvironment(ProcessStartInfo startInfo)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var hasFcitx = Process.GetProcessesByName("fcitx5").Length > 0 ||
                       Process.GetProcessesByName("fcitx5-bin").Length > 0 ||
                       Process.GetProcessesByName("fcitx").Length > 0;
        var hasIbus = Process.GetProcessesByName("ibus-daemon").Length > 0;

        string? framework = null;
        string? source = null;
        if (hasFcitx)
        {
            framework = "fcitx";
            source = hasIbus ? "process:fcitx+ibus-daemon" : "process:fcitx";
        }
        else if (hasIbus)
        {
            framework = "ibus";
            source = "process:ibus-daemon";
        }

        if (framework == null)
        {
            _logger.LogDebug("[GAME] Unable to detect IME framework, keeping existing XMODIFIERS");
            return;
        }

        var xModifiers = $"@im={framework}";
        startInfo.Environment["XMODIFIERS"] = xModifiers;
        _logger.LogInformation(
            "[GAME] Auto-detected IME framework: {Framework} (source: {Source}), set XMODIFIERS={XModifiers}",
            framework, source, xModifiers);
    }
    
    private async Task WriteGameLogAsync(Process process)
    {
        try
        {
            var logDir = GetLogDirectory();
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
                        _logger.LogDebug("[GAME-OUT] {Line}", line);
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
                        _logger.LogWarning("[GAME-ERR] {Line}", line);
                    }
                }
            });
            
            await Task.WhenAll(stdoutTask, stderrTask);
            
            await logFile.WriteLineAsync();
            await logFile.WriteLineAsync($"=== Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            try
            {
                await logFile.WriteLineAsync($"=== Exit Code: {process.ExitCode} ===");
            }
            catch (InvalidOperationException)
            {
                await logFile.WriteLineAsync("=== Exit Code: unknown (process still running) ===");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GAME] Failed to write game log");
        }
    }
    
    /// <summary>
    /// Build game executable path and launch arguments without starting the game.
    /// Used by EntryPoint injection mode so Dalamud.Injector can start the game.
    /// </summary>
    public (string ExePath, string Arguments) BuildGameLaunchArgs(string gamePath, string sessionId)
    {
        gamePath = HomePathService.MapToEffectiveHomePath(gamePath);
        var exePath = Path.Combine(gamePath, "game", "ffxiv_dx11.exe");

        var argumentBuilder = new ArgumentBuilder()
            .Append("DEV.LobbyHost01", "neolobby01.ffxiv.com.tw")
            .Append("DEV.LobbyPort01", "54994")
            .Append("DEV.GMServerHost", "frontier.ffxiv.com.tw")
            .Append("DEV.TestSID", sessionId)
            .Append("SYS.resetConfig", "0")
            .Append("DEV.SaveDataBankHost", "config-dl.ffxiv.com.tw");

        var ffxivConfigPath = GetFfxivConfigPath();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            argumentBuilder.Append("UserPath", ConvertToWinePath(ffxivConfigPath));
        }
        else
        {
            argumentBuilder.Append("UserPath", ffxivConfigPath);
        }

        return (exePath, argumentBuilder.Build());
    }

    /// <summary>
    /// Register an externally-started game process (e.g. launched by Dalamud EntryPoint mode).
    /// Enables IsGameRunning / WaitForExitAsync tracking.
    /// </summary>
    public void RegisterExternalGameProcess(int pid)
    {
        try
        {
            _gameProcess = Process.GetProcessById(pid);
            _logger.LogInformation("[GAME] Registered external game process PID: {Pid}", pid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GAME] Failed to register external game process PID: {Pid}", pid);
        }
    }

    /// <summary>
    /// Wait for game to exit and get exit code.
    /// </summary>
    public async Task<int?> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (_gameProcess == null)
        {
            _logger.LogWarning("[GAME] No game process to wait for");
            return null;
        }

        try
        {
            await _gameProcess.WaitForExitAsync(cancellationToken);
            try
            {
                var exitCode = _gameProcess.ExitCode;
                _logger.LogInformation("[GAME] Game exited with code: {ExitCode}", exitCode);
                return exitCode;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "[GAME] Game exited but exit code is unavailable for attached process PID: {Pid}", _gameProcess.Id);
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[GAME] Wait for exit was cancelled");
            return null;
        }
    }

    /// <summary>
    /// Set an externally-created monitor process (e.g. wineserver -w).
    /// IsGameRunning and WaitForExitAsync will track this process.
    /// </summary>
    public void SetMonitorProcess(Process process)
    {
        _gameProcess = process;
        _logger.LogInformation("[GAME] Set monitor process PID: {Pid}", process.Id);
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
                HomePathService.GetEffectiveHomePath(),
                "Library", "Application Support");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            appSupport = Path.Combine(
                HomePathService.GetEffectiveHomePath(),
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
    public Dictionary<string, string>? LaunchEnvironment { get; set; }
}
