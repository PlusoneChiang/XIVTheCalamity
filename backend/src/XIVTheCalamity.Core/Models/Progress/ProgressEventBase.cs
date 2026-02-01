namespace XIVTheCalamity.Core.Models.Progress;

/// <summary>
/// Base class for all progress events
/// Used with IAsyncEnumerable pattern for progress reporting
/// </summary>
public abstract class ProgressEventBase
{
    /// <summary>
    /// Current stage/phase identifier
    /// </summary>
    public string Stage { get; set; } = string.Empty;
    
    /// <summary>
    /// i18n message key for frontend translation
    /// </summary>
    public string MessageKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public double Percentage { get; set; }
    
    /// <summary>
    /// Whether this operation is complete
    /// </summary>
    public bool IsComplete { get; set; }
    
    /// <summary>
    /// Whether an error occurred
    /// </summary>
    public bool HasError { get; set; }
    
    /// <summary>
    /// Error message key for i18n
    /// </summary>
    public string? ErrorMessageKey { get; set; }
    
    /// <summary>
    /// Error message (for non-i18n errors or details)
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Additional parameters for message interpolation
    /// </summary>
    public Dictionary<string, object>? Params { get; set; }
}
