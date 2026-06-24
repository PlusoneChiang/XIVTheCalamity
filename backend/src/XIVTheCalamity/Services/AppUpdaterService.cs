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
    private readonly ConfigService _configService;
    private readonly EventBroadcastHub _eventHub;
    private readonly IHttpClientFactory _httpClientFactory;
    private string? _downloadedFilePath;
    private readonly object _lock = new();

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
        try
        {
            var config = await _configService.LoadConfigAsync();
            var enablePreRelease = config.Launcher?.EnablePreRelease ?? false;

            Log.Information("[AppUpdater] Checking for updates. EnablePreRelease: {EnablePreRelease}", enablePreRelease);

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (XIVTheCalamity/1.0)");
            
            var response = await client.GetAsync("https://api.github.com/repos/PlusoneChiang/XIVTheCalamity/releases");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var releases = JsonSerializer.Deserialize(json, AppJsonContext.Default.GitHubReleaseArray);

            if (releases == null || releases.Length == 0)
            {
                Log.Information("[AppUpdater] No releases found on GitHub");
                return new AppUpdateCheckResult(false, "", "", "", 0);
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
                return new AppUpdateCheckResult(false, "", "", "", 0);
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
                    return new AppUpdateCheckResult(
                        UpdateAvailable: true,
                        Version: selectedRelease.Tag_name,
                        ReleaseNotes: selectedRelease.Body,
                        DownloadUrl: asset.Browser_download_url,
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

        return new AppUpdateCheckResult(false, "", "", "", 0);
    }

    private GitHubAsset? FindTargetAsset(GitHubRelease release)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Array.Find(release.Assets, a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
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

    public async Task DownloadUpdateAsync(string downloadUrl)
    {
        if (string.IsNullOrEmpty(downloadUrl))
        {
            throw new ArgumentException("Download URL cannot be empty");
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
open ""{currentAppPath}""
rm -rf ""{tempExtractPath}""
rm -- ""$0""
";
                            File.WriteAllText(scriptPath, scriptContent);
                            Process.Start("chmod", $"+x \"{scriptPath}\"")?.WaitForExit();
                            Process.Start("/bin/bash", $"\"{scriptPath}\"");
                            Environment.Exit(0);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[AppUpdater] Failed silent OSX zip update, falling back to open");
                    }
                }
                
                Process.Start("open", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // 給予執行權限並啟動
                Process.Start("chmod", $"+x \"{path}\"")?.WaitForExit();
                Process.Start(path);
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
