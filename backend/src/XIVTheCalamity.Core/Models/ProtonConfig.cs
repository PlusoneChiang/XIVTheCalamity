namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Proton configuration (Linux)
/// </summary>
public class ProtonConfig
{
    /// <summary>
    /// Enable DXVK HUD display
    /// </summary>
    public bool DxvkHudEnabled { get; set; } = false;
    
    /// <summary>
    /// Enable Fsync synchronization (Linux)
    /// </summary>
    public bool FsyncEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable Esync synchronization
    /// </summary>
    public bool EsyncEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable GameMode optimization
    /// </summary>
    public bool GameModeEnabled { get; set; } = true;
    
    /// <summary>
    /// Maximum framerate (30 - 240)
    /// </summary>
    public int MaxFramerate { get; set; } = 60;
    
    /// <summary>
    /// Wine debug flags (e.g., "-all,+module" or empty to disable)
    /// </summary>
    public string WineDebug { get; set; } = "";
}
