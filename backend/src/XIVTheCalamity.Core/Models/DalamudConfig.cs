namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Dalamud configuration
/// </summary>
public class DalamudConfig
{
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
    public string PluginRepoUrl { get; set; } = "https://kamori.goats.dev/Dalamud/Release/Meta";
}
