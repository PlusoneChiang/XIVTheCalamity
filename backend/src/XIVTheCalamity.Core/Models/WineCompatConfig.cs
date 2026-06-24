namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Wine compatibility and layout configuration
/// </summary>
public class WineCompatConfig
{
    /// <summary>
    /// Enable audio routing
    /// </summary>
    public bool AudioRouting { get; set; } = false;
    
    /// <summary>
    /// Enable Home alias compatibility mode on macOS.
    /// </summary>
    public bool UseHomeAlias { get; set; } = false;
    
    /// <summary>
    /// Map left Option key to Alt (macOS)
    /// </summary>
    public bool LeftOptionIsAlt { get; set; } = true;
    
    /// <summary>
    /// Map right Option key to Alt (macOS)
    /// </summary>
    public bool RightOptionIsAlt { get; set; } = true;
    
    /// <summary>
    /// Map left Command key to Ctrl (macOS)
    /// </summary>
    public bool LeftCommandIsCtrl { get; set; } = true;
    
    /// <summary>
    /// Map right Command key to Ctrl (macOS)
    /// </summary>
    public bool RightCommandIsCtrl { get; set; } = true;
}
