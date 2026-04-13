using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform.MacOS.Wine;

namespace XIVTheCalamity.Platform.MacOS.Discord;

public record DiscordRpcBridgeStatus(
    bool Supported,
    bool Enabled,
    bool AutoInstall,
    bool PrefixBridgeInstalled,
    string BridgeVersion,
    string? LastError = null
);

public record DiscordRpcEnsureResult(bool Success, string Message, DiscordRpcBridgeStatus Status);

/// <summary>
/// Manages rpc-bridge setup for macOS Wine prefixes.
/// </summary>
public sealed class DiscordRpcBridgeService(
    ConfigService configService,
    ILogger<DiscordRpcBridgeService>? logger = null)
{
    private const string BridgeZipUrl = "https://github.com/EnderIce2/rpc-bridge/releases/latest/download/bridge.zip";

    private readonly PlatformPathService _paths = PlatformPathService.Instance;
    private readonly WinePathService? _winePaths = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? WinePathService.Instance
        : null;

    private string BridgeRoot => Path.Combine(_paths.AppDataDirectory, "discord-rpc");
    private string BridgeZipPath => Path.Combine(BridgeRoot, "bridge.zip");
    private string BridgeExePath => Path.Combine(BridgeRoot, "bridge.exe");
    private string BridgeVersionPath => Path.Combine(BridgeRoot, ".bridge-version");
    private string PrefixBridgeExePath => Path.Combine(_winePaths?.WinePrefix ?? string.Empty, "drive_c", "windows", "bridge.exe");
    private string TmpLinkPath => "/tmp/rpc-bridge/tmpdir";

    public async Task<DiscordRpcBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var config = await configService.LoadConfigAsync();
        var supported = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && _winePaths is not null;

        return new DiscordRpcBridgeStatus(
            Supported: supported,
            Enabled: config.DiscordRpc.Enabled,
            AutoInstall: config.DiscordRpc.AutoInstall,
            PrefixBridgeInstalled: supported && File.Exists(PrefixBridgeExePath),
            BridgeVersion: config.DiscordRpc.BridgeVersion
        );
    }

    public async Task<DiscordRpcEnsureResult> EnsureReadyAsync(bool forceInstall, CancellationToken cancellationToken = default)
    {
        var config = await configService.LoadConfigAsync();
        var status = await GetStatusAsync(cancellationToken);
        if (!status.Supported)
        {
            return new DiscordRpcEnsureResult(false, "Discord RPC bridge is only supported on macOS Wine.", status);
        }

        if (!config.DiscordRpc.Enabled)
        {
            return new DiscordRpcEnsureResult(true, "Discord RPC bridge is disabled.", status);
        }

        if (!config.DiscordRpc.AutoInstall && !forceInstall && !status.PrefixBridgeInstalled)
        {
            return new DiscordRpcEnsureResult(false, "Discord RPC bridge is not installed in prefix and auto install is disabled.", status);
        }

        try
        {
            Directory.CreateDirectory(BridgeRoot);

            if (!File.Exists(BridgeExePath) || forceInstall)
            {
                await DownloadAndExtractBridgeAsync(cancellationToken);
                File.WriteAllText(BridgeVersionPath, config.DiscordRpc.BridgeVersion);
            }

            EnsureTmpDirSymlink();

            if (forceInstall)
            {
                await ReinstallBridgeInPrefixAsync(cancellationToken);
            }
            else if (!File.Exists(PrefixBridgeExePath))
            {
                await InstallBridgeInPrefixAsync(cancellationToken);
            }

            status = await GetStatusAsync(cancellationToken);
            return new DiscordRpcEnsureResult(true, "Discord RPC bridge is ready.", status);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[DISCORD-RPC] Failed to ensure bridge readiness");
            status = (await GetStatusAsync(cancellationToken)) with { LastError = ex.Message };
            return new DiscordRpcEnsureResult(false, ex.Message, status);
        }
    }

    public async Task EnsureTmpDirSymlinkReadyAsync(CancellationToken cancellationToken = default)
    {
        var config = await configService.LoadConfigAsync();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || _winePaths is null || !config.DiscordRpc.Enabled)
        {
            return;
        }

        try
        {
            EnsureTmpDirSymlink();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[DISCORD-RPC] Failed to refresh tmpdir symlink");
        }
    }

    public async Task CleanupTmpDirSymlinkAsync(CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || _winePaths is null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(TmpLinkPath))
            {
                var dirInfo = new DirectoryInfo(TmpLinkPath);
                if (dirInfo.LinkTarget != null)
                {
                    Directory.Delete(TmpLinkPath, recursive: false);
                    logger?.LogInformation("[DISCORD-RPC] Removed tmpdir symlink: {Link}", TmpLinkPath);
                }
                else
                {
                    logger?.LogWarning("[DISCORD-RPC] Skip cleanup because path is not a symlink: {Link}", TmpLinkPath);
                }
            }
            else if (File.Exists(TmpLinkPath))
            {
                File.Delete(TmpLinkPath);
                logger?.LogInformation("[DISCORD-RPC] Removed tmpdir link file: {Link}", TmpLinkPath);
            }

            var parentDir = Path.GetDirectoryName(TmpLinkPath);
            if (!string.IsNullOrWhiteSpace(parentDir) && Directory.Exists(parentDir) &&
                !Directory.EnumerateFileSystemEntries(parentDir).Any())
            {
                Directory.Delete(parentDir);
                logger?.LogInformation("[DISCORD-RPC] Removed empty tmpdir parent directory: {Dir}", parentDir);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[DISCORD-RPC] Failed to cleanup tmpdir symlink");
        }
    }

    private async Task DownloadAndExtractBridgeAsync(CancellationToken cancellationToken)
    {
        logger?.LogInformation("[DISCORD-RPC] Downloading rpc-bridge from {Url}", BridgeZipUrl);

        using var httpClient = new HttpClient();
        await using (var stream = await httpClient.GetStreamAsync(BridgeZipUrl, cancellationToken))
        await using (var output = File.Create(BridgeZipPath))
        {
            await stream.CopyToAsync(output, cancellationToken);
        }

        var extractDir = Path.Combine(BridgeRoot, "extract");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        ZipFile.ExtractToDirectory(BridgeZipPath, extractDir, overwriteFiles: true);

        var extractedBridgeExe = Directory.GetFiles(extractDir, "bridge.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(extractedBridgeExe))
        {
            throw new InvalidOperationException("bridge.exe not found in rpc-bridge package.");
        }

        File.Copy(extractedBridgeExe, BridgeExePath, overwrite: true);
        File.Delete(BridgeZipPath);
        Directory.Delete(extractDir, true);
    }

    private void EnsureTmpDirSymlink()
    {
        var currentTmpDir = Environment.GetEnvironmentVariable("TMPDIR");
        if (string.IsNullOrWhiteSpace(currentTmpDir))
        {
            throw new InvalidOperationException("TMPDIR is not available for Discord RPC bridge setup.");
        }

        currentTmpDir = currentTmpDir.TrimEnd('/');
        Directory.CreateDirectory(Path.GetDirectoryName(TmpLinkPath)!);

        if (Directory.Exists(TmpLinkPath) || File.Exists(TmpLinkPath))
        {
            try
            {
                var existing = new DirectoryInfo(TmpLinkPath);
                var target = existing.ResolveLinkTarget(returnFinalTarget: false);
                if (target != null && Path.GetFullPath(target.FullName) == Path.GetFullPath(currentTmpDir))
                {
                    return;
                }
            }
            catch
            {
                // Recreate below.
            }

            Directory.Delete(TmpLinkPath, true);
        }

        Directory.CreateSymbolicLink(TmpLinkPath, currentTmpDir);
        logger?.LogInformation("[DISCORD-RPC] Linked {Link} -> {Target}", TmpLinkPath, currentTmpDir);
    }

    private async Task InstallBridgeInPrefixAsync(CancellationToken cancellationToken)
    {
        if (_winePaths == null)
        {
            throw new PlatformNotSupportedException("Wine path service is unavailable.");
        }

        if (!File.Exists(_winePaths.Wine))
        {
            throw new FileNotFoundException("Wine executable is missing.", _winePaths.Wine);
        }

        if (!File.Exists(BridgeExePath))
        {
            throw new FileNotFoundException("bridge.exe is missing.", BridgeExePath);
        }

        var wineBridgePath = $"Z:{BridgeExePath.Replace("/", "\\")}";
        var result = await RunProcessAsync(
            _winePaths.Wine,
            $"\"{wineBridgePath}\" --install",
            cancellationToken,
            extraEnv: _winePaths.GetEnvironment());

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"rpc-bridge install failed: {result.StandardError}");
        }

        if (!File.Exists(PrefixBridgeExePath))
        {
            throw new InvalidOperationException("rpc-bridge install completed but C:\\windows\\bridge.exe is missing in prefix.");
        }
    }

    private async Task ReinstallBridgeInPrefixAsync(CancellationToken cancellationToken)
    {
        if (_winePaths == null)
        {
            throw new PlatformNotSupportedException("Wine path service is unavailable.");
        }

        var wineBridgePath = $"Z:{BridgeExePath.Replace("/", "\\")}";
        await RunProcessAsync(
            _winePaths.Wine,
            $"\"{wineBridgePath}\" --uninstall",
            cancellationToken,
            extraEnv: _winePaths.GetEnvironment(),
            ignoreNonZeroExitCode: true);

        await InstallBridgeInPrefixAsync(cancellationToken);
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken,
        Dictionary<string, string>? extraEnv = null,
        bool ignoreNonZeroExitCode = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (extraEnv != null)
        {
            foreach (var (key, value) in extraEnv)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 && !ignoreNonZeroExitCode)
        {
            logger?.LogWarning("[DISCORD-RPC] Process failed: {FileName} {Args} (exit {Code})\n{Stderr}",
                fileName, arguments, process.ExitCode, stderr);
        }

        return (process.ExitCode, stdout, stderr);
    }
}
