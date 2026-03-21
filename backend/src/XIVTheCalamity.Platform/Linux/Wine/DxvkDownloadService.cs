using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.Linux.Wine;

/// <summary>
/// Downloads and manages DXVK (Vulkan-based D3D translation layer) for Linux.
/// DXVK is a separate component from Wine — Wine-XIV does NOT bundle DXVK.
/// Reference: goatcorp/XIVLauncher.Core Dxvk.cs
/// </summary>
public class DxvkDownloadService(
    ILogger<DxvkDownloadService>? logger = null)
{
    private readonly PlatformPathService _platformPaths = PlatformPathService.Instance;
    private readonly HttpClient _httpClient = new();

    // DXVK GPLAsync release
    private const string DxvkReleaseName = "dxvk-gplasync-v2.7.1-1";
    private const string DxvkDownloadUrl = "https://raw.githubusercontent.com/PlusoneChiang/wine-xiv-git/refs/heads/master/dxvk-gplasync-v2.7.1-1.tar.gz";

    private string DxvkBaseDirectory => Path.Combine(_platformPaths.UserDataDirectory, "dxvk");
    private string DxvkVersionDirectory => Path.Combine(DxvkBaseDirectory, DxvkReleaseName);
    public string DxvkDllDirectory => Path.Combine(DxvkVersionDirectory, "x64");

    /// <summary>
    /// Check if DXVK is already downloaded
    /// </summary>
    public bool IsInstalled()
    {
        if (!Directory.Exists(DxvkDllDirectory))
            return false;

        // Verify at least the core DLLs exist
        var requiredDlls = new[] { "d3d11.dll", "dxgi.dll" };
        return requiredDlls.All(dll => File.Exists(Path.Combine(DxvkDllDirectory, dll)));
    }

    /// <summary>
    /// Download DXVK if not already present
    /// </summary>
    public async IAsyncEnumerable<DownloadProgressEvent> EnsureDxvkAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (IsInstalled())
        {
            logger?.LogInformation("[DXVK] DXVK {Version} already installed at {Path}", DxvkReleaseName, DxvkDllDirectory);
            yield return new DownloadProgressEvent
            {
                Stage = "complete",
                MessageKey = "progress.dxvk_ready",
                Percentage = 100,
                IsComplete = true
            };
            yield break;
        }

        logger?.LogInformation("[DXVK] DXVK not found, downloading {Version}", DxvkReleaseName);
        logger?.LogInformation("[DXVK] URL: {Url}", DxvkDownloadUrl);

        Directory.CreateDirectory(DxvkBaseDirectory);

        var tempDir = Path.Combine(DxvkBaseDirectory, $"dxvk-temp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var archivePath = Path.Combine(tempDir, "dxvk.tar.gz");

        try
        {
            // Download
            yield return new DownloadProgressEvent
            {
                Stage = "downloading",
                MessageKey = "progress.downloading_dxvk",
                Percentage = 10
            };

            await foreach (var progress in DownloadFileAsync(DxvkDownloadUrl, archivePath, cancellationToken))
            {
                yield return progress;
            }

            // Extract
            yield return new DownloadProgressEvent
            {
                Stage = "extracting",
                MessageKey = "progress.extracting_dxvk",
                Percentage = 80
            };

            await ExtractArchiveAsync(archivePath, DxvkBaseDirectory, cancellationToken);

            // Verify
            if (!IsInstalled())
            {
                yield return new DownloadProgressEvent
                {
                    Stage = "error",
                    MessageKey = "error.dxvk_download_failed",
                    HasError = true,
                    ErrorMessage = $"DXVK DLLs not found after extraction at {DxvkDllDirectory}"
                };
                yield break;
            }

            logger?.LogInformation("[DXVK] DXVK {Version} installed successfully", DxvkReleaseName);

            yield return new DownloadProgressEvent
            {
                Stage = "complete",
                MessageKey = "progress.dxvk_ready",
                Percentage = 100,
                IsComplete = true
            };
        }
        finally
        {
            // Clean up temp dir
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[DXVK] Failed to clean up temp directory");
            }
        }
    }

    /// <summary>
    /// Download DXVK if not already present (synchronous wrapper).
    /// </summary>
    public void EnsureDxvk()
    {
        if (IsInstalled()) return;
        Task.Run(async () =>
        {
            await foreach (var _ in EnsureDxvkAsync()) { }
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Install DXVK DLLs into the Wine prefix system32 directory
    /// </summary>
    public void InstallToPrefix(string winePrefixPath)
    {
        var system32Path = Path.Combine(winePrefixPath, "drive_c", "windows", "system32");
        Directory.CreateDirectory(system32Path);

        if (!Directory.Exists(DxvkDllDirectory))
        {
            logger?.LogWarning("[DXVK] DXVK DLL directory not found: {Path}", DxvkDllDirectory);
            return;
        }

        var files = Directory.GetFiles(DxvkDllDirectory, "*.dll");
        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var destPath = Path.Combine(system32Path, fileName);
            File.Copy(filePath, destPath, overwrite: true);
            logger?.LogDebug("[DXVK] Installed {Dll} to system32", fileName);
        }

        logger?.LogInformation("[DXVK] Installed {Count} DLLs to {Path}", files.Length, system32Path);
    }

    private async IAsyncEnumerable<DownloadProgressEvent> DownloadFileAsync(
        string url,
        string destinationPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var fileName = Path.GetFileName(url);

        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        var lastReportTime = DateTime.UtcNow;
        long lastReportedBytes = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;

            var now = DateTime.UtcNow;
            if (totalBytes > 0 && (now - lastReportTime).TotalMilliseconds >= 500)
            {
                var elapsedSeconds = (now - lastReportTime).TotalSeconds;
                var bytesDownloadedSinceLastReport = totalRead - lastReportedBytes;
                var downloadSpeed = elapsedSeconds > 0 ? bytesDownloadedSinceLastReport / elapsedSeconds : 0;
                var percentage = (int)(10 + (totalRead * 70.0 / totalBytes));

                yield return new DownloadProgressEvent
                {
                    Stage = "downloading",
                    MessageKey = "progress.downloading_dxvk",
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

    private async Task ExtractArchiveAsync(string archivePath, string destinationDir, CancellationToken ct)
    {
        logger?.LogInformation("[DXVK] Extracting: {Path}", archivePath);

        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"xzf \"{archivePath}\" -C \"{destinationDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start tar process");

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new Exception($"tar extraction failed: {error}");
        }
    }
}
