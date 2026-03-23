namespace XIVTheCalamity.Core.Models;

/// <summary>
/// GE-Proton configuration (Linux only)
/// </summary>
public class ProtonGeConfig
{
    /// <summary>
    /// Enable DXVK HUD display
    /// </summary>
    public bool DxvkHudEnabled { get; set; } = false;

    /// <summary>
    /// Enable DXVK GPLAsync mode.
    /// When enabled, downloads the GPLAsync build and installs it to the Wine prefix,
    /// replacing the DXVK bundled in GE-Proton.
    /// When disabled, the GPLAsync DLLs are removed and GE-Proton's bundled DXVK is used.
    /// </summary>
    public bool DxvkAsyncEnabled { get; set; } = false;
    
    /// <summary>
    /// Maximum framerate (30 - 240)
    /// </summary>
    public int MaxFramerate { get; set; } = 60;
    
    /// <summary>
    /// Enable Esync synchronization
    /// </summary>
    public bool EsyncEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable Fsync synchronization
    /// </summary>
    public bool FsyncEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable GameMode for performance optimization (Linux only)
    /// </summary>
    public bool GameModeEnabled { get; set; } = false;
    
    /// <summary>
    /// Wine debug flags (e.g., "-all,+module" or empty to disable)
    /// </summary>
    public string WineDebug { get; set; } = "";

    /// <summary>
    /// Extra environment variables to pass to the Wine/Proton environment.
    /// Key-value pairs that override or extend the default environment.
    /// </summary>
    public Dictionary<string, string> ExtraEnvironmentVariables { get; set; } = new();

    /// <summary>
    /// Launch options string, supports %command% placeholder (like Steam launch options).
    /// Example: "~/fgmod/fgmod %command%"
    /// Default is "%command%" which means no wrapper.
    /// </summary>
    public string LaunchOptions { get; set; } = "%command%";
}
