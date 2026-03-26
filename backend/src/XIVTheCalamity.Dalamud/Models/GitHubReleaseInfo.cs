using System.Text.Json.Serialization;

namespace XIVTheCalamity.Dalamud.Models;

/// <summary>
/// GitHub Release entry from the Releases API
/// </summary>
public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

/// <summary>
/// A single asset attached to a GitHub Release
/// </summary>
public class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;
}
