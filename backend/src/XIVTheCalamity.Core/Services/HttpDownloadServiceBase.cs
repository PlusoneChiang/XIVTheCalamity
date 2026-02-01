using System.Diagnostics;
using System.Runtime.CompilerServices;
using XIVTheCalamity.Core.Models.Progress;

namespace XIVTheCalamity.Core.Services;

/// <summary>
/// Base class for HTTP download services using IAsyncEnumerable for progress reporting
/// </summary>
public abstract class HttpDownloadServiceBase
{
    protected readonly HttpClient HttpClient;
    protected readonly Action<string>? LogInfo;
    protected readonly Action<string>? LogError;
    
    protected HttpDownloadServiceBase(
        HttpClient httpClient, 
        Action<string>? logInfo = null,
        Action<string>? logError = null)
    {
        HttpClient = httpClient;
        LogInfo = logInfo;
        LogError = logError;
    }
    
    /// <summary>
    /// Download a file with progress reporting using IAsyncEnumerable
    /// </summary>
    /// <param name="url">Download URL</param>
    /// <param name="destinationPath">Destination file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="progressInterval">Progress report interval in milliseconds (default: 500ms)</param>
    /// <returns>Async enumerable of download progress events</returns>
    protected async IAsyncEnumerable<DownloadProgressEvent> DownloadFileAsync(
        string url,
        string destinationPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        int progressInterval = 500)
    {
        var fileName = Path.GetFileName(url);
        LogInfo?.Invoke($"[DOWNLOAD] Starting download: {url} -> {destinationPath}");
        
        // Yield start event
        yield return new DownloadProgressEvent
        {
            Stage = "download_started",
            MessageKey = "progress.download_started",
            CurrentFile = fileName,
            Percentage = 0
        };
        
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
            LogError?.Invoke($"[DOWNLOAD] Failed to download {url}: {errorMsg}");
            
            yield return new DownloadProgressEvent
            {
                Stage = "download_failed",
                MessageKey = "error.download_failed",
                HasError = true,
                ErrorMessage = errorMsg,
                CurrentFile = fileName
            };
            yield break;
        }
        
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        LogInfo?.Invoke($"[DOWNLOAD] File size: {totalBytes} bytes ({totalBytes / (1024.0 * 1024.0):F2} MB)");
        
        // Perform download and collect progress events
        var progressEvents = new List<DownloadProgressEvent>();
        var finalEvent = await PerformDownloadAsync(
            response, 
            destinationPath, 
            fileName, 
            totalBytes, 
            progressInterval, 
            progressEvents,
            cancellationToken);
        
        // Yield all progress events
        foreach (var evt in progressEvents)
        {
            yield return evt;
        }
        
        // Yield final event
        yield return finalEvent;
    }
    
    private async Task<DownloadProgressEvent> PerformDownloadAsync(
        HttpResponseMessage response,
        string destinationPath,
        string fileName,
        long totalBytes,
        int progressInterval,
        List<DownloadProgressEvent> progressEvents,
        CancellationToken cancellationToken)
    {
        try
        {
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            
            var buffer = new byte[8192];
            long totalRead = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReportTime = stopwatch.Elapsed;
            long lastReportedBytes = 0;
            
            while (true)
            {
                var bytesRead = await contentStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0) break;
                
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;
                
                // Report progress at specified interval
                var elapsed = stopwatch.Elapsed;
                if ((elapsed - lastReportTime).TotalMilliseconds >= progressInterval)
                {
                    var timeSinceLastReport = (elapsed - lastReportTime).TotalSeconds;
                    var bytesSinceLastReport = totalRead - lastReportedBytes;
                    var speed = timeSinceLastReport > 0 ? bytesSinceLastReport / timeSinceLastReport : 0;
                    
                    progressEvents.Add(new DownloadProgressEvent
                    {
                        Stage = "downloading",
                        MessageKey = "progress.downloading",
                        CurrentFile = fileName,
                        BytesDownloaded = totalRead,
                        TotalBytes = totalBytes,
                        DownloadSpeedBytesPerSec = speed
                    });
                    
                    lastReportTime = elapsed;
                    lastReportedBytes = totalRead;
                }
            }
            
            // Final progress report
            var totalTime = stopwatch.Elapsed.TotalSeconds;
            var avgSpeed = totalTime > 0 ? totalRead / totalTime : 0;
            
            LogInfo?.Invoke($"[DOWNLOAD] Completed: {fileName}, {totalRead / (1024.0 * 1024.0):F2} MB in {totalTime:F1}s ({avgSpeed / (1024.0 * 1024.0):F2} MB/s)");
            
            return new DownloadProgressEvent
            {
                Stage = "download_complete",
                MessageKey = "progress.download_complete",
                CurrentFile = fileName,
                BytesDownloaded = totalRead,
                TotalBytes = totalBytes,
                DownloadSpeedBytesPerSec = avgSpeed,
                IsComplete = true,
                Percentage = 100
            };
        }
        catch (Exception ex)
        {
            LogError?.Invoke($"[DOWNLOAD] Download failed: {fileName} - {ex.Message}");
            
            return new DownloadProgressEvent
            {
                Stage = "download_error",
                MessageKey = "error.download_failed",
                HasError = true,
                ErrorMessage = ex.Message,
                CurrentFile = fileName
            };
        }
    }
}

