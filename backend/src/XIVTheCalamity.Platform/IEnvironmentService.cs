using System.Runtime.CompilerServices;
using XIVTheCalamity.Core.Models.Progress;

namespace XIVTheCalamity.Platform;

/// <summary>
/// Cross-platform environment service interface
/// Unifies macOS Wine and Linux Wine-XIV environment management
/// </summary>
public interface IEnvironmentService
{
    /// <summary>
    /// Initialize environment with progress reporting using IAsyncEnumerable
    /// Includes downloading emulator (if needed) and creating prefix
    /// </summary>
    IAsyncEnumerable<EnvironmentProgressEvent> InitializeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Ensure environment is initialized (Prefix created)
    /// Simple version without progress reporting
    /// </summary>
    Task EnsurePrefixAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get emulator root directory
    /// macOS: Wine directory
    /// Linux: Runtime directory (Wine-XIV/Proton)
    /// Windows: Empty string
    /// </summary>
    string GetEmulatorDirectory();

    /// <summary>
    /// Get Wine-compatible executable path used to launch Windows programs.
    /// macOS/Linux: wine64 path from current runtime
    /// Windows: Empty string
    /// </summary>
    string GetWineExecutablePath();

    /// <summary>
    /// Get the launcher command used to execute Windows programs.
    /// For plain wine: returns (wine64, [])
    /// For umu: returns ("python3", ["/path/to/umu-run"])
    /// LaunchOptions (e.g. fgmod wrapper) are applied when set.
    /// The full invocation is: Executable PrefixArgs... windowsExe windowsArgs... SuffixArgs
    /// Use this only for actual game launch.
    /// </summary>
    WineLauncher GetLauncherCommand() => new WineLauncher(GetWineExecutablePath(), []);

    /// <summary>
    /// Get the base launcher command WITHOUT LaunchOptions applied.
    /// Used for utility Wine operations (winedbg, Dalamud.Injector) that must not
    /// be wrapped by user-defined launch wrappers such as fgmod.
    /// </summary>
    WineLauncher GetBaseLauncherCommand() => new WineLauncher(GetWineExecutablePath(), []);
    
    /// <summary>
    /// Get environment variables configuration
    /// </summary>
    Dictionary<string, string> GetEnvironment();
    
    /// <summary>
    /// Execute environment command
    /// </summary>
    Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if environment is available
    /// </summary>
    Task<bool> IsAvailableAsync();
    
    /// <summary>
    /// Get environment information (for debugging)
    /// </summary>
    string GetDebugInfo();
    
    /// <summary>
    /// Apply platform-specific configuration
    /// macOS: Apply Wine registry settings
    /// Linux: Apply Wine-XIV configuration
    /// Windows: No-op
    /// </summary>
    Task ApplyConfigAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Start audio routing for game process (macOS only)
    /// </summary>
    /// <param name="gamePid">Game process ID</param>
    /// <param name="msyncEnabled">Msync enabled</param>
    void StartAudioRouter(int gamePid, bool msyncEnabled);
}

/// <summary>
/// Represents the launcher command used to execute Windows executables.
/// For plain Wine: Executable="wine64", PrefixArgs=[]
/// For umu-launcher: Executable="python3", PrefixArgs=["/path/to/umu-run"]
/// Full invocation: Executable PrefixArgs... windowsExe windowsArgs... SuffixArgs
/// </summary>
public record WineLauncher(string Executable, IReadOnlyList<string> PrefixArgs, IReadOnlyList<string>? SuffixArgs = null)
{
    public bool IsValid => !string.IsNullOrEmpty(Executable) && File.Exists(Executable);

    /// <summary>
    /// Builds an Arguments string that wraps windowsArgs with PrefixArgs and SuffixArgs.
    /// Example (umu):    BuildArguments("\"/path/game.exe\" /arg") → "/path/umu-run \"/path/game.exe\" /arg"
    /// Example (suffix): BuildArguments("\"/path/game.exe\"")       → "umu-run \"/path/game.exe\" --extra"
    /// </summary>
    public string BuildArguments(string windowsArgs)
    {
        var parts = new List<string>();

        if (PrefixArgs.Count > 0)
            parts.Add(string.Join(" ", PrefixArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

        if (!string.IsNullOrEmpty(windowsArgs))
            parts.Add(windowsArgs);

        if (SuffixArgs is { Count: > 0 })
            parts.Add(string.Join(" ", SuffixArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

        return string.Join(" ", parts);
    }
}

/// <summary>
/// Process execution result
/// </summary>
public record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError
);

/// <summary>
/// Environment initialization progress (unified across all platforms)
/// Follows DalamudUpdateProgress pattern for consistency
/// </summary>
public class EnvironmentInitProgress
{
    public string Stage { get; set; } = string.Empty;
    public string MessageKey { get; set; } = string.Empty;
    public string? CurrentFile { get; set; }
    
    /// <summary>Downloaded bytes (for download progress)</summary>
    public long BytesDownloaded { get; set; }
    
    /// <summary>Total bytes (for download progress)</summary>
    public long TotalBytes { get; set; }
    
    /// <summary>Completed items (for multi-item progress)</summary>
    public int CompletedItems { get; set; }
    
    /// <summary>Total items (for multi-item progress)</summary>
    public int TotalItems { get; set; }
    
    /// <summary>
    /// Completion percentage (0-100), auto-calculated
    /// Priority: BytesDownloaded/TotalBytes > CompletedItems/TotalItems
    /// </summary>
    public double Percentage => TotalBytes > 0
        ? Math.Round(BytesDownloaded * 100.0 / TotalBytes, 1)
        : (TotalItems > 0 ? Math.Round(CompletedItems * 100.0 / TotalItems, 1) : 0);
    
    public bool IsComplete { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessageKey { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? ExtraData { get; set; }
}
