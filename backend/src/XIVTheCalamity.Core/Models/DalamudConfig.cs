namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Dalamud configuration
/// </summary>
public class DalamudConfig
{
    /// <summary>
    /// Managed plugin repository URLs — always enforced, not user-editable.
    /// Multiple URLs are separated by semicolons.
    /// </summary>
    public const string ManagedPluginRepoUrl =
        "https://raw.githubusercontent.com/yanmucorp/PluginDistD17/refs/heads/main/pluginmaster.json;" +
        "https://raw.githubusercontent.com/cycleapple/DalamudPlugins-TW/main/repo.json;" +
        "https://raw.githubusercontent.com/cycleapple/DalamudPlugins-TW-API13-Extended/main/repo.json";
    /// <summary>
    /// Enable Dalamud
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Injection delay (milliseconds)
    /// </summary>
    public int InjectDelay { get; set; } = 5000;
    
    /// <summary>
    /// Safe mode (disable third-party plugins)
    /// </summary>
    public bool SafeMode { get; set; } = false;
    
    /// <summary>
    /// Plugin repository URL
    /// </summary>
    public string PluginRepoUrl { get; set; } = ManagedPluginRepoUrl;

    /// <summary>
    /// Use EntryPoint injection mode (Dalamud.Injector starts the game directly)
    /// </summary>
    public bool UseEntryPoint { get; set; } = true;

    /// <summary>
    /// Use latest pre-release version from GitHub releases instead of the stable version channel.
    /// Falls back to the latest stable release if no pre-release is available.
    /// </summary>
    public bool UseLatestPreRelease { get; set; } = false;
}
