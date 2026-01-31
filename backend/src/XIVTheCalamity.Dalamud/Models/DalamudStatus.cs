namespace XIVTheCalamity.Dalamud.Models;

/// <summary>
/// Dalamud state
/// </summary>
public enum DalamudState
{
    /// <summary>Not installed</summary>
    NotInstalled,
    
    /// <summary>Installed and up to date</summary>
    UpToDate,
    
    /// <summary>Update available</summary>
    UpdateAvailable,
    
    /// <summary>Checking for updates</summary>
    Checking,
    
    /// <summary>Downloading/updating</summary>
    Updating,
    
    /// <summary>Update failed</summary>
    Failed,
    
    /// <summary>Game version mismatch</summary>
    GameVersionMismatch
}

/// <summary>
/// Dalamud status information
/// </summary>
public class DalamudStatus
{
    /// <summary>Current state</summary>
    public DalamudState State { get; set; } = DalamudState.NotInstalled;
    
    /// <summary>Local version (null = not installed)</summary>
    public string? LocalVersion { get; set; }
    
    /// <summary>Remote version</summary>
    public string? RemoteVersion { get; set; }
    
    /// <summary>Supported game version</summary>
    public string? SupportedGameVersion { get; set; }
    
    /// <summary>Runtime installed</summary>
    public bool RuntimeInstalled { get; set; }
    
    /// <summary>Runtime version</summary>
    public string? RuntimeVersion { get; set; }
    
    /// <summary>Assets installed</summary>
    public bool AssetsInstalled { get; set; }
    
    /// <summary>Assets version</summary>
    public int AssetsVersion { get; set; }
    
    /// <summary>Error message (if any)</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Dalamud update progress
/// </summary>
public class DalamudUpdateProgress
{
    /// <summary>Current stage</summary>
    public DalamudUpdateStage Stage { get; set; }
    
    /// <summary>Current file name</summary>
    public string? CurrentFile { get; set; }
    
    /// <summary>Completed items count</summary>
    private int _completedItems;
    public int CompletedItems 
    { 
        get => _completedItems;
        set => _completedItems = value;
    }
    
    /// <summary>Increment completed items count (thread-safe)</summary>
    public int IncrementCompleted() => Interlocked.Increment(ref _completedItems);
    
    /// <summary>Total items count</summary>
    public int TotalItems { get; set; }
    
    /// <summary>Downloaded bytes</summary>
    public long BytesDownloaded { get; set; }
    
    /// <summary>Total bytes</summary>
    public long TotalBytes { get; set; }
    
    /// <summary>Completion percentage (0-100)</summary>
    public double Percentage => TotalBytes > 0 
        ? Math.Round(BytesDownloaded * 100.0 / TotalBytes, 1) 
        : (TotalItems > 0 ? Math.Round(CompletedItems * 100.0 / TotalItems, 1) : 0);
    
    /// <summary>Is complete</summary>
    public bool IsComplete { get; set; }
    
    /// <summary>Has error</summary>
    public bool HasError { get; set; }
    
    /// <summary>Error message</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Update stage
/// </summary>
public enum DalamudUpdateStage
{
    /// <summary>Checking version</summary>
    CheckingVersion,
    
    /// <summary>Downloading Dalamud</summary>
    DownloadingDalamud,
    
    /// <summary>Extracting Dalamud</summary>
    ExtractingDalamud,
    
    /// <summary>Downloading Runtime</summary>
    DownloadingRuntime,
    
    /// <summary>Extracting Runtime</summary>
    ExtractingRuntime,
    
    /// <summary>Downloading Assets</summary>
    DownloadingAssets,
    
    /// <summary>Verifying Assets</summary>
    VerifyingAssets,
    
    /// <summary>Complete</summary>
    Complete,
    
    /// <summary>Failed</summary>
    Failed
}
