using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.Linux.Proton;

/// <summary>
/// Proton download service
/// Downloads and extracts Proton GE from GitHub releases
/// </summary>
public class ProtonDownloadService(
    HttpClient httpClient, 
    ILogger<ProtonDownloadService>? logger = null)
{
    private readonly PlatformPathService _platformPaths = PlatformPathService.Instance;
    
    private const string PROTON_VERSION = "GE-Proton10-29";
    private const string PROTON_DOWNLOAD_URL = 
        "https://github.com/GloriousEggroll/proton-ge-custom/releases/download/GE-Proton10-29/GE-Proton10-29.tar.gz";
    
    /// <summary>
    /// Check if Proton is already installed
    /// </summary>
    public async Task<ProtonStatus> GetStatusAsync(string version = PROTON_VERSION)
    {
        logger?.LogDebug("[PROTON-DL] Checking Proton status for version: {Version}", version);
        
        try
        {
            var protonDir = GetProtonDirectory(version);
            var wineBinary = Path.Combine(protonDir, "files", "bin", "wine");
            
            logger?.LogDebug("[PROTON-DL] Proton directory: {ProtonDir}", protonDir);
            logger?.LogDebug("[PROTON-DL] Wine binary path: {WineBinary}", wineBinary);
            
            var exists = File.Exists(wineBinary);
            logger?.LogInformation("[PROTON-DL] Proton {Version} installed: {Exists}", version, exists);
            
            return new ProtonStatus
            {
                IsInstalled = exists,
                Version = version,
                InstallPath = exists ? protonDir : null
            };
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[PROTON-DL] Failed to check Proton status");
            return new ProtonStatus
            {
                IsInstalled = false,
                Version = version,
                InstallPath = null
            };
        }
    }
    
    /// <summary>
    /// Download and install Proton with progress reporting using async enumerable
    /// </summary>
    public async IAsyncEnumerable<DownloadProgress> DownloadAsyncEnumerable(
        string version = PROTON_VERSION,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[PROTON-DL] ========== Starting Proton Download ==========");
        logger?.LogInformation("[PROTON-DL] Version: {Version}", version);
        logger?.LogInformation("[PROTON-DL] URL: {Url}", PROTON_DOWNLOAD_URL);
        
        var downloadDir = GetDownloadDirectory();
        logger?.LogDebug("[PROTON-DL] Download directory: {DownloadDir}", downloadDir);
        
        // Create download directory
        Directory.CreateDirectory(downloadDir);
        logger?.LogDebug("[PROTON-DL] Download directory created/verified");
        
        var archivePath = Path.Combine(downloadDir, $"{version}.tar.gz");
        logger?.LogInformation("[PROTON-DL] Archive path: {ArchivePath}", archivePath);
        
        // Delete existing archive if present
        if (File.Exists(archivePath))
        {
            logger?.LogWarning("[PROTON-DL] Existing archive found, deleting: {ArchivePath}", archivePath);
            File.Delete(archivePath);
        }
        
        // Start download
        logger?.LogInformation("[PROTON-DL] Starting HTTP download...");
        yield return new DownloadProgress
        {
            Stage = "download",
            MessageKey = "progress.downloading",
            DownloadedBytes = 0,
            TotalBytes = 0,
            Percent = 0
        };
        
        using var response = await httpClient.GetAsync(
            PROTON_DOWNLOAD_URL, 
            HttpCompletionOption.ResponseHeadersRead, 
            cancellationToken);
        
        logger?.LogDebug("[PROTON-DL] HTTP response status: {StatusCode}", response.StatusCode);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        logger?.LogInformation("[PROTON-DL] Total download size: {Size} bytes ({SizeMB:F2} MB)", 
            totalBytes, totalBytes / 1024.0 / 1024.0);
        
        var downloadedBytes = 0L;
        var lastLoggedPercent = 0;
        
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        
        var buffer = new byte[8192];
        int bytesRead;
        var stopwatch = Stopwatch.StartNew();
        
        logger?.LogDebug("[PROTON-DL] Starting download loop...");
        
        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;
            
            var percent = totalBytes > 0 ? (int)((downloadedBytes * 100) / totalBytes) : 0;
            
            // Log and yield every 10%
            if (percent >= lastLoggedPercent + 10 || percent == 100)
            {
                var speed = downloadedBytes / stopwatch.Elapsed.TotalSeconds;
                logger?.LogInformation("[PROTON-DL] Download progress: {Percent}% ({Downloaded}/{Total} MB, {Speed:F2} MB/s)",
                    percent,
                    downloadedBytes / 1024.0 / 1024.0,
                    totalBytes / 1024.0 / 1024.0,
                    speed / 1024.0 / 1024.0);
                lastLoggedPercent = percent;
                
                yield return new DownloadProgress
                {
                    Stage = "download",
                    MessageKey = "progress.downloading",
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = totalBytes,
                    Percent = percent
                };
            }
        }
        
        stopwatch.Stop();
        logger?.LogInformation("[PROTON-DL] Download complete: {Path} ({Size:F2} MB in {Time:F1}s)",
            archivePath, downloadedBytes / 1024.0 / 1024.0, stopwatch.Elapsed.TotalSeconds);
        
        // Extract
        logger?.LogInformation("[PROTON-DL] Starting extraction...");
        yield return new DownloadProgress
        {
            Stage = "extract",
            MessageKey = "progress.extracting",
            DownloadedBytes = downloadedBytes,
            TotalBytes = totalBytes,
            Percent = 100
        };
        
        await ExtractAsync(archivePath, downloadDir, cancellationToken);
        logger?.LogInformation("[PROTON-DL] Extraction complete");
        
        // Delete archive
        logger?.LogDebug("[PROTON-DL] Deleting archive: {ArchivePath}", archivePath);
        File.Delete(archivePath);
        
        // Verify installation
        var status = await GetStatusAsync(version);
        if (!status.IsInstalled)
        {
            throw new Exception("Proton installation verification failed");
        }
        
        logger?.LogInformation("[PROTON-DL] ========== Proton {Version} installed successfully ==========", version);
    }
    
    /// <summary>
    /// Download and install Proton
    /// </summary>
    public async Task DownloadAsync(
        string version = PROTON_VERSION, 
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[PROTON-DL] ========== Starting Proton Download ==========");
        logger?.LogInformation("[PROTON-DL] Version: {Version}", version);
        logger?.LogInformation("[PROTON-DL] URL: {Url}", PROTON_DOWNLOAD_URL);
        
        var downloadDir = GetDownloadDirectory();
        logger?.LogDebug("[PROTON-DL] Download directory: {DownloadDir}", downloadDir);
        
        try
        {
            // Create download directory
            Directory.CreateDirectory(downloadDir);
            logger?.LogDebug("[PROTON-DL] Download directory created/verified");
            
            var archivePath = Path.Combine(downloadDir, $"{version}.tar.gz");
            logger?.LogInformation("[PROTON-DL] Archive path: {ArchivePath}", archivePath);
            
            // Delete existing archive if present
            if (File.Exists(archivePath))
            {
                logger?.LogWarning("[PROTON-DL] Existing archive found, deleting: {ArchivePath}", archivePath);
                File.Delete(archivePath);
            }
            
            // Start download
            logger?.LogInformation("[PROTON-DL] Starting HTTP download...");
            progress?.Report(new DownloadProgress
            {
                Stage = "download",
                MessageKey = "progress.downloading",
                DownloadedBytes = 0,
                TotalBytes = 0,
                Percent = 0
            });
            
            using var response = await httpClient.GetAsync(
                PROTON_DOWNLOAD_URL, 
                HttpCompletionOption.ResponseHeadersRead, 
                cancellationToken);
            
            logger?.LogDebug("[PROTON-DL] HTTP response status: {StatusCode}", response.StatusCode);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            logger?.LogInformation("[PROTON-DL] Total download size: {Size} bytes ({SizeMB:F2} MB)", 
                totalBytes, totalBytes / 1024.0 / 1024.0);
            
            var downloadedBytes = 0L;
            var lastLoggedPercent = 0;
            
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            
            var buffer = new byte[8192];
            int bytesRead;
            var stopwatch = Stopwatch.StartNew();
            
            logger?.LogDebug("[PROTON-DL] Starting download loop...");
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;
                
                var percent = totalBytes > 0 ? (int)((downloadedBytes * 100) / totalBytes) : 0;
                
                // Log every 10%
                if (percent >= lastLoggedPercent + 10)
                {
                    var speed = downloadedBytes / stopwatch.Elapsed.TotalSeconds;
                    logger?.LogInformation("[PROTON-DL] Download progress: {Percent}% ({Downloaded}/{Total} MB, {Speed:F2} MB/s)",
                        percent,
                        downloadedBytes / 1024.0 / 1024.0,
                        totalBytes / 1024.0 / 1024.0,
                        speed / 1024.0 / 1024.0);
                    lastLoggedPercent = percent;
                }
                
                progress?.Report(new DownloadProgress
                {
                    Stage = "download",
                    MessageKey = "progress.downloading",
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = totalBytes,
                    Percent = percent
                });
            }
            
            stopwatch.Stop();
            logger?.LogInformation("[PROTON-DL] Download complete: {Path} ({Size:F2} MB in {Time:F1}s)",
                archivePath, downloadedBytes / 1024.0 / 1024.0, stopwatch.Elapsed.TotalSeconds);
            
            // Extract
            logger?.LogInformation("[PROTON-DL] Starting extraction...");
            progress?.Report(new DownloadProgress
            {
                Stage = "extract",
                MessageKey = "progress.extracting",
                Percent = 100
            });
            
            await ExtractAsync(archivePath, downloadDir, cancellationToken);
            logger?.LogInformation("[PROTON-DL] Extraction complete");
            
            // Delete archive
            logger?.LogDebug("[PROTON-DL] Deleting archive: {ArchivePath}", archivePath);
            File.Delete(archivePath);
            
            // Verify installation
            var status = await GetStatusAsync(version);
            if (!status.IsInstalled)
            {
                throw new Exception("Proton installation verification failed");
            }
            
            logger?.LogInformation("[PROTON-DL] ========== Proton {Version} installed successfully ==========", version);
            
            progress?.Report(new DownloadProgress
            {
                Stage = "complete",
                MessageKey = "progress.complete",
                Percent = 100,
                IsComplete = true
            });
        }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("[PROTON-DL] Download cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[PROTON-DL] ========== Download failed ==========");
            progress?.Report(new DownloadProgress
            {
                Stage = "error",
                MessageKey = "error.download_failed",
                HasError = true,
                ErrorMessage = ex.Message
            });
            throw;
        }
    }
    
    /// <summary>
    /// Extract Proton archive using tar
    /// </summary>
    private async Task ExtractAsync(string archivePath, string targetDir, CancellationToken cancellationToken)
    {
        logger?.LogDebug("[PROTON-DL] Extracting archive: {Archive} -> {Target}", archivePath, targetDir);
        
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            ArgumentList = { "-xzf", archivePath, "-C", targetDir },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        
        logger?.LogDebug("[PROTON-DL] Running command: tar -xzf {Archive} -C {Target}", archivePath, targetDir);
        
        using var process = new Process { StartInfo = psi };
        process.Start();
        
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        
        await process.WaitForExitAsync(cancellationToken);
        
        var stdout = await outputTask;
        var stderr = await errorTask;
        
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            logger?.LogDebug("[PROTON-DL] tar stdout: {Output}", stdout);
        }
        
        if (process.ExitCode != 0)
        {
            logger?.LogError("[PROTON-DL] tar extraction failed with exit code {ExitCode}", process.ExitCode);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                logger?.LogError("[PROTON-DL] tar stderr: {Error}", stderr);
            }
            throw new Exception($"Failed to extract Proton: {stderr}");
        }
        
        logger?.LogDebug("[PROTON-DL] Extraction completed successfully");
    }
    
    /// <summary>
    /// Get download directory path
    /// </summary>
    private string GetDownloadDirectory()
    {
        var dir = Path.Combine(_platformPaths.UserDataDirectory, "proton-ge");
        logger?.LogDebug("[PROTON-DL] Download directory: {Dir}", dir);
        return dir;
    }
    
    /// <summary>
    /// Get Proton installation directory path
    /// </summary>
    private string GetProtonDirectory(string version)
    {
        var dir = Path.Combine(GetDownloadDirectory(), version);
        logger?.LogDebug("[PROTON-DL] Proton directory for {Version}: {Dir}", version, dir);
        return dir;
    }
}

/// <summary>
/// Proton installation status
/// </summary>
public class ProtonStatus
{
    public bool IsInstalled { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? InstallPath { get; set; }
}

/// <summary>
/// Download progress information
/// </summary>
public class DownloadProgress
{
    public string Stage { get; set; } = string.Empty;
    public string MessageKey { get; set; } = string.Empty;
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public int Percent { get; set; }
    public bool IsComplete { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
}
