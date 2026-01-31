namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Application configuration
/// </summary>
public class AppConfig
{
    public GameConfig Game { get; set; } = new();
    public WineConfig? Wine { get; set; }  // macOS only
    public WineXIVConfig? WineXIV { get; set; }  // Linux only
    public DalamudConfig Dalamud { get; set; } = new();
    public LauncherConfig Launcher { get; set; } = new();
}
