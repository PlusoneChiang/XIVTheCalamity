namespace XIVTheCalamity.Platform;

/// <summary>
/// Cross-platform environment service interface
/// Unifies macOS Wine and Linux Proton environment management
/// </summary>
public interface IEnvironmentService
{
    /// <summary>
    /// Initialize environment with progress reporting
    /// Includes downloading emulator (if needed) and creating prefix
    /// </summary>
    Task InitializeAsync(IProgress<EnvironmentInitProgress>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Ensure environment is initialized (Prefix created)
    /// Simple version without progress reporting
    /// </summary>
    Task EnsurePrefixAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get emulator root directory
    /// macOS: Wine directory
    /// Linux: Proton directory
    /// Windows: Empty string
    /// </summary>
    string GetEmulatorDirectory();
    
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
    /// Linux: Apply Proton configuration
    /// Windows: No-op
    /// </summary>
    Task ApplyConfigAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Start audio routing for game process (macOS only)
    /// </summary>
    /// <param name="gamePid">Game process ID</param>
    /// <param name="esyncEnabled">Esync enabled</param>
    /// <param name="msyncEnabled">Msync enabled</param>
    void StartAudioRouter(int gamePid, bool esyncEnabled, bool msyncEnabled);
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
/// </summary>
public class EnvironmentInitProgress
{
    public string Stage { get; set; } = string.Empty;
    public string MessageKey { get; set; } = string.Empty;
    public int Percent { get; set; }
    public bool IsComplete { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessageKey { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? ExtraData { get; set; }
}
