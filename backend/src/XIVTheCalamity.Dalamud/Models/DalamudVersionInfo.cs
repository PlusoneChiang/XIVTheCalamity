using System.Text.Json.Serialization;

namespace XIVTheCalamity.Dalamud.Models;

/// <summary>
/// Dalamud 套件版本資訊。
/// </summary>
public class DalamudVersionInfo
{
    /// <summary>完整套件識別，包含上游、TC 修補與選用的測試版次，例如 "13.0.0.16-tc.1-rc.1"。</summary>
    [JsonPropertyName("assemblyVersion")]
    public string AssemblyVersion { get; set; } = string.Empty;

    /// <summary>國際服上游版本，例如 "13.0.0.16"。</summary>
    [JsonPropertyName("upstreamVersion")]
    public string UpstreamVersion { get; set; } = string.Empty;

    /// <summary>同一上游版本的 TC 修補版次，從 1 開始；舊安裝紀錄未提供時為 0。</summary>
    [JsonPropertyName("tcRevision")]
    public int TcRevision { get; set; }

    /// <summary>預先發布測試版次，從 1 開始；正式版或舊安裝紀錄未提供時為 null。</summary>
    [JsonPropertyName("preReleaseRevision")]
    public int? PreReleaseRevision { get; set; }
    
    /// <summary>支援的遊戲版本 (e.g., "2025.12.05.0000.0000")</summary>
    [JsonPropertyName("supportedGameVer")]
    public string SupportedGameVer { get; set; } = string.Empty;
    
    /// <summary>是否需要 .NET Runtime</summary>
    [JsonPropertyName("runtimeRequired")]
    public bool RuntimeRequired { get; set; }
    
    /// <summary>.NET Runtime 版本 (e.g., "9.0.2")</summary>
    [JsonPropertyName("runtimeVersion")]
    public string RuntimeVersion { get; set; } = string.Empty;
    
    /// <summary>同一 Release 的版本固定 ZIP 下載網址。</summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>下載套件的 SHA-256，為 64 位十六進位字串。</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
    
    /// <summary>追蹤頻道 (e.g., "custom")</summary>
    [JsonPropertyName("track")]
    public string Track { get; set; } = string.Empty;
    
    /// <summary>顯示名稱 (e.g., "XIV on Mac in TC")</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>密鑰 (可選)</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>驗證新來源的發布契約；讀取舊安裝紀錄時不呼叫。</summary>
    public void Validate()
    {
        if (!Version.TryParse(UpstreamVersion, out var upstream) || upstream.Revision < 0 ||
            upstream.ToString() != UpstreamVersion || TcRevision < 1 || PreReleaseRevision is < 1)
            throw new InvalidDataException("Dalamud 上游版本或修補／測試版次無效。");

        var expected = $"{UpstreamVersion}-tc.{TcRevision}";
        if (PreReleaseRevision is int revision)
            expected += $"-rc.{revision}";
        if (AssemblyVersion != expected)
            throw new InvalidDataException("Dalamud 完整套件版本與三個版本欄位不一致。");

        if (string.IsNullOrWhiteSpace(SupportedGameVer) || !RuntimeRequired ||
            !Version.TryParse(RuntimeVersion, out var runtime) || runtime.Build < 0 || runtime.Revision >= 0 ||
            runtime.ToString() != RuntimeVersion)
            throw new InvalidDataException("Dalamud 支援遊戲版本或 Runtime 版本無效。");
        if (!Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(Sha256) || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Dalamud 下載網址或 SHA-256 無效。");
    }
}
