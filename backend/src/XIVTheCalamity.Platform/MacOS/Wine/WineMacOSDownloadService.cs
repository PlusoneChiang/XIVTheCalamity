using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.MacOS.Wine;

/// <summary>
/// Wine download service for macOS
/// Downloads Wine from PlusoneChiang/winecx GitHub releases
/// </summary>
public class WineMacOSDownloadService(
    ILogger<WineMacOSDownloadService>? logger = null)
{
    private readonly PlatformPathService _platformPaths = PlatformPathService.Instance;
    private readonly HttpClient _httpClient = new();
    
    private string WineRoot => Path.Combine(_platformPaths.AppDataDirectory, "wine");
    private string WinePath => Path.Combine(WineRoot, "bin", "wine64");
    private string VersionFilePath => Path.Combine(WineRoot, ".wine-version");
    
    private const string WineVersion = "v2026.03.30";
    private const string GithubRepo = "PlusoneChiang/winecx";
    private const string ArchiveName = $"wine-macos-x86_64-{WineVersion}.tar.xz";
    private const string DownloadUrl = $"https://github.com/{GithubRepo}/releases/download/{WineVersion}/{ArchiveName}";
    
    /// <summary>
    /// Check if Wine is installed and version matches.
    /// Returns false if binary is missing OR version file doesn't match expected version.
    /// </summary>
    public bool IsInstalled()
    {
        var binaryExists = Directory.Exists(WineRoot) && File.Exists(WinePath);
        if (!binaryExists)
        {
            logger?.LogDebug("[WINE-DL] Wine not installed, path: {Path}", WineRoot);
            return false;
        }
        
        var versionMatch = IsVersionCurrent();
        if (!versionMatch)
        {
            logger?.LogInformation("[WINE-DL] Wine version mismatch, update required. Expected: {Expected}, installed: {Installed}",
                WineVersion, GetInstalledVersion() ?? "(no version file)");
            return false;
        }
        
        logger?.LogDebug("[WINE-DL] Wine installed and up-to-date: {Version}", WineVersion);
        return true;
    }
    
    /// <summary>
    /// Check if installed Wine version matches expected version
    /// </summary>
    private bool IsVersionCurrent()
    {
        if (!File.Exists(VersionFilePath))
            return false;
        
        try
        {
            var installedVersion = File.ReadAllText(VersionFilePath).Trim();
            return installedVersion == WineVersion;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[WINE-DL] Failed to read version file");
            return false;
        }
    }
    
    /// <summary>
    /// Get currently installed Wine version from version file
    /// </summary>
    private string? GetInstalledVersion()
    {
        try
        {
            return File.Exists(VersionFilePath) ? File.ReadAllText(VersionFilePath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Write version file after successful installation
    /// </summary>
    private void WriteVersionFile()
    {
        try
        {
            File.WriteAllText(VersionFilePath, WineVersion);
            logger?.LogInformation("[WINE-DL] Version file written: {Version}", WineVersion);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[WINE-DL] Failed to write version file");
        }
    }
    
    /// <summary>
    /// Get Wine installation root path
    /// </summary>
    public string GetWineRoot() => WineRoot;
    
    /// <summary>
    /// Download and install Wine with progress streaming
    /// </summary>
    public async IAsyncEnumerable<DownloadProgressEvent> DownloadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WINE-DL] Starting Wine download for macOS");
        logger?.LogInformation("[WINE-DL] Version: {Version}, URL: {Url}", WineVersion, DownloadUrl);
        
        // Step 1: Download tar.xz
        var parentDir = Path.GetDirectoryName(WineRoot)!;
        Directory.CreateDirectory(parentDir);
        
        var tempDir = Path.Combine(parentDir, $"wine-temp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        
        var archivePath = Path.Combine(tempDir, ArchiveName);
        
        yield return new DownloadProgressEvent
        {
            Stage = "downloading",
            MessageKey = "progress.downloading_wine",
            CurrentFile = ArchiveName,
            Percentage = 5
        };
        
        // Download with progress
        await foreach (var progress in DownloadFileAsync(DownloadUrl, archivePath, cancellationToken))
        {
            yield return progress;
        }
        
        // Step 2: Extract archive
        yield return new DownloadProgressEvent
        {
            Stage = "extracting",
            MessageKey = "progress.extracting_wine",
            Percentage = 70
        };
        
        string? extractionError = null;
        try
        {
            await ExtractArchiveAsync(archivePath, tempDir, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WINE-DL] Extraction failed");
            extractionError = ex.Message;
        }
        
        if (extractionError != null)
        {
            yield return new DownloadProgressEvent
            {
                Stage = "error",
                MessageKey = "error.wine_download_failed",
                HasError = true,
                ErrorMessage = $"Extraction failed: {extractionError}"
            };
            CleanupTempDir(tempDir);
            yield break;
        }
        
        // Step 3: Install - move to final location
        yield return new DownloadProgressEvent
        {
            Stage = "installing",
            MessageKey = "progress.installing_wine",
            Percentage = 85
        };
        
        // The archive extracts to a "wine" directory
        var extractedDir = Path.Combine(tempDir, "wine");
        if (!Directory.Exists(extractedDir))
        {
            // Fallback: look for any directory
            var dirs = Directory.GetDirectories(tempDir);
            extractedDir = dirs.Length > 0 ? dirs[0] : null;
        }
        
        if (extractedDir == null || !Directory.Exists(extractedDir))
        {
            yield return new DownloadProgressEvent
            {
                Stage = "error",
                MessageKey = "error.wine_download_failed",
                HasError = true,
                ErrorMessage = "No extracted Wine directory found"
            };
            CleanupTempDir(tempDir);
            yield break;
        }
        
        // Remove old installation and wineprefix (prefix must be recreated for new Wine version)
        if (Directory.Exists(WineRoot))
        {
            logger?.LogInformation("[WINE-DL] Removing old installation: {Path}", WineRoot);
            Directory.Delete(WineRoot, true);
        }
        
        var winePrefixPath = _platformPaths.GetWinePrefixPath();
        if (Directory.Exists(winePrefixPath))
        {
            logger?.LogInformation("[WINE-DL] Removing old wineprefix for version upgrade: {Path}", winePrefixPath);
            Directory.Delete(winePrefixPath, true);
        }
        
        Directory.Move(extractedDir, WineRoot);
        logger?.LogInformation("[WINE-DL] Wine moved to: {Path}", WineRoot);
        
        // Step 4: Remove quarantine attribute (macOS Gatekeeper)
        yield return new DownloadProgressEvent
        {
            Stage = "configuring",
            MessageKey = "progress.configuring_wine",
            Percentage = 92
        };
        
        await RemoveQuarantineAsync(WineRoot, cancellationToken);
        
        // Cleanup temp files
        CleanupTempDir(tempDir);
        
        // Verify installation
        if (!File.Exists(WinePath))
        {
            yield return new DownloadProgressEvent
            {
                Stage = "error",
                MessageKey = "error.wine_download_failed",
                HasError = true,
                ErrorMessage = "Wine executable not found after installation"
            };
            yield break;
        }
        
        // Write version file for future version checks
        WriteVersionFile();
        
        logger?.LogInformation("[WINE-DL] Wine {Version} installed successfully at {Path}", WineVersion, WineRoot);
        
        yield return new DownloadProgressEvent
        {
            Stage = "complete",
            MessageKey = "progress.wine_downloaded",
            Percentage = 100,
            IsComplete = true
        };
    }
    
    /// <summary>
    /// Download file with progress reporting
    /// </summary>
    private async IAsyncEnumerable<DownloadProgressEvent> DownloadFileAsync(
        string url,
        string destinationPath,
        [EnumeratorCancellation] CancellationToken ct = default)
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
        var startTime = DateTime.UtcNow;
        var lastReportTime = startTime;
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
                
                var percentage = 5 + (int)(totalRead * 65.0 / totalBytes);
                
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
        
        // Final download report
        if (totalBytes > 0)
        {
            var totalElapsedSeconds = (DateTime.UtcNow - startTime).TotalSeconds;
            var avgSpeed = totalElapsedSeconds > 0 ? totalRead / totalElapsedSeconds : 0;
            
            yield return new DownloadProgressEvent
            {
                Stage = "downloading",
                MessageKey = "progress.downloading_wine",
                BytesDownloaded = totalRead,
                TotalBytes = totalBytes,
                Percentage = 70,
                CurrentFile = fileName,
                DownloadSpeedBytesPerSec = avgSpeed
            };
        }
    }
    
    /// <summary>
    /// Extract tar.xz archive
    /// </summary>
    private async Task ExtractArchiveAsync(string archivePath, string destinationDir, CancellationToken ct)
    {
        logger?.LogInformation("[WINE-DL] Extracting: {Path}", archivePath);
        
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"xJf \"{archivePath}\" -C \"{destinationDir}\"",
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
            throw new Exception($"tar extraction failed (exit {process.ExitCode}): {error}");
        }
    }
    
    /// <summary>
    /// Remove quarantine extended attribute from Wine binaries
    /// Required for macOS Gatekeeper to allow unsigned binaries in user directories
    /// </summary>
    private async Task RemoveQuarantineAsync(string wineRoot, CancellationToken ct)
    {
        logger?.LogInformation("[WINE-DL] Removing quarantine attribute from Wine binaries");
        
        var psi = new ProcessStartInfo
        {
            FileName = "xattr",
            Arguments = $"-cr \"{wineRoot}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        
        using var process = Process.Start(psi);
        if (process != null)
        {
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ct);
                logger?.LogWarning("[WINE-DL] xattr -cr returned exit code {Code}: {Error}", process.ExitCode, error);
            }
            else
            {
                logger?.LogInformation("[WINE-DL] Quarantine attribute removed successfully");
            }
        }
    }
    
    private void CleanupTempDir(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[WINE-DL] Failed to clean up temp directory");
        }
    }
}


