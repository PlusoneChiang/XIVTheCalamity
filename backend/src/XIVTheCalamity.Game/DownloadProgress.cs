namespace XIVTheCalamity.Game.Models;

/// <summary>
/// Update phase (download or install)
/// </summary>
public enum UpdatePhase
{
    Downloading,
    Installing
}

/// <summary>
/// Download/install progress information
/// </summary>
public class DownloadProgress
{
    /// <summary>
    /// Current update phase
    /// </summary>
    public UpdatePhase Phase { get; set; } = UpdatePhase.Downloading;

    /// <summary>
    /// Total patch count
    /// </summary>
    public int TotalPatches { get; set; }

    /// <summary>
    /// Completed patch count (fully installed)
    /// </summary>
    public int CompletedPatches { get; set; }

    /// <summary>
    /// Number of patches currently being downloaded
    /// </summary>
    public int DownloadingCount { get; set; }

    /// <summary>
    /// Number of patches currently being installed (0 or 1)
    /// </summary>
    public int InstallingCount { get; set; }

    /// <summary>
    /// Current file name (for single-thread mode)
    /// </summary>
    public string? CurrentFileName { get; set; }

    /// <summary>
    /// Current file size
    /// </summary>
    public long CurrentFileSize { get; set; }

    /// <summary>
    /// Current file bytes downloaded
    /// </summary>
    public long CurrentFileDownloaded { get; set; }

    /// <summary>
    /// Total download size (all patches)
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Total bytes downloaded (cumulative)
    /// </summary>
    public long TotalBytesDownloaded { get; set; }

    /// <summary>
    /// Download speed (bytes/second)
    /// </summary>
    public double DownloadSpeedBytesPerSecond { get; set; }

    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Install progress: patches installed
    /// </summary>
    public int InstalledPatches { get; set; }

    /// <summary>
    /// Install progress: current install file name
    /// </summary>
    public string? InstallingFileName { get; set; }

    /// <summary>
    /// Overall progress percentage (based on installed patches)
    /// </summary>
    public double OverallPercentage => 
        TotalPatches > 0 ? (InstalledPatches * 100.0 / TotalPatches) : 0;

    /// <summary>
    /// Current file progress percentage
    /// </summary>
    public double CurrentFilePercentage => 
        CurrentFileSize > 0 ? (CurrentFileDownloaded * 100.0 / CurrentFileSize) : 0;

    /// <summary>
    /// Whether all downloads and installs are completed
    /// </summary>
    public bool IsCompleted => InstalledPatches >= TotalPatches && TotalPatches > 0;
    
    // Legacy property for backward compatibility
    [Obsolete("Use DownloadingCount instead")]
    public int DownloadingPatches { get => DownloadingCount; set => DownloadingCount = value; }
}

/// <summary>
/// Update check result
/// </summary>
public class UpdateCheckResult
{
    /// <summary>
    /// Whether update is needed
    /// </summary>
    public bool NeedsUpdate { get; set; }

    /// <summary>
    /// Required patches list
    /// </summary>
    public List<PatchInfo> RequiredPatches { get; set; } = new();

    /// <summary>
    /// Total download size (bytes)
    /// </summary>
    public long TotalDownloadSize { get; set; }

    /// <summary>
    /// Current version info
    /// </summary>
    public GameVersionInfo? CurrentVersions { get; set; }

    /// <summary>
    /// Error message (if any)
    /// </summary>
    public string? ErrorMessage { get; set; }
}
