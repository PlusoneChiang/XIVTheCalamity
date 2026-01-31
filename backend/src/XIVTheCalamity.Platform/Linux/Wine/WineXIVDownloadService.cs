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
    /// Download and install Wine-XIV
    /// </summary>
    public async Task<bool> DownloadAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger?.LogInformation("[WINE-XIV] Starting Wine-XIV download");
            
            // Step 1: Detect distro
            progress?.Report(new DownloadProgress
            {
                Stage = "detecting_distro",
                MessageKey = "progress.detecting_distro",
                Percentage = 5
            });
            
            var distro = DetectDistro();
            if (string.IsNullOrEmpty(distro))
            {
                throw new Exception("Unsupported Linux distribution. Supported: Fedora, Ubuntu, Arch Linux");
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
            
            progress?.Report(new DownloadProgress
            {
                Stage = "downloading",
                MessageKey = "progress.downloading_wine",
                CurrentFile = "wine-xiv.tar.xz",
                Percentage = 10
            });
            
            await DownloadFileAsync(downloadUrl, archivePath, progress, cancellationToken);
            
            // Step 3: Extract archive
            progress?.Report(new DownloadProgress
            {
                Stage = "extracting",
                MessageKey = "progress.extracting_wine",
                Percentage = 70
            });
            
            await ExtractArchiveAsync(archivePath, tempDir, cancellationToken);
            
            // Step 4: Move to installation directory (same filesystem, no copy needed)
            progress?.Report(new DownloadProgress
            {
                Stage = "installing",
                MessageKey = "progress.installing_wine",
                Percentage = 90
            });
            
            // Find extracted directory (wine-xiv-staging-fsync-git-{distro}-{version})
            var extractedDirs = Directory.GetDirectories(tempDir, "wine-xiv-*");
            if (extractedDirs.Length == 0)
            {
                throw new Exception("No extracted Wine-XIV directory found");
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
                throw new Exception("Wine executable not found after installation");
            }
            
            logger?.LogInformation("[WINE-XIV] Wine-XIV {Version} installed successfully", WineXIVVersion);
            
            progress?.Report(new DownloadProgress
            {
                Stage = "complete",
                MessageKey = "progress.wine_downloaded",
                Percentage = 100,
                IsComplete = true
            });
            
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WINE-XIV] Download failed: {Message}", ex.Message);
            
            progress?.Report(new DownloadProgress
            {
                Stage = "error",
                MessageKey = "error.wine_download_failed",
                HasError = true,
                ErrorMessage = ex.Message
            });
            
            return false;
        }
    }
    
    /// <summary>
    /// Download file with progress reporting
    /// </summary>
    private async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        
        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            
            if (totalBytes > 0)
            {
                var percentage = (int)(10 + (totalRead * 60.0 / totalBytes));
                progress?.Report(new DownloadProgress
                {
                    Stage = "downloading",
                    MessageKey = "progress.downloading_wine",
                    BytesDownloaded = totalRead,
                    TotalBytes = totalBytes,
                    Percentage = percentage
                });
            }
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
    public bool IsComplete { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
}
