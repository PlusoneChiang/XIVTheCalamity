using System.Runtime.InteropServices;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Game configuration
/// </summary>
public class GameConfig
{
    /// <summary>
    /// Game installation path (no default, must be selected via setup wizard)
    /// </summary>
    public string GamePath { get; set; } = "";
    
    /// <summary>
    /// Game region (fixed to Traditional Chinese server)
    /// </summary>
    public string Region { get; set; } = "TraditionalChinese";
    
    /// <summary>
    /// Get game configuration directory path (fixed location, not configurable)
    /// </summary>
    public string GetConfigPath()
    {
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            appSupport = Path.Combine(
                HomePathService.GetEffectiveHomePath(),
                "Library", "Application Support");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            appSupport = Path.Combine(
                HomePathService.GetEffectiveHomePath(),
                ".config");
        }
        
        return Path.Combine(appSupport, "XIVTheCalamity", "ffxivConfig");
    }
}
