using System.Net.Http.Json;
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
    private DalamudUpdateProgress _currentProgress = new();
    
    public event Action<DalamudUpdateProgress>? OnProgress;
    
    public DalamudUpdater(ILogger<DalamudUpdater> logger, DalamudPathService pathService)
    {
        _logger = logger;
        _pathService = pathService;
        _httpClient = new HttpClient();
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
            
            if (status.LocalVersion == null)
            {
                status.State = DalamudState.NotInstalled;
            }
            else if (status.LocalVersion == remoteVersion.AssemblyVersion)
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
    
    /// <summary>Execute complete update</summary>
    public async Task<bool> UpdateAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentProgress = new DalamudUpdateProgress();
        
        try
        {
            _pathService.EnsureDirectoriesExist();
            
            // 1. Check version
            ReportProgress(DalamudUpdateStage.CheckingVersion, "Checking version...");
            var versionInfo = await GetRemoteVersionAsync();
            if (versionInfo == null)
            {
                throw new Exception("Failed to retrieve remote version information");
            }
            
            var localVersion = _pathService.GetLocalVersion();
            var needsUpdate = localVersion != versionInfo.AssemblyVersion;
            
            _logger.LogInformation("Dalamud version check: Local={LocalVersion}, Remote={RemoteVersion}, NeedsUpdate={NeedsUpdate}",
                localVersion ?? "Not installed", versionInfo.AssemblyVersion, needsUpdate);
            
            // 2. Download Dalamud (if needed)
            if (needsUpdate)
            {
                await DownloadDalamudAsync(versionInfo, _cts.Token);
            }
            
            // 3. Download Runtime (if needed)
            if (versionInfo.RuntimeRequired)
            {
                var localRuntime = _pathService.GetLocalRuntimeVersion();
                if (localRuntime != versionInfo.RuntimeVersion)
                {
                    await DownloadRuntimeAsync(versionInfo.RuntimeVersion, _cts.Token);
                }
                else
                {
                    _logger.LogInformation("Runtime 已是最新版本: {Version}", localRuntime);
                }
            }
            
            // 4. 下載 Assets (如需要)
            await DownloadAssetsAsync(_cts.Token);
            
            // 完成
            ReportProgress(DalamudUpdateStage.Complete, "更新完成");
            _currentProgress.IsComplete = true;
            OnProgress?.Invoke(_currentProgress);
            
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Dalamud 更新已取消");
            ReportProgress(DalamudUpdateStage.Failed, "更新已取消");
            _currentProgress.HasError = true;
            _currentProgress.ErrorMessage = "更新已取消";
            OnProgress?.Invoke(_currentProgress);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dalamud 更新失敗");
            ReportProgress(DalamudUpdateStage.Failed, ex.Message);
            _currentProgress.HasError = true;
            _currentProgress.ErrorMessage = ex.Message;
            OnProgress?.Invoke(_currentProgress);
            return false;
        }
    }
    
    /// <summary>取消更新</summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }
    
    /// <summary>取得當前進度</summary>
    public DalamudUpdateProgress GetProgress() => _currentProgress;
    
    #region Private Methods
    
    private async Task DownloadDalamudAsync(DalamudVersionInfo versionInfo, CancellationToken ct)
    {
        ReportProgress(DalamudUpdateStage.DownloadingDalamud, "下載 Dalamud...");
        
        var tempFile = Path.Combine(Path.GetTempPath(), $"dalamud_{versionInfo.AssemblyVersion}.7z");
        var targetDir = _pathService.GetHooksVersionPath(versionInfo.AssemblyVersion);
        
        try
        {
            // 下載
            _logger.LogInformation("下載 Dalamud 從: {Url}", versionInfo.DownloadUrl);
            await DownloadFileWithProgressAsync(versionInfo.DownloadUrl, tempFile, ct);
            
            // 解壓
            ReportProgress(DalamudUpdateStage.ExtractingDalamud, "解壓 Dalamud...");
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
    
    private async Task DownloadRuntimeAsync(string version, CancellationToken ct)
    {
        ReportProgress(DalamudUpdateStage.DownloadingRuntime, $"下載 .NET Runtime {version}...");
        
        // 設定總共需要下載 2 個檔案
        _currentProgress.TotalItems = 2;
        _currentProgress.CompletedItems = 0;
        
        var tempDir = Path.Combine(Path.GetTempPath(), $"dotnet_runtime_{version}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            // 下載 dotnet-runtime (1/2)
            var dotnetUrl = string.Format(DotnetRuntimeUrl, version);
            var dotnetFile = Path.Combine(tempDir, "dotnet-runtime.zip");
            _logger.LogInformation("下載 .NET Runtime 從: {Url}", dotnetUrl);
            _currentProgress.CurrentFile = "dotnet-runtime.zip";
            await DownloadFileWithProgressAsync(dotnetUrl, dotnetFile, ct);
            _currentProgress.CompletedItems = 1;
            OnProgress?.Invoke(_currentProgress);
            
            // 下載 windowsdesktop-runtime (2/2)
            var desktopUrl = string.Format(DesktopRuntimeUrl, version);
            var desktopFile = Path.Combine(tempDir, "windowsdesktop-runtime.zip");
            _logger.LogInformation("下載 Windows Desktop Runtime 從: {Url}", desktopUrl);
            _currentProgress.CurrentFile = "windowsdesktop-runtime.zip";
            await DownloadFileWithProgressAsync(desktopUrl, desktopFile, ct);
            _currentProgress.CompletedItems = 2;
            OnProgress?.Invoke(_currentProgress);
            
            // 解壓
            ReportProgress(DalamudUpdateStage.ExtractingRuntime, "解壓 Runtime...");
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
    
    private async Task DownloadAssetsAsync(CancellationToken ct)
    {
        ReportProgress(DalamudUpdateStage.DownloadingAssets, "檢查 Assets...");
        
        var manifest = await GetAssetManifestAsync();
        if (manifest == null)
        {
            throw new Exception("無法取得 Asset 清單");
        }
        
        var localVersion = _pathService.GetLocalAssetsVersion();
        var needsFullDownload = localVersion != manifest.Version;
        
        var assetDir = _pathService.GetAssetsVersionPath(manifest.Version);
        Directory.CreateDirectory(assetDir);
        
        // 計算需要下載的檔案
        var filesToDownload = new List<DalamudAssetEntry>();
        foreach (var asset in manifest.Assets)
        {
            var localPath = Path.Combine(assetDir, asset.FileName);
            if (!File.Exists(localPath) || !await VerifyFileHashAsync(localPath, asset.Hash))
            {
                filesToDownload.Add(asset);
            }
        }
        
        if (filesToDownload.Count == 0)
        {
            _logger.LogInformation("Assets 已是最新版本");
            return;
        }
        
        _logger.LogInformation("需要下載 {Count} 個 Asset 檔案", filesToDownload.Count);
        
        _currentProgress.TotalItems = filesToDownload.Count;
        _currentProgress.CompletedItems = 0;
        
        // 並行下載 (最多 5 個)
        var semaphore = new SemaphoreSlim(5);
        var tasks = filesToDownload.Select(async asset =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var localPath = Path.Combine(assetDir, asset.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                
                // 使用 jsDelivr CDN 加速
                var url = ConvertToJsDelivr(asset.Url);
                await DownloadFileSimpleAsync(url, localPath, ct);
                
                // 驗證
                if (!await VerifyFileHashAsync(localPath, asset.Hash))
                {
                    _logger.LogWarning("Asset 驗證失敗: {FileName}", asset.FileName);
                    File.Delete(localPath);
                    throw new Exception($"Asset 驗證失敗: {asset.FileName}");
                }
                
                _currentProgress.IncrementCompleted();
                _currentProgress.CurrentFile = asset.FileName;
                OnProgress?.Invoke(_currentProgress);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        
        // 保存版本
        await File.WriteAllTextAsync(_pathService.AssetsVersionFile, manifest.Version.ToString(), ct);
        
        // 更新 dev 目錄
        await UpdateDevLinkAsync(assetDir, _pathService.AssetsDevPath, ct);
        
        _logger.LogInformation("Assets v{Version} 安裝完成", manifest.Version);
    }
    
    /// <summary>下載檔案並追蹤進度 (用於單一大檔案下載)</summary>
    private async Task DownloadFileWithProgressAsync(string url, string targetPath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        _currentProgress.TotalBytes = totalBytes;
        _currentProgress.BytesDownloaded = 0;
        
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        
        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            _currentProgress.BytesDownloaded += bytesRead;
            OnProgress?.Invoke(_currentProgress);
        }
    }
    
    /// <summary>下載檔案 (不追蹤 bytes 進度，用於並行下載小檔案)</summary>
    private async Task DownloadFileSimpleAsync(string url, string targetPath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream, ct);
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
    
    private void ReportProgress(DalamudUpdateStage stage, string? currentFile = null, bool resetProgress = true)
    {
        _currentProgress.Stage = stage;
        _currentProgress.CurrentFile = currentFile;
        
        // 切換階段時重置進度數值
        if (resetProgress)
        {
            _currentProgress.TotalBytes = 0;
            _currentProgress.BytesDownloaded = 0;
            _currentProgress.TotalItems = 0;
            _currentProgress.CompletedItems = 0;
        }
        
        OnProgress?.Invoke(_currentProgress);
    }
    
    #endregion
}
