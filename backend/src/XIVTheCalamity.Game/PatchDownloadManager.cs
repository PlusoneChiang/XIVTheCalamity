using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Game.Models;

namespace XIVTheCalamity.Game.Services;

/// <summary>
/// Patch download manager using IAsyncEnumerable for progress reporting
/// Supports multi-threaded concurrent downloads with Channel-based progress aggregation
/// </summary>
public class PatchDownloadManager
{
    private readonly ILogger<PatchDownloadManager> _logger;
    private readonly HttpClient _httpClient;
    private readonly int _maxConcurrent;

    public PatchDownloadManager(
        ILogger<PatchDownloadManager> logger, 
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _maxConcurrent = 3; // Default to 3 concurrent downloads
    }

    /// <summary>
    /// Download all patches with progress reporting via IAsyncEnumerable
    /// Uses Channel to aggregate progress from multiple download threads
    /// </summary>
    public async IAsyncEnumerable<PatchProgressEvent> DownloadAllPatchesAsync(
        List<PatchInfo> patches,
        string downloadPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(downloadPath))
        {
            Directory.CreateDirectory(downloadPath);
        }

        _logger.LogInformation("Starting download of {Count} patches ({Concurrent} concurrent threads)", 
            patches.Count, _maxConcurrent);

        // Create channel for progress aggregation
        var progressChannel = Channel.CreateUnbounded<PatchProgressUpdate>();
        
        // Shared progress state (thread-safe via channel)
        var totalBytes = patches.Sum(p => p.Size);
        var patchProgress = new Dictionary<string, long>(); // filename -> bytes downloaded
        var completedPatches = 0;
        var downloadingCount = 0;
        var speedStopwatch = Stopwatch.StartNew();
        var lastTotalBytes = 0L;
        var lastSpeedUpdate = speedStopwatch.Elapsed;
        var lastYieldUpdate = speedStopwatch.Elapsed;
        
        // Initialize progress tracking
        foreach (var patch in patches)
        {
            patchProgress[patch.FileName] = 0;
        }
        
        // Start download tasks
        var downloadTask = Task.Run(async () =>
        {
            using var semaphore = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
            var downloadTasks = patches.Select(patch =>
                DownloadPatchWithProgressAsync(patch, downloadPath, progressChannel.Writer, semaphore, cancellationToken)
            );
            
            await Task.WhenAll(downloadTasks);
            progressChannel.Writer.Complete();
        }, cancellationToken);
        
        // Yield initial progress
        yield return new PatchProgressEvent
        {
            Stage = "download_started",
            MessageKey = "progress.download_started",
            Phase = "downloading",
            TotalPatches = patches.Count,
            CompletedPatches = 0,
            DownloadingCount = 0,
            TotalBytes = totalBytes,
            TotalBytesDownloaded = 0,
            Percentage = 0
        };
        
        // Read progress updates from channel and aggregate
        await foreach (var update in progressChannel.Reader.ReadAllAsync(cancellationToken))
        {
            // Update shared state based on update type
            switch (update.Type)
            {
                case ProgressUpdateType.DownloadStarted:
                    downloadingCount++;
                    break;
                    
                case ProgressUpdateType.DownloadProgress:
                    if (update.FileName != null)
                    {
                        patchProgress[update.FileName] = update.BytesDownloaded;
                    }
                    break;
                    
                case ProgressUpdateType.DownloadCompleted:
                    downloadingCount--;
                    completedPatches++;
                    if (update.FileName != null)
                    {
                        patchProgress[update.FileName] = update.TotalBytes;
                    }
                    break;
            }
            
            // Calculate aggregated progress
            var totalDownloaded = patchProgress.Values.Sum();
            
            // Calculate speed (every second)
            var downloadSpeed = 0.0;
            var elapsed = speedStopwatch.Elapsed;
            if ((elapsed - lastSpeedUpdate).TotalMilliseconds >= 1000)
            {
                var timeDiff = (elapsed - lastSpeedUpdate).TotalSeconds;
                var bytesDiff = totalDownloaded - lastTotalBytes;
                downloadSpeed = timeDiff > 0 ? bytesDiff / timeDiff : 0;
                
                lastSpeedUpdate = elapsed;
                lastTotalBytes = totalDownloaded;
            }
            
            // Yield progress event (throttle to avoid too many events)
            var shouldYield = update.Type == ProgressUpdateType.DownloadCompleted || 
                              (elapsed - lastYieldUpdate).TotalMilliseconds >= 500;
            
            if (shouldYield)
            {
                yield return new PatchProgressEvent
                {
                    Stage = "downloading",
                    MessageKey = "progress.downloading_patches",
                    Phase = "downloading",
                    TotalPatches = patches.Count,
                    CompletedPatches = completedPatches,
                    DownloadingCount = downloadingCount,
                    CurrentFileName = update.FileName,
                    CurrentFileDownloaded = update.BytesDownloaded,
                    CurrentFileSize = update.TotalBytes,
                    TotalBytes = totalBytes,
                    TotalBytesDownloaded = totalDownloaded,
                    DownloadSpeedBytesPerSec = downloadSpeed
                };
                
                lastYieldUpdate = elapsed;
            }
        }
        
        // Wait for all downloads to complete
        await downloadTask;
        
        _logger.LogInformation("All patches downloaded");
        
        // Yield completion
        yield return new PatchProgressEvent
        {
            Stage = "download_complete",
            MessageKey = "progress.download_complete",
            Phase = "downloading",
            TotalPatches = patches.Count,
            CompletedPatches = completedPatches,
            DownloadingCount = 0,
            TotalBytes = totalBytes,
            TotalBytesDownloaded = totalBytes,
            IsComplete = true,
            Percentage = 100
        };
    }

    /// <summary>
    /// Download a single patch and report progress to channel
    /// </summary>
    private async Task DownloadPatchWithProgressAsync(
        PatchInfo patch,
        string downloadPath,
        ChannelWriter<PatchProgressUpdate> progressWriter,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        
        try
        {
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
                    
                    await progressWriter.WriteAsync(new PatchProgressUpdate
                    {
                        Type = ProgressUpdateType.DownloadCompleted,
                        FileName = patch.FileName,
                        TotalBytes = patch.Size
                    }, cancellationToken);
                    
                    return;
                }
            }

            _logger.LogInformation("Starting download: {File} ({Size} MB)", 
                patch.FileName, patch.Size / 1024.0 / 1024.0);

            // Report download started
            await progressWriter.WriteAsync(new PatchProgressUpdate
            {
                Type = ProgressUpdateType.DownloadStarted,
                FileName = patch.FileName,
                TotalBytes = patch.Size
            }, cancellationToken);

            // Download file with progress
            await DownloadFileAsync(patch, filePath, progressWriter, cancellationToken);

            _logger.LogInformation("Download complete: {File}", patch.FileName);
            
            // Report download completed
            await progressWriter.WriteAsync(new PatchProgressUpdate
            {
                Type = ProgressUpdateType.DownloadCompleted,
                FileName = patch.FileName,
                TotalBytes = patch.Size
            }, cancellationToken);
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
            semaphore.Release();
        }
    }

    /// <summary>
    /// Download file with streaming and progress reporting
    /// </summary>
    private async Task DownloadFileAsync(
        PatchInfo patch,
        string filePath,
        ChannelWriter<PatchProgressUpdate> progressWriter,
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
        var progressStopwatch = Stopwatch.StartNew();

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;

            // Report progress every 200ms (more frequent than outer throttle)
            if (progressStopwatch.ElapsedMilliseconds >= 200)
            {
                await progressWriter.WriteAsync(new PatchProgressUpdate
                {
                    Type = ProgressUpdateType.DownloadProgress,
                    FileName = patch.FileName,
                    BytesDownloaded = totalBytesRead,
                    TotalBytes = patch.Size
                }, cancellationToken);
                
                progressStopwatch.Restart();
            }
        }
        
        // Final progress update
        await progressWriter.WriteAsync(new PatchProgressUpdate
        {
            Type = ProgressUpdateType.DownloadProgress,
            FileName = patch.FileName,
            BytesDownloaded = totalBytesRead,
            TotalBytes = patch.Size
        }, cancellationToken);
    }
}

/// <summary>
/// Internal progress update message for channel communication
/// </summary>
internal class PatchProgressUpdate
{
    public ProgressUpdateType Type { get; set; }
    public string? FileName { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
}

/// <summary>
/// Type of progress update
/// </summary>
internal enum ProgressUpdateType
{
    DownloadStarted,
    DownloadProgress,
    DownloadCompleted
}
