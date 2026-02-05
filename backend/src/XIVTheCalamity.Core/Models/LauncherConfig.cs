namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Launcher configuration
/// </summary>
public class LauncherConfig
{
    /// <summary>
    /// Developer mode (enable verbose debug logs)
    /// </summary>
    public bool DevelopmentMode { get; set; } = false;
    
    /// <summary>
    /// Use encrypted launch arguments
    /// </summary>
    public bool EncryptedArguments { get; set; } = true;
    
    /// <summary>
    /// Exit launcher when game exits
    /// </summary>
    public bool ExitWithGame { get; set; } = true;
    
    /// <summary>
    /// Detect non-zero exit codes and report errors
    /// </summary>
    public bool NonZeroExitError { get; set; } = true;
    
    /// <summary>
    /// Show Dalamud tab in settings (frontend-only, developer mode)
    /// </summary>
    public bool ShowDalamudTab { get; set; } = false;
}
