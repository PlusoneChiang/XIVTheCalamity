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
    /// Maximum framerate limit. 0 = unlimited (default).
    /// Mapped to DXVK_FRAME_RATE environment variable.
    /// </summary>
    public int MaxFramerate { get; set; } = 0;
    
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
    /// Enable ALSA spatial downmix override.
    /// </summary>
    public bool WineAlsaSpacialEnabled { get; set; } = false;

    /// <summary>
    /// Override ALSA audio channel count (e.g. 2 for stereo, 6 for 5.1).
    /// </summary>
    public int? WineAlsaChannels { get; set; } = null;

    /// <summary>
    /// Use Proton 11's built-in Optiscaler support (PROTON_USE_OPTISCALER=1).
    /// </summary>
    public bool UseProtonOptiscaler { get; set; } = false;

    /// <summary>
    /// Use Proton 11's built-in Discord Bridge support (PROTON_DISCORD_BRIDGE=1).
    /// </summary>
    public bool UseProtonDiscordBridge { get; set; } = false;

    /// <summary>
    /// Launch options string, supports %command% placeholder (like Steam launch options).
    /// Example: "~/fgmod/fgmod %command%"
    /// Default is "%command%" which means no wrapper.
    /// </summary>
    public string LaunchOptions { get; set; } = "%command%";
}
