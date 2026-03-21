namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Dalamud configuration
/// </summary>
public class DalamudConfig
{
    /// <summary>
    /// Managed plugin repository URL — always enforced, not user-editable.
    /// </summary>
    public const string ManagedPluginRepoUrl =
        "https://raw.githubusercontent.com/yanmucorp/PluginDistD17/refs/heads/main/pluginmaster.json";
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
}
