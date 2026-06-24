using System;
using System.Text.Json.Serialization;

namespace XIVTheCalamity.Core.Models;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string Tag_name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string Browser_download_url { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public record AppUpdateCheckResult(
    bool UpdateAvailable,
    string Version,
    string ReleaseNotes,
    string DownloadUrl,
    long Size
);

public record AppUpdateProgress(
    double Percent,
    long BytesPerSecond
);
