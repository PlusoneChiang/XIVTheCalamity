namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Wine-XIV configuration (Linux only)
/// </summary>
public class WineXIVConfig
{
    /// <summary>
    /// Enable DXVK HUD display
    /// </summary>
    public bool DxvkHudEnabled { get; set; } = false;
    
    /// <summary>
    /// Maximum framerate (30 - 240)
    /// </summary>
    public int MaxFramerate { get; set; } = 60;
    
    /// <summary>
    /// Enable Esync synchronization
    /// </summary>
    public bool EsyncEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable Fsync synchronization (Wine-XIV specific)
    /// </summary>
    public bool FsyncEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable GameMode for performance optimization (Linux only)
    /// </summary>
    public bool GameModeEnabled { get; set; } = true;
    
    /// <summary>
    /// Wine debug flags (e.g., "-all,+module" or empty to disable)
    /// </summary>
    public string WineDebug { get; set; } = "";
}
