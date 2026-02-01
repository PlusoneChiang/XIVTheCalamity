using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using XIVTheCalamity.Dalamud.Models;

namespace XIVTheCalamity.Dalamud.Services;

/// <summary>
/// Dalamud update service
/// </summary>
public class DalamudUpdater
{
    private readonly ILogger<DalamudUpdater> _logger;
    private readonly DalamudPathService _pathService;
    private readonly HttpClient _httpClient;
    
    private const string VersionUrl = "https://plusonechiang.github.io/XIV-on-Mac-in-TC/dalamud_version.json";
    private const string AssetUrl = "https://plusonechiang.github.io/XIV-on-Mac-in-TC/dalamud_asset.json";
    private const string DotnetRuntimeUrl = "https://dotnetcli.azureedge.net/dotnet/Runtime/{0}/dotnet-runtime-{0}-win-x64.zip";
    private const string DesktopRuntimeUrl = "https://dotnetcli.azureedge.net/dotnet/WindowsDesktop/{0}/windowsdesktop-runtime-{0}-win-x64.zip";
    
    private CancellationTokenSource? _cts;
    
    public DalamudUpdater(ILogger<DalamudUpdater> logger, DalamudPathService pathService)
    {
        _logger = logger;
        _pathService = pathService;
        
        // Configure HttpClient for optimal performance
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 10,  // Allow more connections for parallel downloads
            EnableMultipleHttp2Connections = true,  // Enable HTTP/2 multiplexing
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        
        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan  // Use CancellationToken for timeout control instead
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "XIVTheCalamity/1.0");
    }
    
    /// <summary>Get current status</summary>
    public async Task<DalamudStatus> GetStatusAsync()
    {
        var status = new DalamudStatus
        {
            LocalVersion = _pathService.GetLocalVersion(),
            RuntimeVersion = _pathService.GetLocalRuntimeVersion(),
            RuntimeInstalled = _pathService.GetLocalRuntimeVersion() != null,
            AssetsVersion = _pathService.GetLocalAssetsVersion(),
            AssetsInstalled = _pathService.GetLocalAssetsVersion() > 0
        };
        
        try
        {
            status.State = DalamudState.Checking;
            var remoteVersion = await GetRemoteVersionAsync();
            
            if (remoteVersion == null)
            {
                status.State = DalamudState.Failed;
                status.ErrorMessage = "Failed to retrieve remote version information";
                return status;
            }
            
            status.RemoteVersion = remoteVersion.AssemblyVersion;
            status.SupportedGameVersion = remoteVersion.SupportedGameVer;
            
            // Get remote Assets version
            var remoteAssetManifest = await GetAssetManifestAsync();
            var remoteAssetsVersion = remoteAssetManifest?.Version ?? 0;
            
            if (status.LocalVersion == null)
            {
                status.State = DalamudState.NotInstalled;
            }
            else if (status.LocalVersion == remoteVersion.AssemblyVersion && status.AssetsVersion == remoteAssetsVersion)
            {
                status.State = DalamudState.UpToDate;
            }
            else
            {
                status.State = DalamudState.UpdateAvailable;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check Dalamud status");
            status.State = DalamudState.Failed;
            status.ErrorMessage = ex.Message;
        }
        
        return status;
    }
    
    /// <summary>Get remote version information</summary>
    public async Task<DalamudVersionInfo?> GetRemoteVersionAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<DalamudVersionInfo>(VersionUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve remote version information");
            return null;
        }
    }
    
    /// <summary>Get Asset list</summary>
    public async Task<DalamudAssetManifest?> GetAssetManifestAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<DalamudAssetManifest>(AssetUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve Asset list");
            return null;
        }
    }
    
    /// <summary>Execute complete update and stream progress</summary>
    public async IAsyncEnumerable<DalamudUpdateProgress> UpdateAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var currentProgress = new DalamudUpdateProgress();
        
        _pathService.EnsureDirectoriesExist();
        
        // 1. Check version
        currentProgress = ReportProgress(currentProgress, DalamudUpdateStage.CheckingVersion, "Checking version...");
        yield return currentProgress;
        
        var versionInfo = await GetRemoteVersionAsync();
        if (versionInfo == null)
        {
            currentProgress = ReportProgress(currentProgress, DalamudUpdateStage.Failed, "Failed to retrieve version");
            currentProgress.HasError = true;
            currentProgress.ErrorMessage = "Failed to retrieve remote version information";
            yield return currentProgress;
            yield break;
        }
        
        var localVersion = _pathService.GetLocalVersion();
        var needsUpdate = localVersion != versionInfo.AssemblyVersion;
        
        _logger.LogInformation("Dalamud version check: Local={LocalVersion}, Remote={RemoteVersion}, NeedsUpdate={NeedsUpdate}",
            localVersion ?? "Not installed", versionInfo.AssemblyVersion, needsUpdate);
        
        // 2. Download Dalamud (if needed)
        if (needsUpdate)
        {
            await foreach (var progress in DownloadDalamudAsync(versionInfo, _cts.Token))
            {
                yield return progress;
                currentProgress = progress;
            }
        }
        
        // 3. Download Runtime (if needed)
        if (versionInfo.RuntimeRequired)
        {
            var localRuntime = _pathService.GetLocalRuntimeVersion();
            if (localRuntime != versionInfo.RuntimeVersion)
            {
                await foreach (var progress in DownloadRuntimeAsync(versionInfo.RuntimeVersion, _cts.Token))
                {
                    yield return progress;
                    currentProgress = progress;
                }
            }
            else
            {
                _logger.LogInformation("Runtime 已是最新版本: {Version}", localRuntime);
            }
        }
        
        // 4. 下載 Assets (如需要)
        await foreach (var progress in DownloadAssetsAsync(_cts.Token))
        {
            yield return progress;
            currentProgress = progress;
        }
        
        // 完成
        currentProgress = ReportProgress(currentProgress, DalamudUpdateStage.Complete, "更新完成");
        currentProgress.IsComplete = true;
        yield return currentProgress;
    }
    
    /// <summary>取消更新</summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }
    
    #region Private Methods
    
    private async IAsyncEnumerable<DalamudUpdateProgress> DownloadDalamudAsync(DalamudVersionInfo versionInfo, [EnumeratorCancellation] CancellationToken ct)
    {
        var progress = new DalamudUpdateProgress
        {
            Stage = DalamudUpdateStage.DownloadingDalamud,
            CurrentFile = "下載 Dalamud..."
        };
        yield return progress;
        
        var tempFile = Path.Combine(Path.GetTempPath(), $"dalamud_{versionInfo.AssemblyVersion}.7z");
        var targetDir = _pathService.GetHooksVersionPath(versionInfo.AssemblyVersion);
        
        try
        {
            // 下載
            _logger.LogInformation("下載 Dalamud 從: {Url}", versionInfo.DownloadUrl);
            await foreach (var downloadProgress in DownloadFileWithProgressAsync(versionInfo.DownloadUrl, tempFile, ct))
            {
                downloadProgress.Stage = DalamudUpdateStage.DownloadingDalamud;
                yield return downloadProgress;
                progress = downloadProgress;
            }
            
            // 解壓
            progress = ReportProgress(progress, DalamudUpdateStage.ExtractingDalamud, "解壓 Dalamud...");
            yield return progress;
            await ExtractSevenZipAsync(tempFile, targetDir, ct);
            
            // 保存版本資訊
            var versionJson = JsonSerializer.Serialize(versionInfo);
            await File.WriteAllTextAsync(Path.Combine(targetDir, "version.json"), versionJson, ct);
            
            // 更新 dev 目錄 (符號連結或複製)
            await UpdateDevLinkAsync(targetDir, _pathService.HooksDevPath, ct);
            
            _logger.LogInformation("Dalamud {Version} 安裝完成", versionInfo.AssemblyVersion);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
    
    private async IAsyncEnumerable<DalamudUpdateProgress> DownloadRuntimeAsync(string version, [EnumeratorCancellation] CancellationToken ct)
    {
        var progress = new DalamudUpdateProgress
        {
            Stage = DalamudUpdateStage.DownloadingRuntime,
            CurrentFile = $"下載 .NET Runtime {version}...",
            TotalItems = 2,
            CompletedItems = 0
        };
        yield return progress;
        
        var tempDir = Path.Combine(Path.GetTempPath(), $"dotnet_runtime_{version}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            // 下載 dotnet-runtime (1/2)
            var dotnetUrl = string.Format(DotnetRuntimeUrl, version);
            var dotnetFile = Path.Combine(tempDir, "dotnet-runtime.zip");
            _logger.LogInformation("下載 .NET Runtime 從: {Url}", dotnetUrl);
            progress.CurrentFile = "dotnet-runtime.zip";
            
            await foreach (var downloadProgress in DownloadFileWithProgressAsync(dotnetUrl, dotnetFile, ct))
            {
                downloadProgress.Stage = DalamudUpdateStage.DownloadingRuntime;
                downloadProgress.TotalItems = 2;
                downloadProgress.CompletedItems = 0;
                downloadProgress.CurrentFile = "dotnet-runtime.zip";
                yield return downloadProgress;
                progress = downloadProgress;
            }
            
            progress.CompletedItems = 1;
            yield return progress;
            
            // 下載 windowsdesktop-runtime (2/2)
            var desktopUrl = string.Format(DesktopRuntimeUrl, version);
            var desktopFile = Path.Combine(tempDir, "windowsdesktop-runtime.zip");
            _logger.LogInformation("下載 Windows Desktop Runtime 從: {Url}", desktopUrl);
            progress.CurrentFile = "windowsdesktop-runtime.zip";
            
            await foreach (var downloadProgress in DownloadFileWithProgressAsync(desktopUrl, desktopFile, ct))
            {
                downloadProgress.Stage = DalamudUpdateStage.DownloadingRuntime;
                downloadProgress.TotalItems = 2;
                downloadProgress.CompletedItems = 1;
                downloadProgress.CurrentFile = "windowsdesktop-runtime.zip";
                yield return downloadProgress;
                progress = downloadProgress;
            }
            
            progress.CompletedItems = 2;
            yield return progress;
            
            // 解壓
            progress = ReportProgress(progress, DalamudUpdateStage.ExtractingRuntime, "解壓 Runtime...");
            yield return progress;
            await ExtractZipAsync(dotnetFile, _pathService.RuntimePath, ct);
            await ExtractZipAsync(desktopFile, _pathService.RuntimePath, ct);
            
            // 保存版本
            await File.WriteAllTextAsync(_pathService.RuntimeVersionFile, version, ct);
            
            _logger.LogInformation(".NET Runtime {Version} 安裝完成", version);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
    
    private async IAsyncEnumerable<DalamudUpdateProgress> DownloadAssetsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var progress = new DalamudUpdateProgress
        {
            Stage = DalamudUpdateStage.DownloadingAssets,
            CurrentFile = "檢查 Assets..."
        };
        yield return progress;
        
        var manifest = await GetAssetManifestAsync();
        if (manifest == null)
        {
            throw new Exception("無法取得 Asset 清單");
        }
        
        var localVersion = _pathService.GetLocalAssetsVersion();
        var assetDir = _pathService.GetAssetsVersionPath(manifest.Version);
        Directory.CreateDirectory(assetDir);
        
        _logger.LogInformation("本地 Assets 版本: {LocalVersion}, 遠端版本: {RemoteVersion}", 
            localVersion, manifest.Version);
        
        // 計算需要下載的檔案
        var filesToDownload = new List<DalamudAssetEntry>();
        foreach (var asset in manifest.Assets)
        {
            var localPath = Path.Combine(assetDir, asset.FileName);
            
            // 文件不存在 或 Hash驗證失敗 → 需要下載
            if (!File.Exists(localPath))
            {
                _logger.LogDebug("Asset 不存在，需要下載: {FileName}", asset.FileName);
                filesToDownload.Add(asset);
            }
            else if (!await VerifyFileHashAsync(localPath, asset.Hash))
            {
                _logger.LogWarning("Asset Hash驗證失敗，需要重新下載: {FileName}", asset.FileName);
                filesToDownload.Add(asset);
            }
        }
        
        if (filesToDownload.Count == 0)
        {
            _logger.LogInformation("Assets 已是最新版本 (v{Version}, {Count} 個檔案已驗證)", 
                manifest.Version, manifest.Assets.Count);
            
            // 確保版本文件存在
            if (localVersion != manifest.Version)
            {
                await File.WriteAllTextAsync(_pathService.AssetsVersionFile, manifest.Version.ToString(), ct);
                _logger.LogInformation("更新 Assets 版本文件: v{Version}", manifest.Version);
            }
            yield break;
        }
        
        _logger.LogInformation("需要下載 {Count}/{Total} 個 Asset 檔案", 
            filesToDownload.Count, manifest.Assets.Count);
        
        progress.TotalItems = filesToDownload.Count;
        progress.CompletedItems = 0;
        progress.CurrentFile = $"下載 Assets (0/{filesToDownload.Count})";
        yield return progress;
        
        // 並行下載 (最多 5 個) - 使用 Channel 報告進度
        var progressChannel = System.Threading.Channels.Channel.CreateUnbounded<DalamudUpdateProgress>();
        var semaphore = new SemaphoreSlim(5);
        var downloadedCount = 0;
        var completedCount = 0;
        
        var downloadTask = Task.Run(async () =>
        {
            var tasks = filesToDownload.Select(async asset =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var localPath = Path.Combine(assetDir, asset.FileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    
                    _logger.LogDebug("開始下載 Asset: {FileName} ({Index}/{Total})", 
                        asset.FileName, Interlocked.Increment(ref downloadedCount), filesToDownload.Count);
                    
                    // 刪除已存在的不完整檔案
                    if (File.Exists(localPath))
                    {
                        _logger.LogDebug("刪除舊檔案: {FileName}", asset.FileName);
                        File.Delete(localPath);
                    }
                    
                    // 使用 jsDelivr CDN 加速
                    var url = ConvertToJsDelivr(asset.Url);
                    if (url != asset.Url)
                    {
                        _logger.LogDebug("使用 CDN 加速: {OriginalUrl} → {CdnUrl}", asset.Url, url);
                    }
                    
                    try
                    {
                        await DownloadFileSimpleAsync(url, localPath, ct);
                    }
                    catch (Exception ex)
                    {
                        // 下載失敗，刪除可能部分寫入的檔案
                        if (File.Exists(localPath))
                        {
                            _logger.LogWarning("下載失敗，刪除不完整檔案: {FileName}", asset.FileName);
                            try
                            {
                                File.Delete(localPath);
                            }
                            catch (Exception deleteEx)
                            {
                                _logger.LogError(deleteEx, "無法刪除不完整檔案: {FileName}", asset.FileName);
                            }
                        }
                        throw new Exception($"下載失敗: {asset.FileName}", ex);
                    }
                    
                    // 驗證
                    if (!await VerifyFileHashAsync(localPath, asset.Hash))
                    {
                        _logger.LogWarning("Asset Hash驗證失敗: {FileName}", asset.FileName);
                        File.Delete(localPath);
                        throw new Exception($"Asset Hash驗證失敗: {asset.FileName}");
                    }
                    
                    _logger.LogDebug("完成下載 Asset: {FileName}", asset.FileName);
                    
                    var completed = Interlocked.Increment(ref completedCount);
                    
                    // 每 5 個檔案或最後一個檔案才報告進度
                    if (completed % 5 == 0 || completed == filesToDownload.Count)
                    {
                        _logger.LogInformation("Asset 下載進度: {Completed}/{Total}", completed, filesToDownload.Count);
                        await progressChannel.Writer.WriteAsync(new DalamudUpdateProgress
                        {
                            Stage = DalamudUpdateStage.DownloadingAssets,
                            CurrentFile = asset.FileName,
                            TotalItems = filesToDownload.Count,
                            CompletedItems = completed
                        }, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "下載 Asset 失敗: {FileName}", asset.FileName);
                    throw;
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            _logger.LogInformation("開始並行下載 {Count} 個 Asset 檔案...", filesToDownload.Count);
            await Task.WhenAll(tasks);
            _logger.LogInformation("所有 Asset 檔案下載完成");
            progressChannel.Writer.Complete();
        }, ct);
        
        // Stream progress from channel
        await foreach (var downloadProgress in progressChannel.Reader.ReadAllAsync(ct))
        {
            yield return downloadProgress;
        }
        
        await downloadTask; // Ensure download completes
        
        // 驗證所有檔案完整性（包含子目錄）
        _logger.LogInformation("驗證 Assets 完整性（包含子目錄）...");
        var failedFiles = new List<string>();
        var verifiedCount = 0;
        
        foreach (var asset in manifest.Assets)
        {
            var localPath = Path.Combine(assetDir, asset.FileName);
            
            // 確保父目錄存在
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                failedFiles.Add($"{asset.FileName} (父目錄不存在: {directory})");
                continue;
            }
            
            // 檢查檔案是否存在
            if (!File.Exists(localPath))
            {
                failedFiles.Add($"{asset.FileName} (檔案不存在)");
                continue;
            }
            
            // 驗證 Hash
            if (!await VerifyFileHashAsync(localPath, asset.Hash))
            {
                failedFiles.Add($"{asset.FileName} (Hash驗證失敗)");
                continue;
            }
            
            verifiedCount++;
        }
        
        if (failedFiles.Count > 0)
        {
            _logger.LogError("Assets 完整性驗證失敗，{FailedCount}/{TotalCount} 個檔案有問題:", 
                failedFiles.Count, manifest.Assets.Count);
            foreach (var file in failedFiles.Take(10))  // 只顯示前10個
            {
                _logger.LogError("  - {File}", file);
            }
            if (failedFiles.Count > 10)
            {
                _logger.LogError("  ... 還有 {Count} 個檔案", failedFiles.Count - 10);
            }
            throw new Exception($"Assets 完整性驗證失敗: {failedFiles.Count}/{manifest.Assets.Count} 個檔案有問題");
        }
        
        _logger.LogInformation("✓ Assets 完整性驗證通過 ({Count} 個檔案，包含子目錄)", verifiedCount);
        
        // 保存版本
        await File.WriteAllTextAsync(_pathService.AssetsVersionFile, manifest.Version.ToString(), ct);
        
        // 更新 dev 目錄
        await UpdateDevLinkAsync(assetDir, _pathService.AssetsDevPath, ct);
        
        _logger.LogInformation("Assets v{Version} 安裝完成", manifest.Version);
    }
    
    /// <summary>下載檔案並追蹤進度 (用於單一大檔案下載)</summary>
    private async IAsyncEnumerable<DalamudUpdateProgress> DownloadFileWithProgressAsync(string url, string targetPath, [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var progress = new DalamudUpdateProgress
        {
            TotalBytes = totalBytes,
            BytesDownloaded = 0
        };
        
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        
        var buffer = new byte[81920];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var lastYield = stopwatch.ElapsedMilliseconds;
        int bytesRead;
        
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            progress.BytesDownloaded += bytesRead;
            
            // Yield progress every 500ms
            if (stopwatch.ElapsedMilliseconds - lastYield >= 500)
            {
                yield return progress;
                lastYield = stopwatch.ElapsedMilliseconds;
            }
        }
        
        // Final progress
        yield return progress;
    }
    
    /// <summary>下載檔案 (不追蹤 bytes 進度，用於並行下載小檔案)</summary>
    private async Task DownloadFileSimpleAsync(string url, string targetPath, CancellationToken ct)
    {
        // Use per-request timeout of 5 minutes for large files (17MB CJK fonts)
        // Required speed: ~57 KB/s to download 17MB in 5 minutes
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));
        
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        
        await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        
        await contentStream.CopyToAsync(fileStream, cts.Token);
    }
    
    private async Task ExtractSevenZipAsync(string archivePath, string targetDir, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);
            Directory.CreateDirectory(targetDir);
            
            // 使用較大的 buffer 加速讀取 (262144 = 256KB)
            using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, 
                FileShare.Read, bufferSize: 262144, FileOptions.SequentialScan);
            using var archive = SevenZipArchive.Open(fileStream);
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (!reader.Entry.IsDirectory)
                {
                    reader.WriteEntryToDirectory(targetDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
        }, ct);
    }
    
    /// <summary>
    /// 使用 ReaderFactory 解壓 ZIP (async, 快速)
    /// </summary>
    private async Task ExtractZipFastAsync(string archivePath, string targetDir, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        
        using var fileStream = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ReaderFactory.Open(fileStream);
        await reader.WriteAllToDirectoryAsync(targetDir, new ExtractionOptions 
        { 
            ExtractFullPath = true, 
            Overwrite = true 
        });
    }
    
    /// <summary>
    /// 使用傳統方式解壓 ZIP (備用方案)
    /// </summary>
    private async Task ExtractZipLegacyAsync(string archivePath, string targetDir, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(targetDir);
            
            using var stream = File.OpenRead(archivePath);
            using var reader = ReaderFactory.Open(stream);
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (!reader.Entry.IsDirectory)
                {
                    reader.WriteEntryToDirectory(targetDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
        }, ct);
    }
    
    /// <summary>
    /// 解壓 ZIP 檔案 (優先使用快速方式，失敗則用傳統方式)
    /// </summary>
    private async Task ExtractZipAsync(string archivePath, string targetDir, CancellationToken ct)
    {
        try
        {
            await ExtractZipFastAsync(archivePath, targetDir, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "快速解壓失敗，使用傳統方式: {Archive}", archivePath);
            await ExtractZipLegacyAsync(archivePath, targetDir, ct);
        }
    }
    
    private async Task UpdateDevLinkAsync(string sourcePath, string devPath, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            // 刪除舊的 dev 目錄
            if (Directory.Exists(devPath))
                Directory.Delete(devPath, true);
            
            // 複製檔案到 dev
            CopyDirectory(sourcePath, devPath);
        }, ct);
    }
    
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        
        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        
        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }
    
    private static async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var hashBytes = await SHA1.HashDataAsync(stream);
            var actualHash = Convert.ToHexString(hashBytes);
            return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    
    private static string ConvertToJsDelivr(string url)
    {
        // raw.githubusercontent.com/user/repo/branch/path
        // → cdn.jsdelivr.net/gh/user/repo@branch/path
        if (url.Contains("raw.githubusercontent.com"))
        {
            var uri = new Uri(url);
            var parts = uri.AbsolutePath.TrimStart('/').Split('/', 4);
            if (parts.Length >= 4)
            {
                var user = parts[0];
                var repo = parts[1];
                var branch = parts[2];
                var path = parts[3];
                return $"https://cdn.jsdelivr.net/gh/{user}/{repo}@{branch}/{path}";
            }
        }
        return url;
    }
    
    private static DalamudUpdateProgress ReportProgress(DalamudUpdateProgress current, DalamudUpdateStage stage, string? currentFile = null)
    {
        return new DalamudUpdateProgress
        {
            Stage = stage,
            CurrentFile = currentFile,
            TotalBytes = 0,
            BytesDownloaded = 0,
            TotalItems = 0,
            CompletedItems = 0
        };
    }
    
    #endregion
}
