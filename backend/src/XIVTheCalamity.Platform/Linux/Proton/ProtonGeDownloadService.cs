using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.Linux.Proton;

/// <summary>
/// Downloads and manages Proton-GE runtime on Linux.
/// Uses the latest release from GloriousEggroll/proton-ge-custom.
/// </summary>
public class ProtonGeDownloadService(
    ILogger<ProtonGeDownloadService>? logger = null)
{
    private readonly PlatformPathService _platformPaths = PlatformPathService.Instance;
    private readonly HttpClient _httpClient = new();

    public const string PinnedVersion = "GE-Proton11-1";
    private const string PinnedReleaseApiUrl = $"https://api.github.com/repos/GloriousEggroll/proton-ge-custom/releases/tags/{PinnedVersion}";
    private const string RequestUserAgent = "XIVTheCalamity/1.0";

    public string ProtonBaseDirectory => Path.Combine(_platformPaths.UserDataDirectory, "proton");
    public string ProtonCurrentDirectory => Path.Combine(ProtonBaseDirectory, "current");
    public string ProtonWinePath => File.Exists(Path.Combine(ProtonCurrentDirectory, "files", "bin", "wine64")) 
        ? Path.Combine(ProtonCurrentDirectory, "files", "bin", "wine64") 
        : Path.Combine(ProtonCurrentDirectory, "files", "bin", "wine");
    public string ProtonVersionFilePath => Path.Combine(ProtonBaseDirectory, "version.txt");

    public async Task<DownloadStatus> GetStatusAsync()
    {
        if (!Directory.Exists(ProtonCurrentDirectory) || !File.Exists(ProtonWinePath))
        {
            return new DownloadStatus
            {
                IsInstalled = false
            };
        }

        string? version = null;
        if (File.Exists(ProtonVersionFilePath))
        {
            var rawVersion = await File.ReadAllTextAsync(ProtonVersionFilePath);
            if (!string.IsNullOrWhiteSpace(rawVersion))
            {
                version = rawVersion.Trim();
            }
        }

        return new DownloadStatus
        {
            IsInstalled = true,
            Version = version,
            InstalledPath = ProtonCurrentDirectory
        };
    }

    public async IAsyncEnumerable<DownloadProgressEvent> DownloadLatestAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[PROTON-GE] Starting Proton-GE latest release download");

        yield return new DownloadProgressEvent
        {
            Stage = "fetch_release",
            MessageKey = "progress.checking_wine",
            Percentage = 5
        };

        var tempDir = string.Empty;

        try
        {
            var release = await GetLatestReleaseAsync(cancellationToken);
            logger?.LogInformation("[PROTON-GE] Latest release: {Tag}, asset: {Asset}", release.TagName, release.AssetName);

            Directory.CreateDirectory(ProtonBaseDirectory);
            tempDir = Path.Combine(ProtonBaseDirectory, $"proton-ge-temp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var archivePath = Path.Combine(tempDir, release.AssetName);

            await foreach (var progress in DownloadFileAsync(release.DownloadUrl, archivePath, cancellationToken))
            {
                yield return progress;
            }

            if (File.Exists(archivePath))
            {
                var fileInfo = new FileInfo(archivePath);
                logger?.LogInformation("[PROTON-GE] Download complete. Archive size: {Size} bytes", fileInfo.Length);
            }
            else
            {
                logger?.LogError("[PROTON-GE] Downloaded archive file not found at: {Path}", archivePath);
            }

            yield return new DownloadProgressEvent
            {
                Stage = "extracting",
                MessageKey = "progress.extracting_wine",
                Percentage = 80
            };

            await ExtractArchiveAsync(archivePath, tempDir, cancellationToken);

            yield return new DownloadProgressEvent
            {
                Stage = "installing",
                MessageKey = "progress.installing_wine",
                Percentage = 90
            };

            var extractedDir = FindExtractedDirectory(tempDir);
            
            // Log folders in tempDir for debugging
            try
            {
                var dirs = Directory.GetDirectories(tempDir);
                logger?.LogInformation("[PROTON-GE] Extracted directories found in tempDir: {Dirs}", string.Join(", ", dirs));
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[PROTON-GE] Failed to list tempDir directories");
            }

            if (string.IsNullOrEmpty(extractedDir))
            {
                logger?.LogError("[PROTON-GE] Extracted Proton-GE directory not found in tempDir");
                yield return new DownloadProgressEvent
                {
                    Stage = "error",
                    MessageKey = "error.wine_download_failed",
                    HasError = true,
                    ErrorMessage = "Proton-GE extracted directory not found"
                };
                yield break;
            }

            if (Directory.Exists(ProtonCurrentDirectory))
            {
                logger?.LogInformation("[PROTON-GE] Removing existing Proton installation at {Path}", ProtonCurrentDirectory);
                Directory.Delete(ProtonCurrentDirectory, true);
            }

            Directory.Move(extractedDir, ProtonCurrentDirectory);
            await File.WriteAllTextAsync(ProtonVersionFilePath, release.TagName, cancellationToken);

            if (!File.Exists(ProtonWinePath))
            {
                yield return new DownloadProgressEvent
                {
                    Stage = "error",
                    MessageKey = "error.wine_download_failed",
                    HasError = true,
                    ErrorMessage = $"Proton wine64 executable not found after install: {ProtonWinePath}"
                };
                yield break;
            }

            logger?.LogInformation("[PROTON-GE] Installed successfully: {Tag} at {Path}", release.TagName, ProtonCurrentDirectory);
            yield return new DownloadProgressEvent
            {
                Stage = "complete",
                MessageKey = "progress.wine_downloaded",
                Percentage = 100,
                IsComplete = true
            };
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[PROTON-GE] Failed to clean temporary directory");
            }
        }
    }

    private async Task<ProtonReleaseAsset> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PinnedReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd(RequestUserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagNameElement))
        {
            throw new InvalidOperationException("GitHub release response missing tag_name");
        }

        var tagName = tagNameElement.GetString();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidOperationException("Latest Proton-GE release tag is empty");
        }

        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("GitHub release response missing assets");
        }

        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement) ||
                !asset.TryGetProperty("browser_download_url", out var urlElement))
            {
                continue;
            }

            var assetName = nameElement.GetString();
            var downloadUrl = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) &&
                !assetName.Contains("aarch64", StringComparison.OrdinalIgnoreCase))
            {
                return new ProtonReleaseAsset(tagName, assetName, downloadUrl);
            }
        }

        throw new InvalidOperationException($"No .tar.gz asset found for latest Proton-GE release: {tagName}");
    }

    private async IAsyncEnumerable<DownloadProgressEvent> DownloadFileAsync(
        string url,
        string destinationPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(RequestUserAgent);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var fileName = Path.GetFileName(destinationPath);

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        var lastReportTime = DateTime.UtcNow;
        long lastReportedBytes = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            var now = DateTime.UtcNow;
            if (totalBytes > 0 && (now - lastReportTime).TotalMilliseconds >= 500)
            {
                var elapsedSeconds = (now - lastReportTime).TotalSeconds;
                var bytesDownloadedSinceLastReport = totalRead - lastReportedBytes;
                var downloadSpeed = elapsedSeconds > 0 ? bytesDownloadedSinceLastReport / elapsedSeconds : 0;
                var percentage = 10 + (int)(totalRead * 60.0 / totalBytes);

                yield return new DownloadProgressEvent
                {
                    Stage = "downloading",
                    MessageKey = "progress.downloading_wine",
                    BytesDownloaded = totalRead,
                    TotalBytes = totalBytes,
                    Percentage = percentage,
                    CurrentFile = fileName,
                    DownloadSpeedBytesPerSec = downloadSpeed
                };

                lastReportTime = now;
                lastReportedBytes = totalRead;
            }
        }
    }

    private async Task ExtractArchiveAsync(string archivePath, string destinationDir, CancellationToken cancellationToken)
    {
        logger?.LogInformation("[PROTON-GE] Running tar command: tar xzf \"{Archive}\" -C \"{Dest}\"", archivePath, destinationDir);
        var startInfo = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"xzf \"{archivePath}\" -C \"{destinationDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new Exception("Failed to start tar process for Proton extraction");
        }

        await process.WaitForExitAsync(cancellationToken);
        logger?.LogInformation("[PROTON-GE] Tar process exited with code: {Code}", process.ExitCode);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            logger?.LogError("[PROTON-GE] Tar extraction failed: {Error}", error);
            throw new Exception($"Proton archive extraction failed: {error}");
        }
    }

    private string? FindExtractedDirectory(string tempDir)
    {
        var directCandidate = Directory.GetDirectories(tempDir)
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "files", "bin", "wine64")) || File.Exists(Path.Combine(d, "files", "bin", "wine")));

        if (!string.IsNullOrEmpty(directCandidate))
        {
            return directCandidate;
        }

        foreach (var file in Directory.EnumerateFiles(tempDir, "wine", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (!normalized.EndsWith("/files/bin/wine", StringComparison.Ordinal))
            {
                continue;
            }

            var root = Directory.GetParent(Directory.GetParent(Directory.GetParent(file)!.FullName)!.FullName)!.FullName;
            if (Directory.Exists(root))
            {
                return root;
            }
        }

        foreach (var file in Directory.EnumerateFiles(tempDir, "wine64", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (!normalized.EndsWith("/files/bin/wine64", StringComparison.Ordinal))
            {
                continue;
            }

            var root = Directory.GetParent(Directory.GetParent(Directory.GetParent(file)!.FullName)!.FullName)!.FullName;
            if (Directory.Exists(root))
            {
                return root;
            }
        }

        return null;
    }

    private sealed record ProtonReleaseAsset(string TagName, string AssetName, string DownloadUrl);
}

public class DownloadStatus
{
    public bool IsInstalled { get; set; }
    public string? Version { get; set; }
    public string? InstalledPath { get; set; }
}
