using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.Linux.Wine;

/// <summary>
/// Wine-XIV download service for Linux
/// Downloads and manages wine-xiv-git from goatcorp/wine-xiv-git
/// </summary>
public class WineXIVDownloadService(
    ILogger<WineXIVDownloadService>? logger = null)
{
    private readonly PlatformPathService _platformPaths = PlatformPathService.Instance;
    private readonly HttpClient _httpClient = new();
    
    // Wine-XIV paths  
    private string WineRoot => _platformPaths.GetEmulatorRootDirectory();
    private string WinePath => Path.Combine(WineRoot, "bin", "wine64");
    
    // Fixed Wine-XIV version
    private const string WineXIVVersion = "10.8.r0.g47f77594";
    private const string GithubRepo = "goatcorp/wine-xiv-git";
    private const string DownloadUrlTemplate = $"https://github.com/{GithubRepo}/releases/download/{WineXIVVersion}/wine-xiv-staging-fsync-git-{{0}}-{WineXIVVersion}.tar.xz";
    
    // Supported distros
    private static readonly HashSet<string> SupportedDistros = new() { "fedora", "ubuntu", "arch" };
    
    /// <summary>
    /// Get Wine-XIV installation status
    /// </summary>
    public async Task<DownloadStatus> GetStatusAsync()
    {
        if (Directory.Exists(WineRoot) && File.Exists(WinePath))
        {
            logger?.LogDebug("[WINE-XIV] Wine-XIV is installed at: {Path}", WineRoot);
            return new DownloadStatus
            {
                IsInstalled = true,
                Version = GetInstalledVersion(),
                InstalledPath = WineRoot
            };
        }
        
        logger?.LogDebug("[WINE-XIV] Wine-XIV is not installed");
        return new DownloadStatus
        {
            IsInstalled = false
        };
    }
    
    /// <summary>
    /// Download and install Wine-XIV with progress streaming
    /// </summary>
    public async IAsyncEnumerable<DownloadProgress> DownloadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WINE-XIV] Starting Wine-XIV download");
        
        // Step 1: Detect distro
        yield return new DownloadProgress
        {
            Stage = "detecting_distro",
            MessageKey = "progress.detecting_distro",
            Percentage = 5
        };
        
        var distro = DetectDistro();
        if (string.IsNullOrEmpty(distro))
        {
            yield return new DownloadProgress
            {
                Stage = "error",
                MessageKey = "error.wine_download_failed",
                HasError = true,
                ErrorMessage = "Unsupported Linux distribution. Supported: Fedora, Ubuntu, Arch Linux"
            };
            yield break;
        }
        
        var downloadUrl = string.Format(DownloadUrlTemplate, distro);
        
        logger?.LogInformation("[WINE-XIV] Version: {Version}", WineXIVVersion);
        logger?.LogInformation("[WINE-XIV] Distro: {Distro}", distro);
        logger?.LogInformation("[WINE-XIV] Download URL: {Url}", downloadUrl);
        
        // Step 2: Download tar.xz file to same filesystem as target
        var parentDir = Path.GetDirectoryName(WineRoot)!;
        Directory.CreateDirectory(parentDir);
        
        var tempDir = Path.Combine(parentDir, $"wine-xiv-temp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        
        var archivePath = Path.Combine(tempDir, "wine-xiv.tar.xz");
        
        yield return new DownloadProgress
        {
            Stage = "downloading",
            MessageKey = "progress.downloading_wine",
            CurrentFile = "wine-xiv.tar.xz",
            Percentage = 10
        };
        
        // Download file and forward all progress events
        await foreach (var progress in DownloadFileAsync(downloadUrl, archivePath, cancellationToken))
        {
            yield return progress;
        }
        
        // Step 3: Extract archive
        yield return new DownloadProgress
        {
            Stage = "extracting",
            MessageKey = "progress.extracting_wine",
            Percentage = 70
        };
        
        await ExtractArchiveAsync(archivePath, tempDir, cancellationToken);
        
        // Step 4: Move to installation directory (same filesystem, no copy needed)
        yield return new DownloadProgress
        {
            Stage = "installing",
            MessageKey = "progress.installing_wine",
            Percentage = 90
        };
        
        // Find extracted directory (wine-xiv-staging-fsync-git-{distro}-{version})
        var extractedDirs = Directory.GetDirectories(tempDir, "wine-xiv-*");
        if (extractedDirs.Length == 0)
        {
            yield return new DownloadProgress
            {
                Stage = "error",
                MessageKey = "error.wine_download_failed",
                HasError = true,
                ErrorMessage = "No extracted Wine-XIV directory found"
            };
            yield break;
        }
        
        var extractedDir = extractedDirs[0];
        logger?.LogInformation("[WINE-XIV] Extracted to: {Path}", extractedDir);
        
        // Remove old installation if exists
        if (Directory.Exists(WineRoot))
        {
            logger?.LogInformation("[WINE-XIV] Removing old installation: {Path}", WineRoot);
            Directory.Delete(WineRoot, true);
        }
        
        // Move to final location (same filesystem, use Move instead of Copy)
        logger?.LogInformation("[WINE-XIV] Moving {Source} -> {Dest}", extractedDir, WineRoot);
        Directory.Move(extractedDir, WineRoot);
        
        // Clean up temp files
        try
        {
            logger?.LogDebug("[WINE-XIV] Cleaning up temp directory: {Path}", tempDir);
            Directory.Delete(tempDir, true);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[WINE-XIV] Failed to clean up temp directory");
        }
        
        // Verify installation
        if (!File.Exists(WinePath))
        {
            yield return new DownloadProgress
            {
                Stage = "error",
                MessageKey = "error.wine_download_failed",
                HasError = true,
                ErrorMessage = "Wine executable not found after installation"
            };
            yield break;
        }
        
        logger?.LogInformation("[WINE-XIV] Wine-XIV {Version} installed successfully", WineXIVVersion);
        
        yield return new DownloadProgress
        {
            Stage = "complete",
            MessageKey = "progress.wine_downloaded",
            Percentage = 100,
            IsComplete = true
        };
    }
    
    /// <summary>
    /// Download file with progress reporting via IAsyncEnumerable
    /// </summary>
    private async IAsyncEnumerable<DownloadProgress> DownloadFileAsync(
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
        var startTime = DateTime.UtcNow;
        var lastReportTime = startTime;
        long lastReportedBytes = 0;
        
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            
            // Report progress every 500ms to avoid too frequent updates
            var now = DateTime.UtcNow;
            if (totalBytes > 0 && (now - lastReportTime).TotalMilliseconds >= 500)
            {
                var elapsedSeconds = (now - lastReportTime).TotalSeconds;
                var bytesDownloadedSinceLastReport = totalRead - lastReportedBytes;
                var downloadSpeed = elapsedSeconds > 0 ? bytesDownloadedSinceLastReport / elapsedSeconds : 0;
                
                var percentage = (int)(10 + (totalRead * 60.0 / totalBytes));
                var downloadedMB = totalRead / (1024.0 * 1024.0);
                var totalMB = totalBytes / (1024.0 * 1024.0);
                var speedMBps = downloadSpeed / (1024.0 * 1024.0);
                
                yield return new DownloadProgress
                {
                    Stage = "downloading",
                    MessageKey = "progress.downloading_wine",
                    BytesDownloaded = totalRead,
                    TotalBytes = totalBytes,
                    Percentage = percentage,
                    CurrentFile = fileName,
                    DownloadedMB = downloadedMB,
                    TotalMB = totalMB,
                    DownloadSpeedMBps = speedMBps
                };
                
                lastReportTime = now;
                lastReportedBytes = totalRead;
            }
        }
        
        // Final report
        if (totalBytes > 0)
        {
            var totalElapsedSeconds = (DateTime.UtcNow - startTime).TotalSeconds;
            var avgSpeed = totalElapsedSeconds > 0 ? totalRead / totalElapsedSeconds : 0;
            var downloadedMB = totalRead / (1024.0 * 1024.0);
            var totalMB = totalBytes / (1024.0 * 1024.0);
            var speedMBps = avgSpeed / (1024.0 * 1024.0);
            
            yield return new DownloadProgress
            {
                Stage = "downloading",
                MessageKey = "progress.downloading_wine",
                BytesDownloaded = totalRead,
                TotalBytes = totalBytes,
                Percentage = 70, // Ready for extraction
                CurrentFile = fileName,
                DownloadedMB = downloadedMB,
                TotalMB = totalMB,
                DownloadSpeedMBps = speedMBps
            };
        }
    }
    
    /// <summary>
    /// Extract tar.xz archive
    /// </summary>
    private async Task ExtractArchiveAsync(string archivePath, string destinationDir, CancellationToken ct)
    {
        logger?.LogInformation("[WINE-XIV] Extracting archive: {Path}", archivePath);
        
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
        {
            throw new Exception("Failed to start tar process");
        }
        
        await process.WaitForExitAsync(ct);
        
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new Exception($"tar extraction failed: {error}");
        }
    }
    
    /// <summary>
    /// Detect Linux distro for Wine-XIV variant selection
    /// Returns null if distro is not supported
    /// </summary>
    private string? DetectDistro()
    {
        try
        {
            var osRelease = File.ReadAllText("/etc/os-release");
            
            // Check ID_LIKE first (for derivatives like Bazzite/SteamOS)
            var idLikeMatch = System.Text.RegularExpressions.Regex.Match(osRelease, "ID_LIKE=\"?([^\"\n]+)\"?");
            if (idLikeMatch.Success)
            {
                var idLike = idLikeMatch.Groups[1].Value.ToLower();
                if (idLike.Contains("fedora")) return "fedora";
                if (idLike.Contains("ubuntu") || idLike.Contains("debian")) return "ubuntu";
                if (idLike.Contains("arch")) return "arch";
            }
            
            // Fallback to ID
            var idMatch = System.Text.RegularExpressions.Regex.Match(osRelease, "ID=\"?([^\"\n]+)\"?");
            if (idMatch.Success)
            {
                var id = idMatch.Groups[1].Value.ToLower();
                if (id == "fedora" || id == "rhel" || id == "centos" || id == "bazzite") return "fedora";
                if (id == "ubuntu" || id == "debian") return "ubuntu";
                if (id == "arch" || id == "manjaro" || id == "steamos") return "arch";
            }
            
            // Unsupported distro
            logger?.LogError("[WINE-XIV] Unsupported Linux distribution. Supported: Fedora/RHEL/CentOS, Ubuntu/Debian, Arch/Manjaro");
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WINE-XIV] Failed to detect distro");
            return null;
        }
    }
    
    /// <summary>
    /// Get installed Wine-XIV version
    /// </summary>
    private string GetInstalledVersion()
    {
        return WineXIVVersion;
    }
}

/// <summary>
/// Wine-XIV installation status
/// </summary>
public class DownloadStatus
{
    public bool IsInstalled { get; set; }
    public string? Version { get; set; }
    public string? InstalledPath { get; set; }
}

/// <summary>
/// Download progress information
/// </summary>
public class DownloadProgress
{
    public string Stage { get; set; } = string.Empty;
    public string MessageKey { get; set; } = string.Empty;
    public string? CurrentFile { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage { get; set; }
    
    // New fields for detailed progress display
    public double DownloadedMB { get; set; }
    public double TotalMB { get; set; }
    public double DownloadSpeedMBps { get; set; }
    
    public bool IsComplete { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
}
