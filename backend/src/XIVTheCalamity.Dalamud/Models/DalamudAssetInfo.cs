using System.Text.Json.Serialization;

namespace XIVTheCalamity.Dalamud.Models;

/// <summary>
/// Dalamud Assets manifest (from dalamud_asset.json)
/// </summary>
public class DalamudAssetManifest
{
    /// <summary>Asset version number</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }
    
    /// <summary>Asset file list</summary>
    [JsonPropertyName("assets")]
    public List<DalamudAssetEntry> Assets { get; set; } = [];
}

/// <summary>
/// Single asset file information
/// </summary>
public class DalamudAssetEntry
{
    /// <summary>Download URL</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    
    /// <summary>Relative file path (e.g., "UIRes/logo.png")</summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>SHA1 hash (uppercase)</summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;
}
