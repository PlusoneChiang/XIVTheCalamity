namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Wine performance configuration
/// </summary>
public class WinePerformanceConfig
{
    /// <summary>
    /// Enable Msync synchronization
    /// </summary>
    public bool Msync { get; set; } = true;
    
    /// <summary>
    /// Wine debug flags (e.g., "-all,+module" or empty to disable)
    /// </summary>
    public string WineDebug { get; set; } = "";
}
