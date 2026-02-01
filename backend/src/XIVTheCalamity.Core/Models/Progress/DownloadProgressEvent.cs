namespace XIVTheCalamity.Core.Models.Progress;

/// <summary>
/// Progress event for download operations
/// </summary>
public class DownloadProgressEvent : ProgressEventBase
{
    /// <summary>
    /// Current file being downloaded
    /// </summary>
    public string? CurrentFile { get; set; }
    
    /// <summary>
    /// Bytes downloaded so far
    /// </summary>
    public long BytesDownloaded { get; set; }
    
    /// <summary>
    /// Total bytes to download (0 if unknown)
    /// </summary>
    public long TotalBytes { get; set; }
    
    /// <summary>
    /// Download speed in bytes per second
    /// </summary>
    public double DownloadSpeedBytesPerSec { get; set; }
    
    /// <summary>
    /// Download speed in MB/s (calculated property)
    /// </summary>
    public double DownloadSpeedMBps => DownloadSpeedBytesPerSec / (1024.0 * 1024.0);
    
    /// <summary>
    /// Downloaded size in MB (calculated property)
    /// </summary>
    public double DownloadedMB => BytesDownloaded / (1024.0 * 1024.0);
    
    /// <summary>
    /// Total size in MB (calculated property)
    /// </summary>
    public double TotalMB => TotalBytes / (1024.0 * 1024.0);
    
    /// <summary>
    /// Estimated time remaining (calculated property)
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (DownloadSpeedBytesPerSec <= 0 || TotalBytes <= 0)
                return null;
                
            var remainingBytes = TotalBytes - BytesDownloaded;
            var secondsRemaining = remainingBytes / DownloadSpeedBytesPerSec;
            return TimeSpan.FromSeconds(secondsRemaining);
        }
    }
    
    /// <summary>
    /// Auto-calculate percentage from bytes if not explicitly set
    /// </summary>
    public new double Percentage
    {
        get
        {
            if (base.Percentage > 0)
                return base.Percentage;
                
            if (TotalBytes > 0)
                return Math.Round(BytesDownloaded * 100.0 / TotalBytes, 1);
                
            return 0;
        }
        set => base.Percentage = value;
    }
}
