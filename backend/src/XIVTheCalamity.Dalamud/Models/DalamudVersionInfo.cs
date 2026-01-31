using System.Text.Json.Serialization;

namespace XIVTheCalamity.Dalamud.Models;

/// <summary>
/// Dalamud 版本資訊 (來自 dalamud_version.json)
/// </summary>
public class DalamudVersionInfo
{
    /// <summary>版本號 (e.g., "25-12-26-01")</summary>
    [JsonPropertyName("assemblyVersion")]
    public string AssemblyVersion { get; set; } = string.Empty;
    
    /// <summary>支援的遊戲版本 (e.g., "2025.12.05.0000.0000")</summary>
    [JsonPropertyName("supportedGameVer")]
    public string SupportedGameVer { get; set; } = string.Empty;
    
    /// <summary>是否需要 .NET Runtime</summary>
    [JsonPropertyName("runtimeRequired")]
    public bool RuntimeRequired { get; set; }
    
    /// <summary>.NET Runtime 版本 (e.g., "9.0.2")</summary>
    [JsonPropertyName("runtimeVersion")]
    public string RuntimeVersion { get; set; } = string.Empty;
    
    /// <summary>Dalamud 下載 URL (latest.7z)</summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;
    
    /// <summary>追蹤頻道 (e.g., "custom")</summary>
    [JsonPropertyName("track")]
    public string Track { get; set; } = string.Empty;
    
    /// <summary>顯示名稱 (e.g., "XIV on Mac in TC")</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>密鑰 (可選)</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }
}
