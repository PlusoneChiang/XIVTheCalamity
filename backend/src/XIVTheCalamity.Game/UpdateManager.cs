using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
using XIVTheCalamity.Game.Models;

namespace XIVTheCalamity.Game.Services;

/// <summary>
/// Game update manager (coordinates version checking, downloads, and installation)
/// Uses Taiwan official API which does NOT require sessionId
/// Supports parallel downloads with sequential installation
/// </summary>
public class UpdateManager
{
    private readonly ILogger<UpdateManager> _logger;
    private readonly GameVersionService _versionService;
    private readonly PatchListParser _patchListParser;
    private readonly PatchInstallService _patchInstallService;
    private readonly IHttpClientFactory _httpClientFactory;

    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isDownloading;
    private string _patchDownloadPath = string.Empty;
    private DownloadProgress? _currentProgress;
    
    // For speed calculation
    private DateTime _lastSpeedUpdate = DateTime.MinValue;
    private long _lastBytesDownloaded = 0;
    
    // Settings
    private bool _keepPatches = false;
    
    // Parallel download settings
    private const int MAX_CONCURRENT_DOWNLOADS = 4;
    
    // Thread-safe progress tracking
    private readonly object _progressLock = new();
    private readonly ConcurrentDictionary<string, long> _downloadedBytesPerPatch = new();

    public UpdateManager(
        ILogger<UpdateManager> logger,
        GameVersionService versionService,
        PatchListParser patchListParser,
        PatchInstallService patchInstallService,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _versionService = versionService;
        _patchListParser = patchListParser;
        _patchInstallService = patchInstallService;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Set whether to keep patch files after installation
    /// </summary>
    public void SetKeepPatches(bool keep)
    {
        _keepPatches = keep;
    }

    /// <summary>
    /// Check and install updates using Taiwan official API (no login required)
    /// Uses parallel downloads with sequential installation
    /// </summary>
    public async Task<UpdateCheckResult> CheckAndInstallUpdatesAsync(
        string gamePath,
        IProgress<DownloadProgress>? progress = null)
    {
        if (_isDownloading)
        {
            _logger.LogWarning("Download already in progress, ignoring duplicate request");
            return new UpdateCheckResult
            {
                NeedsUpdate = false,
                ErrorMessage = "Download already in progress"
            };
        }

        try
        {
            _isDownloading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            _logger.LogInformation("=== Starting update check and install (Official API) ===");

            // Set patch download directory
            _patchDownloadPath = Path.Combine(gamePath, ".patches");
            if (!Directory.Exists(_patchDownloadPath))
            {
                Directory.CreateDirectory(_patchDownloadPath);
            }

            // Read local versions
            var versionInfo = _versionService.GetLocalVersions(gamePath);
            _logger.LogInformation("Local versions:");
            foreach (var (repo, version) in versionInfo.Versions)
            {
                _logger.LogInformation("  {Repo}: {Version}", repo, version.Version);
            }

            // Get game version for API call
            var gameVersion = versionInfo.GetVersion(GameRepository.Game)?.Version ?? "2012.01.01.0000.0000";
            _logger.LogInformation("Calling official API with game version: {GameVersion}", gameVersion);

            // Fetch patch list from official API (Taiwan API does NOT need sessionId)
            var allPatches = await _patchListParser.GetPatchesFromOfficialApiAsync(gameVersion, versionInfo, token);
            _logger.LogInformation("Fetched {Count} patches from official API", allPatches.Count);

            // Filter required patches
            var requiredPatches = _patchListParser.GetRequiredPatches(allPatches, versionInfo);

            if (requiredPatches.Count == 0)
            {
                _logger.LogInformation("Game is up to date");
                _isDownloading = false;
                return new UpdateCheckResult
                {
                    NeedsUpdate = false,
                    CurrentVersions = versionInfo
                };
            }

            _logger.LogInformation("Found {Count} patches to download and install", requiredPatches.Count);

            // Process patches with parallel download and sequential install
            return await ProcessPatchesParallelAsync(gamePath, requiredPatches, progress, token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Update cancelled by user");
            _isDownloading = false;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed");
            _isDownloading = false;
            return new UpdateCheckResult
            {
                NeedsUpdate = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Core method: Parallel downloads with sequential installation
    /// </summary>
    private async Task<UpdateCheckResult> ProcessPatchesParallelAsync(
        string gamePath,
        List<PatchInfo> requiredPatches,
        IProgress<DownloadProgress>? progress,
        CancellationToken token)
    {
        // Initialize progress
        _downloadedBytesPerPatch.Clear();
        _currentProgress = new DownloadProgress
        {
            Phase = UpdatePhase.Downloading,
            TotalPatches = requiredPatches.Count,
            TotalBytes = requiredPatches.Sum(p => p.Size)
        };
        _lastSpeedUpdate = DateTime.UtcNow;
        _lastBytesDownloaded = 0;
        
        progress?.Report(_currentProgress);

        // Prepare patch paths
        foreach (var patch in requiredPatches)
        {
            var patchPath = Path.Combine(_patchDownloadPath, patch.Repository.ToString(), patch.FileName);
            patch.LocalPath = patchPath;
            
            var patchDir = Path.GetDirectoryName(patchPath);
            if (!string.IsNullOrEmpty(patchDir) && !Directory.Exists(patchDir))
            {
                Directory.CreateDirectory(patchDir);
            }
        }

        // Create queues for coordination
        var downloadQueue = new ConcurrentQueue<PatchInfo>(requiredPatches);
        var readyToInstall = new ConcurrentQueue<PatchInfo>();
        var downloadedSet = new ConcurrentDictionary<string, bool>();
        
        // Semaphore for controlling concurrent downloads
        using var downloadSemaphore = new SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS);
        
        // Task completion tracking
        var downloadTasks = new List<Task>();
        var installComplete = new TaskCompletionSource<bool>();
        var allDownloadsComplete = false;

        // Start download tasks
        var downloadTask = Task.Run(async () =>
        {
            var activeTasks = new List<Task>();
            
            while (!token.IsCancellationRequested)
            {
                // Clean up completed tasks
                activeTasks.RemoveAll(t => t.IsCompleted);
                
                // Try to get next patch to download
                if (downloadQueue.TryDequeue(out var patch))
                {
                    await downloadSemaphore.WaitAsync(token);
                    
                    var downloadTaskItem = DownloadPatchWithSemaphoreAsync(
                        patch, downloadSemaphore, downloadedSet, readyToInstall, progress, token);
                    activeTasks.Add(downloadTaskItem);
                }
                else if (activeTasks.Count == 0)
                {
                    // No more patches and no active downloads
                    break;
                }
                else
                {
                    // Wait for any download to complete
                    await Task.WhenAny(activeTasks);
                }
            }
            
            // Wait for remaining downloads
            if (activeTasks.Count > 0)
            {
                await Task.WhenAll(activeTasks);
            }
            
            allDownloadsComplete = true;
            _logger.LogInformation("All downloads completed");
        }, token);

        // Start install task (sequential)
        var installTask = Task.Run(async () =>
        {
            var installIndex = 0;
            
            while (installIndex < requiredPatches.Count && !token.IsCancellationRequested)
            {
                var patchToInstall = requiredPatches[installIndex];
                
                // Wait for this specific patch to be ready (must install in order)
                while (!downloadedSet.ContainsKey(patchToInstall.FileName) && !token.IsCancellationRequested)
                {
                    await Task.Delay(100, token);
                }
                
                if (token.IsCancellationRequested) break;
                
                // Install the patch
                await InstallPatchAsync(gamePath, patchToInstall, installIndex, requiredPatches.Count, progress, token);
                installIndex++;
            }
            
            installComplete.TrySetResult(true);
            _logger.LogInformation("All installations completed");
        }, token);

        // Wait for both tasks
        try
        {
            await Task.WhenAll(downloadTask, installTask);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Update cancelled");
            throw;
        }

        _logger.LogInformation("=== All patches downloaded and installed successfully ===");
        _isDownloading = false;
        
        lock (_progressLock)
        {
            if (_currentProgress != null)
            {
                _currentProgress.DownloadingCount = 0;
                _currentProgress.InstallingCount = 0;
                progress?.Report(_currentProgress);
            }
            _currentProgress = null;
        }

        return new UpdateCheckResult
        {
            NeedsUpdate = true,
            RequiredPatches = requiredPatches,
            TotalDownloadSize = requiredPatches.Sum(p => p.Size),
            CurrentVersions = _versionService.GetLocalVersions(gamePath)
        };
    }

    /// <summary>
    /// Download a single patch and release semaphore when done
    /// </summary>
    private async Task DownloadPatchWithSemaphoreAsync(
        PatchInfo patch,
        SemaphoreSlim semaphore,
        ConcurrentDictionary<string, bool> downloadedSet,
        ConcurrentQueue<PatchInfo> readyToInstall,
        IProgress<DownloadProgress>? progress,
        CancellationToken token)
    {
        try
        {
            // Update downloading count
            lock (_progressLock)
            {
                if (_currentProgress != null)
                {
                    _currentProgress.DownloadingCount++;
                    progress?.Report(_currentProgress);
                }
            }
            
            _logger.LogInformation("Downloading patch: {FileName} ({Size:F2} MB)",
                patch.FileName, patch.Size / 1024.0 / 1024.0);

            // Check if already downloaded
            if (File.Exists(patch.LocalPath) && new FileInfo(patch.LocalPath!).Length == patch.Size)
            {
                _logger.LogInformation("Patch already downloaded, skipping: {FileName}", patch.FileName);
                _downloadedBytesPerPatch[patch.FileName] = patch.Size;
                UpdateTotalProgress(progress);
            }
            else
            {
                await DownloadPatchAsync(patch, progress, token);
            }

            // Mark as downloaded
            downloadedSet[patch.FileName] = true;
            readyToInstall.Enqueue(patch);
            
            _logger.LogInformation("Download completed: {FileName}", patch.FileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to download patch: {FileName}", patch.FileName);
            throw;
        }
        finally
        {
            // Update downloading count and release semaphore
            lock (_progressLock)
            {
                if (_currentProgress != null)
                {
                    _currentProgress.DownloadingCount--;
                    progress?.Report(_currentProgress);
                }
            }
            semaphore.Release();
        }
    }

    /// <summary>
    /// Download a single patch file with progress reporting
    /// </summary>
    private async Task DownloadPatchAsync(
        PatchInfo patch,
        IProgress<DownloadProgress>? progress,
        CancellationToken token)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30);
        
        using var response = await httpClient.GetAsync(patch.Url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        using var contentStream = await response.Content.ReadAsStreamAsync(token);
        using var fileStream = new FileStream(patch.LocalPath!, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, token)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            totalRead += bytesRead;

            // Update per-patch progress
            _downloadedBytesPerPatch[patch.FileName] = totalRead;

            // Update total progress every 100KB
            if (totalRead % (100 * 1024) < 81920)
            {
                UpdateTotalProgress(progress);
            }
        }

        _downloadedBytesPerPatch[patch.FileName] = totalRead;
        UpdateTotalProgress(progress);
    }

    /// <summary>
    /// Update total download progress (thread-safe)
    /// </summary>
    private void UpdateTotalProgress(IProgress<DownloadProgress>? progress)
    {
        lock (_progressLock)
        {
            if (_currentProgress == null) return;
            
            // Sum all downloaded bytes
            _currentProgress.TotalBytesDownloaded = _downloadedBytesPerPatch.Values.Sum();
            
            // Calculate speed (every second)
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastSpeedUpdate).TotalSeconds;
            if (elapsed >= 1.0)
            {
                var bytesDownloadedSinceLastUpdate = _currentProgress.TotalBytesDownloaded - _lastBytesDownloaded;
                _currentProgress.DownloadSpeedBytesPerSecond = bytesDownloadedSinceLastUpdate / elapsed;
                
                // Estimate time remaining (based on remaining bytes to download + install)
                var remainingBytes = _currentProgress.TotalBytes - _currentProgress.TotalBytesDownloaded;
                if (_currentProgress.DownloadSpeedBytesPerSecond > 0)
                {
                    var remainingSeconds = remainingBytes / _currentProgress.DownloadSpeedBytesPerSecond;
                    _currentProgress.EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingSeconds);
                }
                
                _lastSpeedUpdate = now;
                _lastBytesDownloaded = _currentProgress.TotalBytesDownloaded;
            }
            
            progress?.Report(_currentProgress);
        }
    }

    /// <summary>
    /// Install a patch (sequential, one at a time)
    /// </summary>
    private async Task InstallPatchAsync(
        string gamePath,
        PatchInfo patch,
        int index,
        int total,
        IProgress<DownloadProgress>? progress,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        
        lock (_progressLock)
        {
            if (_currentProgress != null)
            {
                _currentProgress.Phase = UpdatePhase.Installing;
                _currentProgress.InstallingCount = 1;
                _currentProgress.InstallingFileName = patch.FileName;
                progress?.Report(_currentProgress);
            }
        }

        _logger.LogInformation("Installing patch {Index}/{Total}: {FileName}",
            index + 1, total, patch.FileName);

        try
        {
            _patchInstallService.InstallPatch(patch.LocalPath!, gamePath, patch.Repository);
            _patchInstallService.UpdateVersionFile(gamePath, patch.Repository, patch.Version);

            lock (_progressLock)
            {
                if (_currentProgress != null)
                {
                    _currentProgress.InstalledPatches++;
                    _currentProgress.CompletedPatches++;
                    _currentProgress.InstallingCount = 0;
                    progress?.Report(_currentProgress);
                }
            }

            _logger.LogInformation("Patch installed: {FileName}, version updated to {Version}",
                patch.FileName, patch.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install patch: {FileName}", patch.FileName);
            throw;
        }

        // Cleanup after install
        if (!_keepPatches)
        {
            try
            {
                File.Delete(patch.LocalPath!);
                _logger.LogDebug("Deleted patch file: {FileName}", patch.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete patch file: {FileName}", patch.FileName);
            }
        }
        
        // Small delay to allow progress update
        await Task.Delay(10, token);
    }

    /// <summary>
    /// Cancel update
    /// </summary>
    public void OnUserClickLogin()
    {
        if (_isDownloading && _cancellationTokenSource != null)
        {
            _logger.LogInformation("Cancelling update");
            _cancellationTokenSource.Cancel();
            _isDownloading = false;
            _currentProgress = null;
        }
    }

    /// <summary>
    /// Get current download progress
    /// </summary>
    public DownloadProgress? GetCurrentProgress()
    {
        lock (_progressLock)
        {
            return _currentProgress;
        }
    }

    /// <summary>
    /// Whether download is in progress
    /// </summary>
    public bool IsDownloading => _isDownloading;
}
