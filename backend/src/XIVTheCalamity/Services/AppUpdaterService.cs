using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Services;

public class AppUpdaterService
{
    private const string ReleasesUrl = "https://api.github.com/repos/PlusoneChiang/XIVTheCalamity/releases";
    private const string DownloadBaseUrl = "https://github.com/PlusoneChiang/XIVTheCalamity/releases/download/";
    private string? _availableDownloadUrl;
    private readonly ConfigService _configService;
    private readonly EventBroadcastHub _eventHub;
    private readonly IHttpClientFactory _httpClientFactory;
    private string? _downloadedFilePath;
    private readonly object _lock = new();
    private int _linuxUpdatePreparing;

    public AppUpdaterService(
        ConfigService configService,
        EventBroadcastHub eventHub,
        IHttpClientFactory httpClientFactory)
    {
        _configService = configService;
        _eventHub = eventHub;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync()
    {
        lock (_lock) { _availableDownloadUrl = null; }
        try
        {
            var config = await _configService.LoadConfigAsync();
            var enablePreRelease = config.Launcher?.EnablePreRelease ?? false;

            Log.Information("[AppUpdater] Checking for updates. EnablePreRelease: {EnablePreRelease}", enablePreRelease);

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (XIVTheCalamity/1.0)");
            
            var response = await client.GetAsync(ReleasesUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var releases = JsonSerializer.Deserialize(json, AppJsonContext.Default.GitHubReleaseArray);

            if (releases == null || releases.Length == 0)
            {
                Log.Information("[AppUpdater] No releases found on GitHub");
                return new AppUpdateCheckResult(false, "", "", 0);
            }

            GitHubRelease? selectedRelease = null;

            if (enablePreRelease)
            {
                // 接收測試通道：取第一個發布（可能是 Pre-Release 或正式版）
                selectedRelease = releases[0];
            }
            else
            {
                // 只接收正式版：找尋第一個 prerelease == false 的項目
                foreach (var rel in releases)
                {
                    if (!rel.Prerelease)
                    {
                        selectedRelease = rel;
                        break;
                    }
                }
            }

            if (selectedRelease == null)
            {
                Log.Information("[AppUpdater] No matching release found");
                return new AppUpdateCheckResult(false, "", "", 0);
            }

            // 取得目前的 Informational Version
            var currentVersionStr = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "2.0.0-beta";

            // 移除 Git commit metadata (例如 2.0.0-beta+abcdef)
            if (currentVersionStr.Contains('+'))
            {
                currentVersionStr = currentVersionStr.Split('+')[0];
            }

            var currentVersion = LauncherVersion.Parse(currentVersionStr);
            var releaseVersion = LauncherVersion.Parse(selectedRelease.Tag_name);

            Log.Information("[AppUpdater] Current version: {CurrentVersion}, Release version: {ReleaseVersion} ({Tag})", 
                currentVersionStr, selectedRelease.Tag_name, selectedRelease.Prerelease ? "Pre-Release" : "Stable");

            if (releaseVersion.CompareTo(currentVersion) > 0)
            {
                // 有新版本，尋找適合目前平台的 asset
                var asset = FindTargetAsset(selectedRelease);
                if (asset != null)
                {
                    // 下載位置只由固定儲存庫及後端取得的 Release 資料決定。
                    var downloadUrl = DownloadBaseUrl + Uri.EscapeDataString(selectedRelease.Tag_name)
                        + "/" + Uri.EscapeDataString(asset.Name);
                    lock (_lock) { _availableDownloadUrl = downloadUrl; }
                    return new AppUpdateCheckResult(
                        UpdateAvailable: true,
                        Version: selectedRelease.Tag_name,
                        ReleaseNotes: selectedRelease.Body,
                        Size: asset.Size
                    );
                }
                else
                {
                    Log.Warning("[AppUpdater] Newer version found but no matching asset for current platform");
                }
            }
            else
            {
                Log.Information("[AppUpdater] Launcher is up to date");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AppUpdater] Failed to check for updates");
        }

        return new AppUpdateCheckResult(false, "", "", 0);
    }

    private GitHubAsset? FindTargetAsset(GitHubRelease release)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Array.Find(release.Assets, a => a.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                ?? Array.Find(release.Assets, a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? Array.Find(release.Assets, a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Array.Find(release.Assets, a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ?? Array.Find(release.Assets, a => a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                ?? Array.Find(release.Assets, a => a.Name.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Array.Find(release.Assets, a => a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public async Task DownloadUpdateAsync()
    {
        string downloadUrl;
        lock (_lock)
        {
            downloadUrl = _availableDownloadUrl
                ?? throw new InvalidOperationException("請先檢查更新，確認有可下載的版本。");
        }

        try
        {
            Log.Information("[AppUpdater] Starting download from: {Url}", downloadUrl);

            var uri = new Uri(downloadUrl);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "XIVTheCalamity-Update";
            }

            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (XIVTheCalamity/1.0)");

            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            var totalRead = 0L;
            var bytesRead = 0;
            var lastReportTime = DateTime.UtcNow;
            var bytesReadSinceReport = 0L;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) != 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                bytesReadSinceReport += bytesRead;

                var now = DateTime.UtcNow;
                var elapsed = (now - lastReportTime).TotalSeconds;

                if (elapsed >= 0.5)
                {
                    double percent = totalBytes > 0 ? ((double)totalRead / totalBytes) * 100.0 : 0.0;
                    long bytesPerSecond = (long)(bytesReadSinceReport / elapsed);

                    // 廣播下載進度至前端
                    BroadcastProgress(percent, bytesPerSecond);

                    lastReportTime = now;
                    bytesReadSinceReport = 0L;
                }
            }

            // 完成下載，報告 100%
            BroadcastProgress(100.0, 0L);

            lock (_lock)
            {
                _downloadedFilePath = tempPath;
            }

            Log.Information("[AppUpdater] Download complete. Saved to: {Path}", tempPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AppUpdater] Failed to download update");
            throw;
        }
    }

    private void BroadcastProgress(double percent, long bytesPerSecond)
    {
        try
        {
            var progress = new AppUpdateProgress(percent, bytesPerSecond);
            var progressElement = JsonSerializer.SerializeToElement(progress, AppJsonContext.Default.AppUpdateProgress);
            _eventHub.Broadcast("app-update:download-progress", progressElement);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppUpdater] Failed to broadcast download progress");
        }
    }

    public void InstallUpdate()
    {
        string? path;
        lock (_lock)
        {
            path = _downloadedFilePath;
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Downloaded update file not found");
        }

        Log.Information("[AppUpdater] Executing installer: {Path}", path);

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    InstallWindowsZip(path);
                    Environment.Exit(0);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var tempExtractPath = Path.Combine(Path.GetTempPath(), "xivtc-update-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempExtractPath);
                        
                        System.IO.Compression.ZipFile.ExtractToDirectory(path, tempExtractPath);
                        
                        var newAppFolder = Path.Combine(tempExtractPath, "XIVTheCalamity.app");
                        var currentAppPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
                        
                        if (Directory.Exists(newAppFolder) && currentAppPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                        {
                            var scriptPath = Path.Combine(Path.GetTempPath(), "update-mac.sh");
                            var scriptContent = $@"#!/bin/bash
sleep 1
rm -rf ""{currentAppPath}""
mv ""{newAppFolder}"" ""{currentAppPath}""
# 使用 env -i 呼叫 open，通知 macOS LaunchServices 以全新的系統預設會話啟動 App
env -i open ""{currentAppPath}""
rm -rf ""{tempExtractPath}""
rm -- ""$0""
";
                            File.WriteAllText(scriptPath, scriptContent);
                            Process.Start("chmod", $"+x \"{scriptPath}\"")?.WaitForExit();
                            
                            // 用 env -i 執行 bash 腳本，使其以完全乾淨且不繼承的環境變數執行
                            Process.Start("env", $"-i /bin/bash \"{scriptPath}\"");
                            Environment.Exit(0);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[AppUpdater] Failed silent OSX zip update, falling back to open");
                    }
                }
                
                // 用 env -i 呼叫 open 啟動更新安裝檔
                Process.Start("env", $"-i open \"{path}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (Interlocked.CompareExchange(ref _linuxUpdatePreparing, 1, 0) != 0)
                    throw new InvalidOperationException("Linux 更新程序已啟動。");
                try
                {
                    PrepareLinuxUpdate(path);
                }
                catch
                {
                    Interlocked.Exchange(ref _linuxUpdatePreparing, 0);
                    throw;
                }
            }

            // 關閉目前的主程式
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AppUpdater] Failed to launch installer/update");
            throw;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void PrepareLinuxUpdate(string downloadedPath)
    {
        var target = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathFullyQualified(target) || !File.Exists(target))
            throw new InvalidOperationException("無法確認原始 AppImage 路徑，已取消更新；目前程式不會關閉。");

        target = File.ResolveLinkTarget(target, returnFinalTarget: true)?.FullName ?? target;
        var updateDirectory = Path.Combine(Path.GetDirectoryName(target)!, ".xivtc-update-" + Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(PlatformPathService.Instance.LogsDirectory, "app-update-linux.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        // 關閉主程式前確認日誌可寫，避免外部指令稿失敗卻沒有紀錄。
        using (var log = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { }

        Directory.CreateDirectory(updateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var stagedPath = Path.Combine(updateDirectory, "new.AppImage");
            using (var source = File.OpenRead(downloadedPath))
            using (var staged = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (source.Length == 0)
                    throw new InvalidDataException("下載的 AppImage 是空檔案，已取消更新。");
                source.CopyTo(staged);
                staged.Flush(flushToDisk: true);
            }
            var permissions = File.GetUnixFileMode(target) & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(stagedPath, permissions | UnixFileMode.UserExecute);

            var scriptPath = Path.Combine(updateDirectory, "update.sh");
            using (var script = typeof(AppUpdaterService).Assembly.GetManifestResourceStream("XIVTheCalamity.update-linux.sh")
                ?? throw new InvalidOperationException("找不到 Linux 更新指令稿。"))
            using (var output = File.Create(scriptPath))
                script.CopyTo(output);

            var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
            foreach (var argument in new[] { scriptPath, Environment.ProcessId.ToString(), stagedPath, target, logPath, downloadedPath })
                start.ArgumentList.Add(argument);
            Log.Information("[AppUpdater] Prepared Linux update: {Source} -> {Target}; PID: {Pid}; log: {Log}",
                downloadedPath, target, Environment.ProcessId, logPath);
            using var updater = Process.Start(start) ?? throw new InvalidOperationException("無法啟動 Linux 更新程序。");
        }
        catch
        {
            try { Directory.Delete(updateDirectory, recursive: true); }
            catch (Exception cleanupError) { Log.Warning(cleanupError, "[AppUpdater] Failed to remove staging directory: {Path}", updateDirectory); }
            throw;
        }
    }

    private static void InstallWindowsZip(string archivePath)
    {
        var extractPath = Path.Combine(Path.GetTempPath(), "xivtc-update-" + Guid.NewGuid().ToString("N"));
        System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractPath);

        var sourcePath = Path.Combine(extractPath, "XIVTheCalamity");
        var sourceExecutable = Path.Combine(sourcePath, "XIVTheCalamity.exe");
        if (!File.Exists(sourceExecutable))
        {
            Directory.Delete(extractPath, true);
            throw new InvalidDataException("Windows update archive does not contain XIVTheCalamity.exe");
        }

        var destinationPath = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var executablePath = Environment.ProcessPath ?? Path.Combine(destinationPath, "XIVTheCalamity.exe");
        var scriptPath = Path.Combine(Path.GetTempPath(), "xivtc-update-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(scriptPath, """
param([int]$LauncherPid, [string]$Source, [string]$Destination, [string]$Executable, [string]$ExtractRoot)
$ErrorActionPreference = 'Stop'
Wait-Process -Id $LauncherPid -ErrorAction SilentlyContinue
try {
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
    Start-Process -FilePath $Executable -WorkingDirectory $Destination
    Remove-Item -LiteralPath $ExtractRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
} catch {
    $_ | Out-File -FilePath (Join-Path $env:TEMP 'xivtc-update-error.log') -Encoding utf8
    if (Test-Path -LiteralPath $Executable) {
        Start-Process -FilePath $Executable -WorkingDirectory $Destination
    }
    exit 1
}
""");

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add(destinationPath);
        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add(extractPath);

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Windows update process");
    }
}

public class LauncherVersion : IComparable<LauncherVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Feature { get; }
    public string Suffix { get; } = "";
    public int Build { get; }

    public LauncherVersion(int major, int minor, int feature, string suffix = "", int build = 0)
    {
        Major = major;
        Minor = minor;
        Feature = feature;
        Suffix = suffix.ToLowerInvariant();
        Build = build;
    }

    public static LauncherVersion Parse(string versionStr)
    {
        versionStr = versionStr.TrimStart('v', 'V', ' ');

        var parts = versionStr.Split('-');
        var mainPart = parts[0];
        var suffixPart = parts.Length > 1 ? parts[1] : "";

        var mainNums = mainPart.Split('.');
        int major = mainNums.Length > 0 && int.TryParse(mainNums[0], out var maj) ? maj : 0;
        int minor = mainNums.Length > 1 && int.TryParse(mainNums[1], out var min) ? min : 0;
        int feature = mainNums.Length > 2 && int.TryParse(mainNums[2], out var feat) ? feat : 0;

        string suffix = "";
        int build = 0;

        if (!string.IsNullOrEmpty(suffixPart))
        {
            var suffixNums = suffixPart.Split('.');
            suffix = suffixNums[0];
            if (suffixNums.Length > 1 && int.TryParse(suffixNums[1], out var bld))
            {
                build = bld;
            }
        }

        return new LauncherVersion(major, minor, feature, suffix, build);
    }

    public int CompareTo(LauncherVersion? other)
    {
        if (other == null) return 1;

        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Feature != other.Feature) return Feature.CompareTo(other.Feature);

        int thisSuffixRank = GetSuffixRank(Suffix);
        int otherSuffixRank = GetSuffixRank(other.Suffix);

        if (thisSuffixRank != otherSuffixRank)
        {
            return thisSuffixRank.CompareTo(otherSuffixRank);
        }

        return Build.CompareTo(other.Build);
    }

    private static int GetSuffixRank(string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return 4; // Stable
        if (suffix == "pre") return 3;
        if (suffix == "beta") return 2;
        if (suffix == "alpha") return 1;
        return 0;
    }
}
