using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Dalamud.Json;
using XIVTheCalamity.Dalamud.Models;

namespace XIVTheCalamity.Dalamud.Services;

/// <summary>
/// Dalamud update service
/// </summary>
public class DalamudUpdater
{
    private readonly ILogger<DalamudUpdater> _logger;
    private readonly DalamudPathService _pathService;
    private readonly ConfigService _configService;
    private readonly HttpClient _httpClient;
    private const string AssetUrl = "https://plusonechiang.github.io/DalamudAssets/asset.json";
    private const string GitHubReleasesUrl = "https://api.github.com/repos/PlusoneChiang/Dalamud-TC-Patcher/releases?per_page=100";

    private const string DotnetRuntimeUrl = "https://dotnetcli.azureedge.net/dotnet/Runtime/{0}/dotnet-runtime-{0}-win-x64.zip";
    private const string DesktopRuntimeUrl = "https://dotnetcli.azureedge.net/dotnet/WindowsDesktop/{0}/windowsdesktop-runtime-{0}-win-x64.zip";
    
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private CancellationTokenSource? _cts;
    
    public DalamudUpdater(ILogger<DalamudUpdater> logger, DalamudPathService pathService, ConfigService configService)
    {
        _logger = logger;
        _pathService = pathService;
        _configService = configService;
        
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
    public async Task<DalamudStatus> GetStatusAsync(bool? useLatestPreRelease = null)
    {
        MigrateOldDalamudDirectories();

        if (await _updateLock.WaitAsync(0))
        {
            try { CleanupOldDalamudVersions(); }
            finally { _updateLock.Release(); }
        }

        var usePreRelease = useLatestPreRelease ?? (await _configService.LoadConfigAsync()).Dalamud.UseLatestPreRelease;
        var localVersionInfo = _pathService.GetLocalVersionInfo(usePreRelease);
        var status = new DalamudStatus
        {
            LocalVersion = localVersionInfo?.AssemblyVersion,
            LocalVersionInfo = localVersionInfo,
            RuntimeVersion = _pathService.GetLocalRuntimeVersion(),
            RuntimeInstalled = _pathService.GetLocalRuntimeVersion() != null,
            AssetsVersion = _pathService.GetLocalAssetsVersion(),
            AssetsInstalled = _pathService.GetLocalAssetsVersion() > 0
        };
        
        try
        {
            status.State = DalamudState.Checking;

            var remoteVersion = usePreRelease
                ? await GetLatestPreReleaseVersionInfoAsync()
                : await GetRemoteVersionAsync();
            
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
            else if (IsUpToDate(status, remoteVersion, remoteAssetsVersion))
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

    private static bool IsUpToDate(DalamudStatus status, DalamudVersionInfo version, int assetsVersion) =>
        status.LocalVersion == version.AssemblyVersion && status.AssetsVersion == assetsVersion &&
        (!version.RuntimeRequired || (status.RuntimeInstalled && status.RuntimeVersion == version.RuntimeVersion));
    
    /// <summary>取得新來源最新正式版的版本資訊。</summary>
    public Task<DalamudVersionInfo?> GetRemoteVersionAsync() => GetReleaseVersionInfoAsync(false);

    /// <summary>優先取得候選版，沒有候選版時使用正式版。</summary>
    public Task<DalamudVersionInfo?> GetLatestPreReleaseVersionInfoAsync() => GetReleaseVersionInfoAsync(true);

    private static GitHubRelease? SelectRelease(IEnumerable<GitHubRelease> releases, bool usePreRelease)
    {
        var published = releases.Where(r => !r.Draft).OrderByDescending(r => r.PublishedAt).ToList();
        return (usePreRelease ? published.FirstOrDefault(r => r.Prerelease) : null)
            ?? published.FirstOrDefault(r => !r.Prerelease);
    }

    private async Task<DalamudVersionInfo?> GetReleaseVersionInfoAsync(bool usePreRelease)
    {
        try
        {
            var releases = new List<GitHubRelease>();
            for (var page = 1; ; page++)
            {
                var batch = await _httpClient.GetFromJsonAsync($"{GitHubReleasesUrl}&page={page}", DalamudJsonContext.Default.ListGitHubRelease)
                    ?? throw new InvalidDataException("GitHub Releases 回應為空。");
                releases.AddRange(batch);
                if (batch.Count < 100)
                    break;
            }
            var target = SelectRelease(releases, usePreRelease);
            if (target == null)
                throw new InvalidDataException("新來源尚未提供所選頻道的 Dalamud 發布版本。");

            var manifest = target.Assets.SingleOrDefault(a => a.Name == "versionInfo.json")
                ?? throw new InvalidDataException($"Dalamud {target.TagName} 缺少 versionInfo.json。");
            var releaseInfo = await _httpClient.GetFromJsonAsync(manifest.BrowserDownloadUrl, DalamudJsonContext.Default.DalamudReleaseInfo)
                ?? throw new InvalidDataException("Dalamud versionInfo.json 為空。");
            var checksum = target.Assets.SingleOrDefault(a => a.Name == $"dalamud-{releaseInfo.Id}.zip.sha256")
                ?? throw new InvalidDataException("Dalamud Release 缺少 SHA-256 附件。");
            var version = releaseInfo.ToVersionInfo(target, await _httpClient.GetStringAsync(checksum.BrowserDownloadUrl));

            _logger.LogInformation("選用 Dalamud {Version}，上游={UpstreamVersion}，TC={TcRevision}，測試={PreReleaseRevision}",
                version.AssemblyVersion, version.UpstreamVersion, version.TcRevision, version.PreReleaseRevision);
            return version;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "無法取得 Dalamud 發布版本資訊");
            return null;
        }
    }

    /// <summary>Get Asset list</summary>
    public async Task<DalamudAssetManifest?> GetAssetManifestAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync(AssetUrl, DalamudJsonContext.Default.DalamudAssetManifest);
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
        using var updateCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await _updateLock.WaitAsync(cancellationToken);
        _cts = updateCts;
        try
        {
            var currentProgress = new DalamudUpdateProgress();

            MigrateOldDalamudDirectories();

            _pathService.EnsureDirectoriesExist();

            // 1. Check version
            currentProgress = ReportProgress(currentProgress, DalamudUpdateStage.CheckingVersion, "Checking version...");
            yield return currentProgress;

            var config = await _configService.LoadConfigAsync();
            var versionInfo = config.Dalamud.UseLatestPreRelease
                ? await GetLatestPreReleaseVersionInfoAsync()
                : await GetRemoteVersionAsync();
            if (versionInfo == null)
            {
                currentProgress = ReportProgress(currentProgress, DalamudUpdateStage.Failed, "Failed to retrieve version");
                currentProgress.HasError = true;
                currentProgress.ErrorMessage = "Failed to retrieve remote version information";
                yield return currentProgress;
                yield break;
            }

            var localVersion = _pathService.GetLocalVersion(config.Dalamud.UseLatestPreRelease);
            var needsUpdate = localVersion != versionInfo.AssemblyVersion;

            _logger.LogInformation("Dalamud version check: Local={LocalVersion}, Remote={RemoteVersion}, NeedsUpdate={NeedsUpdate}",
                localVersion ?? "Not installed", versionInfo.AssemblyVersion, needsUpdate);

            // 2. Download Dalamud (if needed)
            if (needsUpdate)
            {
                await foreach (var progress in DownloadDalamudAsync(versionInfo, config.Dalamud.UseLatestPreRelease, updateCts.Token))
                {
                    yield return progress;
                    currentProgress = progress;
                }
            }
            CleanupOldDalamudVersions();

            // 3. Download Runtime (if needed)
            if (versionInfo.RuntimeRequired)
            {
                var localRuntime = _pathService.GetLocalRuntimeVersion();
                if (localRuntime != versionInfo.RuntimeVersion)
                {
                    await foreach (var progress in DownloadRuntimeAsync(versionInfo.RuntimeVersion, updateCts.Token))
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

            // 4. 下載 Assets (版本有變時才強制重新下載)
            var localAssetsVersion = _pathService.GetLocalAssetsVersion();
            var assetManifest = await GetAssetManifestAsync();
            var assetsNeedUpdate = assetManifest != null && localAssetsVersion != assetManifest.Version;

            _logger.LogInformation("Assets version check: Local={LocalVersion}, Remote={RemoteVersion}, NeedsUpdate={NeedsUpdate}",
                localAssetsVersion, assetManifest?.Version, assetsNeedUpdate);

            await foreach (var progress in DownloadAssetsAsync(updateCts.Token, forceUpdate: assetsNeedUpdate, preloadedManifest: assetManifest))
            {
                yield return progress;
                currentProgress = progress;
            }

            // 5. 刪除舊版 Assets 目錄
            if (assetsNeedUpdate && assetManifest != null)
            {
                CleanupOldAssetsVersions(assetManifest.Version);
            }

            // 完成
            currentProgress = ReportProgress(currentProgress, DalamudUpdateStage.Complete, "更新完成");
            currentProgress.IsComplete = true;
            yield return currentProgress;
        }
        finally
        {
            _cts = null;
            _updateLock.Release();
        }
    }

    /// <summary>取消更新</summary>
    public void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 更新可能剛好在取消請求送達前結束。
        }
    }
    
    #region Private Methods
    
    private async IAsyncEnumerable<DalamudUpdateProgress> DownloadDalamudAsync(DalamudVersionInfo versionInfo, bool useLatestPreRelease, [EnumeratorCancellation] CancellationToken ct)
    {
        var progress = new DalamudUpdateProgress
        {
            Stage = DalamudUpdateStage.DownloadingDalamud,
            CurrentFile = "下載 Dalamud..."
        };
        yield return progress;
        
        var ext = GetArchiveExtension(versionInfo.DownloadUrl);
        var tempDir = Path.Combine(Path.GetTempPath(), $"xivtc-dalamud-{Guid.NewGuid():N}");
        var tempFile = Path.Combine(tempDir, $"package{ext}");
        var targetDir = Path.Combine(tempDir, "extracted");
        
        try
        {
            Directory.CreateDirectory(tempDir);
            // 下載
            _logger.LogInformation("下載 Dalamud 從: {Url}", versionInfo.DownloadUrl);
            await foreach (var downloadProgress in DownloadFileWithProgressAsync(versionInfo.DownloadUrl, tempFile, ct))
            {
                downloadProgress.Stage = DalamudUpdateStage.DownloadingDalamud;
                yield return downloadProgress;
                progress = downloadProgress;
            }
            
            await using (var archiveStream = File.OpenRead(tempFile))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, ct));
                if (!hash.Equals(versionInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Dalamud 套件 SHA-256 不符，已停止安裝。");
            }

            // 解壓
            progress = ReportProgress(progress, DalamudUpdateStage.ExtractingDalamud, "解壓 Dalamud...");
            yield return progress;
            await ExtractArchiveAsync(tempFile, targetDir, ct);
            
            // 保存版本資訊
            var versionJson = JsonSerializer.Serialize(versionInfo, DalamudJsonContext.Default.DalamudVersionInfo);
            await File.WriteAllTextAsync(Path.Combine(targetDir, "version.json"), versionJson, ct);
            
            // 只更新所選頻道，保留另一個頻道的安裝。
            await ReplaceDirectoryAsync(targetDir, _pathService.GetHooksPath(useLatestPreRelease), ct);
            
            _logger.LogInformation("Dalamud {Version} 安裝完成", versionInfo.AssemblyVersion);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
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
    
    private async IAsyncEnumerable<DalamudUpdateProgress> DownloadAssetsAsync([EnumeratorCancellation] CancellationToken ct, bool forceUpdate = false, DalamudAssetManifest? preloadedManifest = null)
    {
        var progress = new DalamudUpdateProgress
        {
            Stage = DalamudUpdateStage.DownloadingAssets,
            CurrentFile = "檢查 Assets..."
        };
        yield return progress;
        
        var manifest = preloadedManifest ?? await GetAssetManifestAsync();
        if (manifest == null)
        {
            throw new Exception("無法取得 Asset 清單");
        }
        
        var localVersion = _pathService.GetLocalAssetsVersion();
        var assetDir = _pathService.GetAssetsVersionPath(manifest.Version);
        Directory.CreateDirectory(assetDir);
        
        _logger.LogInformation("本地 Assets 版本: {LocalVersion}, 遠端版本: {RemoteVersion}, 強制更新: {ForceUpdate}", 
            localVersion, manifest.Version, forceUpdate);
        
        // 計算需要下載的檔案
        var filesToDownload = new List<DalamudAssetEntry>();
        foreach (var asset in manifest.Assets)
        {
            var localPath = Path.Combine(assetDir, asset.FileName);
            
            // 強制更新模式：所有檔案都重新下載
            if (forceUpdate)
            {
                filesToDownload.Add(asset);
                continue;
            }
            
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
        
        // 有整包 Package 且需要更新 → 下載整包解壓，再統一驗證
        if (!string.IsNullOrEmpty(manifest.Package) && (forceUpdate || localVersion != manifest.Version))
        {
            await foreach (var p in DownloadAssetPackageAsync(manifest, assetDir, ct))
            {
                yield return p;
            }
        }
        else
        {
        
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
        
        } // end else (per-file download)
        
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
        await ReplaceDirectoryAsync(assetDir, _pathService.AssetsDevPath, ct);
        
        _logger.LogInformation("Assets v{Version} 安裝完成", manifest.Version);
    }
    
    /// <summary>下載 Assets 整包 zip 並解壓到版本目錄</summary>
    private async IAsyncEnumerable<DalamudUpdateProgress> DownloadAssetPackageAsync(
        DalamudAssetManifest manifest, string assetDir, [EnumeratorCancellation] CancellationToken ct)
    {
        var ext = GetArchiveExtension(manifest.Package!);
        var tempFile = Path.Combine(Path.GetTempPath(), $"dalamud_assets_{manifest.Version}{ext}");
        
        try
        {
            _logger.LogInformation("下載 Assets 整包從: {Url}", manifest.Package);
            
            await foreach (var p in DownloadFileWithProgressAsync(manifest.Package!, tempFile, ct))
            {
                p.Stage = DalamudUpdateStage.DownloadingAssets;
                yield return p;
            }
            
            var extractProgress = new DalamudUpdateProgress
            {
                Stage = DalamudUpdateStage.DownloadingAssets,
                CurrentFile = "解壓 Assets..."
            };
            yield return extractProgress;
            
            // 清空目標目錄後解壓
            if (Directory.Exists(assetDir))
                Directory.Delete(assetDir, true);
            Directory.CreateDirectory(assetDir);
            
            await ExtractArchiveAsync(tempFile, assetDir, ct);
            _logger.LogInformation("Assets 整包解壓完成");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
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
    /// 根據副檔名自動選擇解壓方式 (.7z → SevenZip，其他 → ZIP)
    /// </summary>
    private Task ExtractArchiveAsync(string archivePath, string targetDir, CancellationToken ct)
    {
        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (ext == ".7z")
        {
            _logger.LogInformation("使用 7z 解壓: {Archive}", archivePath);
            return ExtractSevenZipAsync(archivePath, targetDir, ct);
        }

        _logger.LogInformation("使用 ZIP 解壓: {Archive}", archivePath);
        return ExtractZipAsync(archivePath, targetDir, ct);
    }

    /// <summary>
    /// 從 URL 取得封存副檔名，預設 .zip
    /// </summary>
    private static string GetArchiveExtension(string url)
    {
        var path = new Uri(url).AbsolutePath;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".7z" or ".zip" ? ext : ".zip";
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
    
    private async Task ReplaceDirectoryAsync(string sourcePath, string targetPath, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            // 替換所選安裝或資源目錄。
            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, true);
            
            // 複製完整套件。
            CopyDirectory(sourcePath, targetPath);
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
        // 沒有 hash 資訊時，跳過驗證視為通過
        if (string.IsNullOrEmpty(expectedHash))
            return true;
            
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

    private void CleanupOldDalamudVersions()
    {
        try
        {
            var hooksPath = _pathService.HooksPath;
            if (!Directory.Exists(hooksPath))
                return;
            if (!File.Exists(_pathService.GetInjectorPath(false)) && !File.Exists(_pathService.GetInjectorPath(true)))
                return;

            foreach (var dir in Directory.GetDirectories(hooksPath))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName == "dev" || dirName == "release" ||
                    !Regex.IsMatch(dirName, @"\Av?\d+\.\d+\.\d+\.\d+-tc\.[1-9]\d*(?:-rc\.[1-9]\d*)?\z") ||
                    !File.Exists(Path.Combine(dir, "version.json")))
                    continue;

                _logger.LogInformation("刪除舊版 Dalamud 目錄: {Version}", dirName);
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理舊版 Dalamud 目錄時發生錯誤（非致命）");
        }
    }

    private void CleanupOldAssetsVersions(int currentVersion)
    {
        try
        {
            var assetsPath = _pathService.AssetsPath;
            if (!Directory.Exists(assetsPath))
                return;

            foreach (var dir in Directory.GetDirectories(assetsPath))
            {
                var dirName = Path.GetFileName(dir);
                if (int.TryParse(dirName, out var version) && version != currentVersion)
                {
                    _logger.LogInformation("刪除舊版 Assets 目錄: v{Version}", version);
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理舊版 Assets 目錄時發生錯誤（非致命）");
        }
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
    
    #region 相容性遷移

    public void MigrateOldDalamudDirectories()
    {
        try
        {
            var userDataDir = PlatformPathService.Instance.UserDataDirectory;
            
            // 1. 遷移共享的 BasePath 內的 Dalamud 目錄
            MigrateDalamudDirectory(userDataDir);

            // 2. 遷移 profiles 底下所有 Profile 的 Dalamud 目錄
            var profilesParentDir = Path.Combine(userDataDir, "profiles");
            if (Directory.Exists(profilesParentDir))
            {
                foreach (var profileDir in Directory.GetDirectories(profilesParentDir))
                {
                    MigrateDalamudDirectory(profileDir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dalamud 相容性遷移時發生錯誤");
        }
    }

    private void MigrateDalamudDirectory(string parentDir)
    {
        if (!Directory.Exists(parentDir)) return;

        var subDirs = Directory.GetDirectories(parentDir);
        var originalDalamud = subDirs.FirstOrDefault(d => Path.GetFileName(d).Equals("Dalamud", StringComparison.Ordinal));

        if (originalDalamud != null)
        {
            var localPath = Path.Combine(parentDir, "Dalamud_local");
            
            _logger.LogInformation("發現舊的 Dalamud 目錄: {OriginalPath}，準備進行相容性遷移至: {LocalPath}", originalDalamud, localPath);

            // 如果 Dalamud_local 已經存在（可能之前遷移失敗殘留），先刪除它
            if (Directory.Exists(localPath))
            {
                Directory.Delete(localPath, true);
            }

            Directory.Move(originalDalamud, localPath);

            // 建立dalamud目錄
            var newDalamudPath = Path.Combine(parentDir, "dalamud");
            Directory.CreateDirectory(newDalamudPath);

            // 將Dalamud_local下的各個子目錄中的檔案，複製到新的預設目錄中。
            foreach (var subDir in Directory.GetDirectories(localPath))
            {
                var subDirName = Path.GetFileName(subDir);
                var targetSubDirName = ToCamelCase(subDirName);
                var targetSubDir = Path.Combine(newDalamudPath, targetSubDirName);

                CopyDirectoryRecursiveKeepNames(subDir, targetSubDir);
            }
            
            // 複製 Dalamud_local 根目錄下的檔案（如果有）
            foreach (var file in Directory.GetFiles(localPath))
            {
                var targetFile = Path.Combine(newDalamudPath, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            // 遷移完成後，刪除Dalamud_local好完成遷移。
            Directory.Delete(localPath, true);
            
            _logger.LogInformation("舊的 Dalamud 目錄遷移完成並已刪除暫存的 Dalamud_local");
        }
    }

    private string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.Equals("Plugins", StringComparison.OrdinalIgnoreCase)) return "installedPlugins";
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private void CopyDirectoryRecursiveKeepNames(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var subDirName = Path.GetFileName(subDir);
            var targetSubDir = Path.Combine(targetDir, subDirName);
            CopyDirectoryRecursiveKeepNames(subDir, targetSubDir);
        }
    }

    #endregion
    
    #endregion
}
