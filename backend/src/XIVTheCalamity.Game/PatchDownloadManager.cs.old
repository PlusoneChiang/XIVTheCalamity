using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
using XIVTheCalamity.Game.Models;

namespace XIVTheCalamity.Game.Services;

/// <summary>
/// Patch download manager (multi-threaded)
/// </summary>
public class PatchDownloadManager
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _downloadSemaphore;
    private readonly int _maxConcurrent;

    private DownloadProgress _currentProgress = new();
    private readonly object _progressLock = new();
    private Stopwatch _speedStopwatch = new();
    private long _lastBytesDownloaded = 0;
    
    // Track download progress per patch (filename -> bytes downloaded)
    private readonly Dictionary<string, long> _patchProgress = new();

    public PatchDownloadManager(
        ILogger logger, 
        IHttpClientFactory httpClientFactory,
        int maxConcurrent = 3)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _maxConcurrent = maxConcurrent;
        _downloadSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    /// <summary>
    /// Download all patches (multi-threaded)
    /// </summary>
    public async Task DownloadAllPatchesAsync(
        List<PatchInfo> patches,
        string downloadPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(downloadPath))
        {
            Directory.CreateDirectory(downloadPath);
        }

        // Initialize progress
        lock (_progressLock)
        {
            _currentProgress = new DownloadProgress
            {
                TotalPatches = patches.Count,
                CompletedPatches = 0,
                DownloadingPatches = 0,
                TotalBytes = patches.Sum(p => p.Size),
                TotalBytesDownloaded = 0,
                DownloadSpeedBytesPerSecond = 0
            };
        }

        _speedStopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting download of {Count} patches ({Concurrent} concurrent threads)", 
            patches.Count, _maxConcurrent);

        // Create download tasks
        var downloadTasks = patches.Select(patch =>
            DownloadPatchAsync(patch, downloadPath, progress, cancellationToken)
        );

        // Wait for all tasks to complete
        await Task.WhenAll(downloadTasks);

        _logger.LogInformation("All patches downloaded");
    }

    /// <summary>
    /// Download a single patch
    /// </summary>
    private async Task DownloadPatchAsync(
        PatchInfo patch,
        string downloadPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Limit concurrency
        await _downloadSemaphore.WaitAsync(cancellationToken);

        try
        {
            // Initialize progress tracking for this patch
            lock (_progressLock)
            {
                _patchProgress[patch.FileName] = 0;
            }
            
            // Update progress: start downloading
            lock (_progressLock)
            {
                _currentProgress.DownloadingPatches++;
                _currentProgress.CurrentFileName = patch.FileName;
                _currentProgress.CurrentFileSize = patch.Size;
                _currentProgress.CurrentFileDownloaded = 0;
            }
            progress?.Report(_currentProgress);

            var filePath = Path.Combine(downloadPath, patch.Repository.ToString(), patch.FileName);
            patch.LocalPath = filePath;

            // Ensure subdirectory exists
            var fileDirectory = Path.GetDirectoryName(filePath);
            if (fileDirectory != null && !Directory.Exists(fileDirectory))
            {
                Directory.CreateDirectory(fileDirectory);
            }

            // If file exists and size is correct, skip
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == patch.Size)
                {
                    _logger.LogInformation("Patch already exists, skipping: {File}", patch.FileName);
                    
                    // Mark as completed
                    lock (_progressLock)
                    {
                        _patchProgress[patch.FileName] = patch.Size;
                    }
                    
                    OnPatchCompleted(progress);
                    return;
                }
            }

            // Download file
            _logger.LogInformation("Starting download: {File} ({Size} MB)", 
                patch.FileName, patch.Size / 1024.0 / 1024.0);

            await DownloadFileAsync(patch, filePath, progress, cancellationToken);

            _logger.LogInformation("Download complete: {File}", patch.FileName);
            OnPatchCompleted(progress);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Download cancelled: {File}", patch.FileName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed: {File}", patch.FileName);
            throw;
        }
        finally
        {
            lock (_progressLock)
            {
                _currentProgress.DownloadingPatches--;
            }
            _downloadSemaphore.Release();
        }
    }

    /// <summary>
    /// Download file (streaming)
    /// </summary>
    private async Task DownloadFileAsync(
        PatchInfo patch,
        string filePath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            patch.Url, 
            HttpCompletionOption.ResponseHeadersRead, 
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(
            filePath, 
            FileMode.Create, 
            FileAccess.Write, 
            FileShare.None,
            bufferSize: 81920); // 80KB buffer

        var buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;

            // Update progress (every 100KB)
            if (totalBytesRead % (100 * 1024) == 0 || totalBytesRead == patch.Size)
            {
                lock (_progressLock)
                {
                    // Update current file progress
                    _currentProgress.CurrentFileDownloaded = totalBytesRead;
                    
                    // Update this patch's progress
                    _patchProgress[patch.FileName] = totalBytesRead;
                    
                    // Calculate total downloaded: cumulative progress of all patches
                    _currentProgress.TotalBytesDownloaded = _patchProgress.Values.Sum();
                    
                    _logger.LogDebug(
                        "[DOWNLOAD-PROGRESS] File: {FileName}, FileProgress: {FileProgress}/{FileSize}, " +
                        "TotalPatches: {TotalPatches}, PatchProgressCount: {ProgressCount}, " +
                        "TotalBytesDownloaded: {TotalDownloaded}/{TotalBytes}",
                        patch.FileName, totalBytesRead, patch.Size,
                        _currentProgress.TotalPatches, _patchProgress.Count,
                        _currentProgress.TotalBytesDownloaded, _currentProgress.TotalBytes);
                    
                    UpdateSpeed();
                }
                progress?.Report(_currentProgress);
            }
        }
    }

    /// <summary>
    /// Patch download completed
    /// </summary>
    private void OnPatchCompleted(IProgress<DownloadProgress>? progress)
    {
        lock (_progressLock)
        {
            _currentProgress.CompletedPatches++;
            _currentProgress.CurrentFileDownloaded = 0;
            _currentProgress.CurrentFileSize = 0;
            _currentProgress.CurrentFileName = null;
            
            _logger.LogDebug(
                "[DOWNLOAD-COMPLETE] Completed: {Completed}/{Total}, TotalBytesDownloaded: {TotalDownloaded}/{TotalBytes}",
                _currentProgress.CompletedPatches, _currentProgress.TotalPatches,
                _currentProgress.TotalBytesDownloaded, _currentProgress.TotalBytes);
        }
        progress?.Report(_currentProgress);
    }

    /// <summary>
    /// Update download speed and ETA
    /// </summary>
    private void UpdateSpeed()
    {
        var elapsed = _speedStopwatch.Elapsed.TotalSeconds;
        if (elapsed < 1) return;

        var currentTotalBytes = _currentProgress.TotalBytesDownloaded;
        var bytesDownloaded = currentTotalBytes - _lastBytesDownloaded;
        
        _currentProgress.DownloadSpeedBytesPerSecond = bytesDownloaded / elapsed;
        
        // Calculate ETA (based on total remaining)
        var remainingBytes = _currentProgress.TotalBytes - currentTotalBytes;
        if (_currentProgress.DownloadSpeedBytesPerSecond > 0)
        {
            var remainingSeconds = remainingBytes / _currentProgress.DownloadSpeedBytesPerSecond;
            _currentProgress.EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingSeconds);
        }

        _lastBytesDownloaded = currentTotalBytes;
        _speedStopwatch.Restart();
    }

    /// <summary>
    /// Get current progress
    /// </summary>
    public DownloadProgress GetCurrentProgress()
    {
        lock (_progressLock)
        {
            return _currentProgress;
        }
    }

    /// <summary>
    /// Cancel all downloads
    /// </summary>
    public void CancelAll()
    {
        _logger.LogWarning("Cancelling all downloads");
    }
}
