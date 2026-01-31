namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Application configuration
/// </summary>
public class AppConfig
{
    public GameConfig Game { get; set; } = new();
    public WineConfig Wine { get; set; } = new();
    public DalamudConfig Dalamud { get; set; } = new();
    public LauncherConfig Launcher { get; set; } = new();
}
