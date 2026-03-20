using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.Linux.Umu;

/// <summary>
/// Downloads and manages the umu-launcher zipapp on Linux.
/// umu is a Proton-based game launcher that provides pressure-vessel sandboxing,
/// resolving FASMX64/Reloaded.Hooks native-AV issues that occur with raw wine64.
/// https://github.com/Open-Wine-Components/umu-launcher
/// </summary>
public class UmuDownloadService(ILogger<UmuDownloadService>? logger = null)
{
    private readonly PlatformPathService _platformPaths = PlatformPathService.Instance;
    private readonly HttpClient _httpClient = new();

    private const string ReleasesApiUrl = "https://api.github.com/repos/Open-Wine-Components/umu-launcher/releases/latest";
    private const string RequestUserAgent = "XIVTheCalamity/1.0";

    public string UmuDirectory => Path.Combine(_platformPaths.UserDataDirectory, "umu");
    public string UmuRunPath => Path.Combine(UmuDirectory, "umu-run");
    public string UmuVersionFilePath => Path.Combine(UmuDirectory, "version.txt");

    public bool IsAvailable() => File.Exists(UmuRunPath);

    public async Task<string?> GetInstalledVersionAsync()
    {
        if (!File.Exists(UmuVersionFilePath))
            return null;
        var raw = await File.ReadAllTextAsync(UmuVersionFilePath);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    /// <summary>
    /// Ensures umu-run is available, downloading if missing.
    /// This is called automatically during Proton environment initialization.
    /// </summary>
    public async Task EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (IsAvailable())
        {
            var version = await GetInstalledVersionAsync();
            logger?.LogInformation("[UMU] umu-run already available (version: {Version})", version ?? "unknown");
            return;
        }

        logger?.LogInformation("[UMU] umu-run not found, downloading...");
        await DownloadLatestAsync(cancellationToken);
    }

    private async Task DownloadLatestAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(UmuDirectory);

        var tempPath = string.Empty;
        try
        {
            var (tagName, assetUrl, assetName) = await GetLatestZipappAssetAsync(cancellationToken);
            logger?.LogInformation("[UMU] Downloading umu {Tag} from {Url}", tagName, assetUrl);

            tempPath = Path.Combine(UmuDirectory, $"umu-temp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempPath);
            var archivePath = Path.Combine(tempPath, assetName);

            await DownloadFileAsync(assetUrl, archivePath, cancellationToken);
            ExtractUmuRun(archivePath, tempPath);

            // The tar extracts to umu/umu-run subdirectory
            var extractedUmuRun = Path.Combine(tempPath, "umu", "umu-run");
            if (!File.Exists(extractedUmuRun))
                throw new FileNotFoundException("umu-run not found after extracting zipapp archive", extractedUmuRun);

            File.Copy(extractedUmuRun, UmuRunPath, overwrite: true);
            await File.WriteAllTextAsync(UmuVersionFilePath, tagName, cancellationToken);

            logger?.LogInformation("[UMU] umu-run installed successfully (version: {Tag})", tagName);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, recursive: true); }
                catch { /* ignore cleanup errors */ }
            }
        }
    }

    private async Task<(string TagName, string AssetUrl, string AssetName)> GetLatestZipappAssetAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
        request.Headers.Add("User-Agent", RequestUserAgent);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("Release tag_name missing");

        if (!root.TryGetProperty("assets", out var assets))
            throw new InvalidOperationException("No assets in umu release");

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.Contains("zipapp", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                var url = asset.GetProperty("browser_download_url").GetString()
                    ?? throw new InvalidOperationException("Asset download URL missing");
                return (tagName, url, name);
            }
        }

        throw new InvalidOperationException($"No zipapp.tar asset found in umu release {tagName}");
    }

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", RequestUserAgent);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream, ct);
    }

    private static void ExtractUmuRun(string archivePath, string destDir)
    {
        // Extract with system tar (always available on Linux)
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xf \"{archivePath}\" -C \"{destDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start tar process");
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"tar extraction failed (exit {process.ExitCode}): {stderr}");
        }
    }
}
