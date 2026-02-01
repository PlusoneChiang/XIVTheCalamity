namespace XIVTheCalamity.Core.Models.Progress;

/// <summary>
/// Progress event for environment initialization operations
/// </summary>
public class EnvironmentProgressEvent : ProgressEventBase
{
    /// <summary>
    /// Number of completed items
    /// </summary>
    public int CompletedItems { get; set; }
    
    /// <summary>
    /// Total number of items
    /// </summary>
    public int TotalItems { get; set; }
    
    /// <summary>
    /// Current operation description
    /// </summary>
    public string? CurrentOperation { get; set; }
    
    /// <summary>
    /// Extra data for specific environment operations
    /// </summary>
    public Dictionary<string, object>? ExtraData { get; set; }
    
    /// <summary>
    /// Auto-calculate percentage from items if not explicitly set
    /// </summary>
    public new double Percentage
    {
        get
        {
            if (base.Percentage > 0)
                return base.Percentage;
                
            if (TotalItems > 0)
                return Math.Round(CompletedItems * 100.0 / TotalItems, 1);
                
            return 0;
        }
        set => base.Percentage = value;
    }
}
