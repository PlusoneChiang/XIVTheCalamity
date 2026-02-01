namespace XIVTheCalamity.Core.Models.Progress;

/// <summary>
/// Progress event for game patch download and installation
/// Supports concurrent multi-threaded downloads
/// Note: Uses UpdatePhase from XIVTheCalamity.Game.Models (but should be in Core for clean architecture)
/// </summary>
public class PatchProgressEvent : ProgressEventBase
{
    /// <summary>
    /// Current update phase (downloading or installing)
    /// </summary>
    public string Phase { get; set; } = "downloading";
    
    /// <summary>
    /// Total number of patches
    /// </summary>
    public int TotalPatches { get; set; }
    
    /// <summary>
    /// Number of completed patches (fully installed)
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
    /// Current file name (for display)
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
    /// Total bytes downloaded (cumulative across all threads)
    /// </summary>
    public long TotalBytesDownloaded { get; set; }
    
    /// <summary>
    /// Download speed in bytes per second
    /// </summary>
    public double DownloadSpeedBytesPerSec { get; set; }
    
    /// <summary>
    /// Download speed in MB/s (calculated property)
    /// </summary>
    public double DownloadSpeedMBps => DownloadSpeedBytesPerSec / (1024.0 * 1024.0);
    
    /// <summary>
    /// Total downloaded in MB (calculated property)
    /// </summary>
    public double TotalDownloadedMB => TotalBytesDownloaded / (1024.0 * 1024.0);
    
    /// <summary>
    /// Total size in MB (calculated property)
    /// </summary>
    public double TotalMB => TotalBytes / (1024.0 * 1024.0);
    
    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (DownloadSpeedBytesPerSec <= 0 || TotalBytes <= 0)
                return null;
                
            var remainingBytes = TotalBytes - TotalBytesDownloaded;
            var secondsRemaining = remainingBytes / DownloadSpeedBytesPerSec;
            return TimeSpan.FromSeconds(secondsRemaining);
        }
    }
    
    /// <summary>
    /// Install progress: patches installed
    /// </summary>
    public int InstalledPatches { get; set; }
    
    /// <summary>
    /// Install progress: current install file name
    /// </summary>
    public string? InstallingFileName { get; set; }
    
    /// <summary>
    /// Overall progress percentage (based on bytes downloaded)
    /// </summary>
    public new double Percentage
    {
        get
        {
            if (base.Percentage > 0)
                return base.Percentage;
                
            if (TotalBytes > 0)
                return Math.Round(TotalBytesDownloaded * 100.0 / TotalBytes, 1);
                
            return 0;
        }
        set => base.Percentage = value;
    }
    
    /// <summary>
    /// Current file progress percentage
    /// </summary>
    public double CurrentFilePercentage => 
        CurrentFileSize > 0 ? Math.Round(CurrentFileDownloaded * 100.0 / CurrentFileSize, 1) : 0;
}
