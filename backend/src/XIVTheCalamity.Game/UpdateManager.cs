using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Game.Models;

namespace XIVTheCalamity.Game.Services;

/// <summary>
/// Game update manager using IAsyncEnumerable for progress reporting
/// Coordinates version checking, downloads, installation, and cleanup
/// Flow: Download (parallel) → Install (sequential) → Cleanup (optional)
/// </summary>
public class UpdateManager
{
    private readonly ILogger<UpdateManager> _logger;
    private readonly GameVersionService _versionService;
    private readonly PatchListParser _patchListParser;
    private readonly PatchInstallService _patchInstallService;
    private readonly PatchDownloadManager _downloadManager;
    private readonly HttpClient _httpClient;

    private bool _keepPatches = false;

    public UpdateManager(
        ILogger<UpdateManager> logger,
        GameVersionService versionService,
        PatchListParser patchListParser,
        PatchInstallService patchInstallService,
        PatchDownloadManager downloadManager,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _versionService = versionService;
        _patchListParser = patchListParser;
        _patchInstallService = patchInstallService;
        _downloadManager = downloadManager;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// Set whether to keep patch files after installation
    /// </summary>
    public void SetKeepPatches(bool keep)
    {
        _keepPatches = keep;
    }

    /// <summary>
    /// Check for updates without installing
    /// </summary>
    public async Task<UpdateCheckResult> CheckUpdatesAsync(
        string gamePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Checking for updates...");

            var versionInfo = _versionService.GetLocalVersions(gamePath);
            var gameVersion = versionInfo.GetVersion(GameRepository.Game)?.Version ?? "2012.01.01.0000.0000";

            var allPatches = await _patchListParser.GetPatchesFromOfficialApiAsync(
                gameVersion, versionInfo, cancellationToken);
            
            var requiredPatches = _patchListParser.GetRequiredPatches(allPatches, versionInfo);

            if (requiredPatches.Count == 0)
            {
                _logger.LogInformation("Game is up to date");
                return new UpdateCheckResult
                {
                    NeedsUpdate = false,
                    CurrentVersions = versionInfo
                };
            }

            _logger.LogInformation("Found {Count} patches to download", requiredPatches.Count);
            
            return new UpdateCheckResult
            {
                NeedsUpdate = true,
                RequiredPatches = requiredPatches,
                TotalDownloadSize = requiredPatches.Sum(p => p.Size),
                CurrentVersions = versionInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed");
            return new UpdateCheckResult
            {
                NeedsUpdate = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Check and install updates with progress reporting via IAsyncEnumerable
    /// Yields progress events for download, install, and cleanup phases
    /// New flow: Download one → Install one → Delete one (saves disk space)
    /// </summary>
    public async IAsyncEnumerable<PatchProgressEvent> CheckAndInstallUpdatesAsync(
        string gamePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== Starting update check and install ===");

        // Yield initial progress immediately to keep SSE connection alive
        yield return new PatchProgressEvent
        {
            Stage = "checking",
            MessageKey = "progress.checking_updates",
            Phase = "checking",
            Percentage = 0
        };

        // Set patch download directory
        var patchDownloadPath = Path.Combine(gamePath, ".patches");
        if (!Directory.Exists(patchDownloadPath))
        {
            Directory.CreateDirectory(patchDownloadPath);
        }

        // Check for updates
        var checkResult = await CheckUpdatesAsync(gamePath, cancellationToken);
        
        if (!checkResult.NeedsUpdate)
        {
            yield return new PatchProgressEvent
            {
                Stage = "up_to_date",
                MessageKey = "progress.up_to_date",
                Phase = "checking",
                IsComplete = true,
                Percentage = 100
            };
            yield break;
        }

        if (checkResult.RequiredPatches == null || checkResult.RequiredPatches.Count == 0)
        {
            yield return new PatchProgressEvent
            {
                Stage = "error",
                MessageKey = "progress.check_failed",
                Phase = "checking",
                HasError = true,
                ErrorMessage = checkResult.ErrorMessage
            };
            yield break;
        }

        var requiredPatches = checkResult.RequiredPatches;
        _logger.LogInformation("Found {Count} patches to process", requiredPatches.Count);

        // New flow: Download → Install → Delete for each patch sequentially
        await foreach (var progress in DownloadInstallAndCleanupPatchesAsync(
            gamePath, requiredPatches, patchDownloadPath, cancellationToken))
        {
            yield return progress;
            
            // If error occurred, stop
            if (progress.HasError)
            {
                yield break;
            }
        }

        // Final completion
        yield return new PatchProgressEvent
        {
            Stage = "all_complete",
            MessageKey = "progress.all_complete",
            Phase = "complete",
            TotalPatches = requiredPatches.Count,
            CompletedPatches = requiredPatches.Count,
            InstalledPatches = requiredPatches.Count,
            IsComplete = true,
            Percentage = 100
        };

        _logger.LogInformation("=== Update completed successfully ===");
    }

    /// <summary>
    /// Download, install, and cleanup patches with parallel downloads and sequential installation
    /// Flow: Parallel downloads (4 threads) → Queue → Sequential install → Delete
    /// </summary>
    private async IAsyncEnumerable<PatchProgressEvent> DownloadInstallAndCleanupPatchesAsync(
        string gamePath,
        List<PatchInfo> patches,
        string downloadPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing {Count} patches (parallel download → queue → sequential install → delete)...", patches.Count);
        
        var totalBytes = patches.Sum(p => p.Size);
        var completedPatches = 0;
        var maxConcurrentDownloads = 4;
        
        // Channel for downloaded patches waiting to be installed (FIFO queue)
        var installQueue = Channel.CreateUnbounded<PatchInfo>(new UnboundedChannelOptions
        {
            SingleReader = true, // Only one installer thread
            SingleWriter = false // Multiple downloader threads
        });
        
        // Channel for download progress updates
        var progressChannel = Channel.CreateUnbounded<DownloadProgressUpdate>();
        
        // Progress tracking
        var downloadProgress = new Dictionary<string, long>();
        var downloadedCount = 0;
        var installedCount = 0;
        var downloadingCount = 0;
        var lastSpeedUpdate = DateTime.UtcNow;
        var lastTotalBytes = 0L;
        var currentTotalSpeed = 0.0;
        
        foreach (var patch in patches)
        {
            downloadProgress[patch.FileName] = 0;
        }
        
        // Task 1: Download patches in parallel (4 concurrent) - no explicit locks needed
        var downloadTask = Task.Run(async () =>
        {
            await Parallel.ForEachAsync(
                patches,
                new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = maxConcurrentDownloads,
                    CancellationToken = cancellationToken
                },
                async (patch, ct) =>
                {
                    await DownloadPatchForInstallAsync(patch, downloadPath, installQueue.Writer, 
                        progressChannel.Writer, ct);
                });
            
            installQueue.Writer.Complete();
            progressChannel.Writer.Complete();
            _logger.LogInformation("All downloads completed");
        }, cancellationToken);
        
        // Task 2: Install patches sequentially from queue
        var installTask = Task.Run(async () =>
        {
            await foreach (var patch in installQueue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    _logger.LogInformation("Installing ({Installed}/{Total}): {FileName}", 
                        installedCount + 1, patches.Count, patch.FileName);
                    
                    // Install patch
                    await Task.Run(() =>
                    {
                        _patchInstallService.InstallPatch(patch.LocalPath!, gamePath, patch.Repository);
                        _patchInstallService.UpdateVersionFile(gamePath, patch.Repository, patch.Version);
                    }, cancellationToken);
                    
                    installedCount++;
                    _logger.LogInformation("Installed: {FileName}", patch.FileName);
                    
                    // Delete patch file after installation (if not keeping)
                    if (!_keepPatches && !string.IsNullOrEmpty(patch.LocalPath) && File.Exists(patch.LocalPath))
                    {
                        try
                        {
                            File.Delete(patch.LocalPath);
                            _logger.LogDebug("Deleted: {FileName}", patch.FileName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete: {FileName}", patch.FileName);
                        }
                    }
                    
                    completedPatches++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to install: {FileName}", patch.FileName);
                    throw;
                }
            }
            
            _logger.LogInformation("All installations completed");
        }, cancellationToken);
        
        // Task 3: Monitor progress updates and report
        var progressStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var lastReportTime = progressStopwatch.Elapsed;
        
        await foreach (var update in progressChannel.Reader.ReadAllAsync(cancellationToken))
        {
            // Update download state
            switch (update.Type)
            {
                case DownloadUpdateType.Started:
                    downloadingCount++;
                    break;
                    
                case DownloadUpdateType.Progress:
                    if (update.FileName != null)
                    {
                        downloadProgress[update.FileName] = update.BytesDownloaded;
                    }
                    break;
                    
                case DownloadUpdateType.Completed:
                    downloadingCount--;
                    downloadedCount++;
                    if (update.FileName != null)
                    {
                        downloadProgress[update.FileName] = update.TotalBytes;
                    }
                    break;
            }
            
            var elapsed = progressStopwatch.Elapsed;
            var totalDownloaded = downloadProgress.Values.Sum();
            
            // Calculate current download speed (every 500ms)
            if ((DateTime.UtcNow - lastSpeedUpdate).TotalMilliseconds >= 500)
            {
                var timeDiff = (DateTime.UtcNow - lastSpeedUpdate).TotalSeconds;
                var bytesDiff = totalDownloaded - lastTotalBytes;
                
                if (timeDiff > 0 && bytesDiff >= 0)
                {
                    currentTotalSpeed = bytesDiff / timeDiff;
                }
                
                lastTotalBytes = totalDownloaded;
                lastSpeedUpdate = DateTime.UtcNow;
            }
            
            // Report progress every 500ms or on important events
            var shouldReport = update.Type == DownloadUpdateType.Completed || 
                              (elapsed - lastReportTime).TotalMilliseconds >= 500;
            
            if (shouldReport)
            {
                yield return new PatchProgressEvent
                {
                    Stage = "downloading",
                    MessageKey = "progress.downloading",
                    Phase = "downloading",
                    TotalPatches = patches.Count,
                    CompletedPatches = downloadedCount,
                    DownloadingCount = downloadingCount,
                    TotalBytes = totalBytes,
                    TotalBytesDownloaded = totalDownloaded,
                    DownloadSpeedBytesPerSec = currentTotalSpeed,
                    Percentage = totalBytes > 0 ? (int)((totalDownloaded * 100.0) / totalBytes) : 0
                };
                
                lastReportTime = elapsed;
            }
        }
        
        // Progress channel is complete, wait for install to finish silently
        _logger.LogInformation("All downloads complete, waiting for installations to finish...");
        
        // Wait for both tasks to complete
        await downloadTask;
        await installTask;
        
        // Check for errors
        if (downloadTask.IsFaulted)
        {
            var ex = downloadTask.Exception?.InnerException ?? downloadTask.Exception!;
            _logger.LogError(ex, "Download task failed");
            yield return new PatchProgressEvent
            {
                Stage = "download_error",
                MessageKey = "progress.download_error",
                Phase = "downloading",
                HasError = true,
                ErrorMessage = ex.Message
            };
            yield break;
        }
        
        if (installTask.IsFaulted)
        {
            var ex = installTask.Exception?.InnerException ?? installTask.Exception!;
            _logger.LogError(ex, "Install task failed");
            yield return new PatchProgressEvent
            {
                Stage = "install_error",
                MessageKey = "progress.install_error",
                Phase = "complete",
                HasError = true,
                ErrorMessage = ex.Message
            };
            yield break;
        }
        
        // Final progress report - all complete
        yield return new PatchProgressEvent
        {
            Stage = "all_complete",
            MessageKey = "progress.all_complete",
            Phase = "complete",
            TotalPatches = patches.Count,
            CompletedPatches = patches.Count,
            DownloadingCount = 0,
            TotalBytes = totalBytes,
            TotalBytesDownloaded = totalBytes,
            Percentage = 100,
            IsComplete = true
        };
        
        _logger.LogInformation("All patches processed successfully");
    }
    
    /// <summary>
    /// Download a single patch and add to install queue with progress reporting
    /// </summary>
    private async Task DownloadPatchForInstallAsync(
        PatchInfo patch,
        string downloadPath,
        ChannelWriter<PatchInfo> installQueue,
        ChannelWriter<DownloadProgressUpdate> progressWriter,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(downloadPath, patch.Repository.ToString(), patch.FileName);
        patch.LocalPath = filePath;

        // Ensure subdirectory exists
        var fileDirectory = Path.GetDirectoryName(filePath);
        if (fileDirectory != null && !Directory.Exists(fileDirectory))
        {
            Directory.CreateDirectory(fileDirectory);
        }

        // If file exists and size is correct, skip download
        if (File.Exists(filePath))
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == patch.Size)
            {
                _logger.LogInformation("Patch already exists, skipping download: {File}", patch.FileName);
                
                // Report as completed immediately (with full bytes)
                await progressWriter.WriteAsync(new DownloadProgressUpdate
                {
                    Type = DownloadUpdateType.Completed,
                    FileName = patch.FileName,
                    BytesDownloaded = patch.Size,
                    TotalBytes = patch.Size
                }, cancellationToken);
                
                await installQueue.WriteAsync(patch, cancellationToken);
                return;
            }
        }

        _logger.LogInformation("Downloading: {File} ({Size} MB)", 
            patch.FileName, patch.Size / 1024.0 / 1024.0);

        // Report download started (only for actual downloads)
        await progressWriter.WriteAsync(new DownloadProgressUpdate
        {
            Type = DownloadUpdateType.Started,
            FileName = patch.FileName,
            TotalBytes = patch.Size
        }, cancellationToken);

        // Download file with progress reporting
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
            bufferSize: 81920);

        var buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;
        var progressStopwatch = System.Diagnostics.Stopwatch.StartNew();

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;

            // Report progress every 200ms
            if (progressStopwatch.ElapsedMilliseconds >= 200)
            {
                await progressWriter.WriteAsync(new DownloadProgressUpdate
                {
                    Type = DownloadUpdateType.Progress,
                    FileName = patch.FileName,
                    BytesDownloaded = totalBytesRead,
                    TotalBytes = patch.Size
                }, cancellationToken);
                
                progressStopwatch.Restart();
            }
        }
        
        _logger.LogInformation("Downloaded: {File}", patch.FileName);
        
        // Report download completed
        await progressWriter.WriteAsync(new DownloadProgressUpdate
        {
            Type = DownloadUpdateType.Completed,
            FileName = patch.FileName,
            BytesDownloaded = totalBytesRead,
            TotalBytes = patch.Size
        }, cancellationToken);
        
        // Add to install queue (will be installed in order)
        await installQueue.WriteAsync(patch, cancellationToken);
    }
}

/// <summary>
/// Download progress update for tracking
/// </summary>
internal class DownloadProgressUpdate
{
    public DownloadUpdateType Type { get; set; }
    public string? FileName { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
}

/// <summary>
/// Type of download progress update
/// </summary>
internal enum DownloadUpdateType
{
    Started,
    Progress,
    Completed
}
