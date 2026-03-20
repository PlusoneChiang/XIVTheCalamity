using System.Text.Json.Serialization;

namespace XIVTheCalamity.Dalamud.Models;

/// <summary>
/// Dalamud Assets manifest (from asset.json)
/// </summary>
public class DalamudAssetManifest
{
    /// <summary>Asset version number</summary>
    [JsonPropertyName("Version")]
    public int Version { get; set; }

    /// <summary>Asset file list</summary>
    [JsonPropertyName("Assets")]
    public List<DalamudAssetEntry> Assets { get; set; } = [];

    /// <summary>
    /// Optional pre-packaged zip URL. When set and a download is needed,
    /// the entire package is downloaded and extracted instead of per-file downloads.
    /// Hash verification still uses the Assets list after extraction.
    /// </summary>
    [JsonPropertyName("Package")]
    public string? Package { get; set; }
}

/// <summary>
/// Single asset file information
/// </summary>
public class DalamudAssetEntry
{
    /// <summary>Download URL</summary>
    [JsonPropertyName("Url")]
    public string Url { get; set; } = string.Empty;
    
    /// <summary>Relative file path (e.g., "UIRes/logo.png")</summary>
    [JsonPropertyName("FileName")]
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>SHA1 hash (uppercase); empty means no verification required</summary>
    [JsonPropertyName("Hash")]
    public string Hash { get; set; } = string.Empty;
}
