using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace XIVTheCalamity.Dalamud.Models;

/// <summary>來源發布的 versionInfo.json；與本機安裝紀錄 version.json 分開。</summary>
public class DalamudReleaseInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("upstreamVersion")]
    public string UpstreamVersion { get; set; } = string.Empty;

    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; set; } = string.Empty;

    [JsonPropertyName("releaseTag")]
    public string ReleaseTag { get; set; } = string.Empty;

    [JsonPropertyName("runtimeRequirements")]
    public JsonElement RuntimeRequirements { get; set; }

    internal DalamudVersionInfo ToVersionInfo(GitHubRelease release, string checksum)
    {
        var match = Regex.Match(Id ?? "", @"\A(?<upstream>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)-tc\.(?<tc>[0-9]+)(?:-rc\.(?<rc>[0-9]+))?\z");
        if (!match.Success || match.Groups["upstream"].Value != UpstreamVersion ||
            !int.TryParse(match.Groups["tc"].Value, out var tcRevision) ||
            (match.Groups["rc"].Success && !int.TryParse(match.Groups["rc"].Value, out _)))
            throw new InvalidDataException("Dalamud 來源套件版本無效。");
        var preReleaseRevision = match.Groups["rc"].Success ? int.Parse(match.Groups["rc"].Value) : (int?)null;
        if (ReleaseTag != release.TagName || ReleaseTag != $"v{Id}" || release.Prerelease != preReleaseRevision.HasValue)
            throw new InvalidDataException("Dalamud 版本資訊與 Release 標籤或預先發布狀態不一致。");

        var archiveName = $"dalamud-{Id}.zip";
        var archive = release.Assets.SingleOrDefault(a => a.Name == archiveName)
            ?? throw new InvalidDataException("Dalamud Release 缺少對應 ZIP 附件。");
        var hashParts = checksum.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (hashParts.Length != 2 || hashParts[1] != archiveName)
            throw new InvalidDataException("Dalamud SHA-256 附件未對應所選 ZIP。");

        // patcherRuntime 是製作工具的環境，遊戲端只能依 runtimeRequirements 安裝。
        var injector = RuntimeRequirements.GetProperty("Dalamud.Injector.runtimeconfig.json").GetProperty("framework");
        var runtimeVersion = injector.GetProperty("version").GetString() ?? "";
        if (injector.GetProperty("name").GetString() != "Microsoft.NETCore.App")
            throw new InvalidDataException("Dalamud Injector 的 Runtime 需求無法辨識。");
        var frameworks = RuntimeRequirements.GetProperty("Dalamud.runtimeconfig.json").GetProperty("frameworks")
            .EnumerateArray().ToList();
        // shortcut: 目前共用單一 Runtime 版本，來源需求分歧時須擴充安裝流程。
        if (frameworks.Count != 2 ||
            !frameworks.Any(f => f.GetProperty("name").GetString() == "Microsoft.NETCore.App") ||
            !frameworks.Any(f => f.GetProperty("name").GetString() == "Microsoft.WindowsDesktop.App") ||
            frameworks.Any(f => f.GetProperty("version").GetString() != runtimeVersion))
            throw new InvalidDataException("Dalamud 與 Injector 必須使用同版 .NET 與 Windows Desktop Runtime。");

        var version = new DalamudVersionInfo
        {
            AssemblyVersion = Id!,
            UpstreamVersion = UpstreamVersion,
            TcRevision = tcRevision,
            PreReleaseRevision = preReleaseRevision,
            SupportedGameVer = GameVersion,
            RuntimeRequired = true,
            RuntimeVersion = runtimeVersion,
            DownloadUrl = archive.BrowserDownloadUrl,
            Sha256 = hashParts[0],
            Track = release.Prerelease ? "pre-release" : "stable",
            DisplayName = release.Name
        };
        version.Validate();
        return version;
    }
}
