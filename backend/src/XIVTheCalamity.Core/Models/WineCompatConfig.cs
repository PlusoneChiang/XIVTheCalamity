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
    
    /// <summary>
    /// IME candidate window X position (% from left edge of game window, 0-100)
    /// Default 25 = 25% from left
    /// </summary>
    public int ImeCandidatePositionX { get; set; } = 25;
    
    /// <summary>
    /// IME candidate window Y position (% from top edge of game window, 0-100)
    /// Default 85 = 85% from top (near bottom, where chat typically is)
    /// </summary>
    public int ImeCandidatePositionY { get; set; } = 85;
}
