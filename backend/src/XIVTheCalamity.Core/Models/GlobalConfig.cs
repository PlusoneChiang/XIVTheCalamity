namespace XIVTheCalamity.Core.Models;

/// <summary>不隨設定檔切換的啟動器設定。</summary>
public class GlobalConfig
{
    public string GamePath { get; set; } = "";
    public bool DevelopmentMode { get; set; }
    public bool EnablePreRelease { get; set; }
    public bool ShowDalamudTab { get; set; }
}
