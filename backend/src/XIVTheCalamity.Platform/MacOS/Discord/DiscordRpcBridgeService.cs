using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform.MacOS.Wine;

namespace XIVTheCalamity.Platform.MacOS.Discord;

public record DiscordRpcBridgeStatus(
    bool Supported,
    bool PrefixBridgeInstalled,
    string? LastError = null
);

public record DiscordRpcEnsureResult(bool Success, string Message, DiscordRpcBridgeStatus Status);
public record DiscordRpcRemoveResult(bool Success, string Message, DiscordRpcBridgeStatus Status);

/// <summary>
/// Manages xbridge setup for macOS Wine prefixes.
/// </summary>
public sealed class DiscordRpcBridgeService(ILogger<DiscordRpcBridgeService>? logger = null)
{
    private const string XBridgeDownloadUrl = "https://github.com/PlusoneChiang/xbridge/releases/latest/download/xbridge.exe";

    private readonly PlatformPathService _paths = PlatformPathService.Instance;
    private readonly WinePathService? _winePaths = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? WinePathService.Instance
        : null;

    private string BridgeRoot => Path.Combine(_paths.AppDataDirectory, "discord-rpc");
    private string XBridgeLocalPath => Path.Combine(BridgeRoot, "xbridge.exe");
    private string PrefixXBridgeExePath => Path.Combine(_winePaths?.WinePrefix ?? string.Empty, "drive_c", "windows", "xbridge.exe");

    public Task<DiscordRpcBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var supported = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && _winePaths is not null;
        return Task.FromResult(new DiscordRpcBridgeStatus(
            Supported: supported,
            PrefixBridgeInstalled: supported && File.Exists(PrefixXBridgeExePath)));
    }

    /// <summary>
    /// If xbridge is installed, refreshes DISCORD_IPC_PATH in the Wine registry to match
    /// the current host TMPDIR. Call this before each game launch so the value stays current
    /// even if the user logged out and back in (which can change TMPDIR on macOS).
    /// </summary>
    public async Task RefreshIpcPathIfInstalledAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.Supported || !status.PrefixBridgeInstalled) return;
        await WriteDiscordIpcPathToRegistryAsync(cancellationToken);
    }

    public async Task<DiscordRpcEnsureResult> EnsureReadyAsync(bool forceInstall, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.Supported)
            return new DiscordRpcEnsureResult(false, "xbridge is only supported on macOS Wine.", status);

        try
        {
            Directory.CreateDirectory(BridgeRoot);

            if (!File.Exists(XBridgeLocalPath) || forceInstall)
                await DownloadXBridgeAsync(cancellationToken);

            await InstallXBridgeInPrefixAsync(forceInstall, cancellationToken);

            status = await GetStatusAsync(cancellationToken);
            return new DiscordRpcEnsureResult(true, "xbridge is ready.", status);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[XBRIDGE] Failed to ensure xbridge readiness");
            status = (await GetStatusAsync(cancellationToken)) with { LastError = ex.Message };
            return new DiscordRpcEnsureResult(false, ex.Message, status);
        }
    }

    public async Task<DiscordRpcRemoveResult> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.Supported)
            return new DiscordRpcRemoveResult(false, "xbridge is only supported on macOS Wine.", status);

        try
        {
            await UninstallXBridgeFromPrefixAsync(cancellationToken);

            if (File.Exists(PrefixXBridgeExePath))
                throw new InvalidOperationException("xbridge.exe still exists in C:\\windows after uninstall.");

            status = await GetStatusAsync(cancellationToken);
            return new DiscordRpcRemoveResult(true, "xbridge removed.", status);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[XBRIDGE] Failed to remove xbridge");
            status = (await GetStatusAsync(cancellationToken)) with { LastError = ex.Message };
            return new DiscordRpcRemoveResult(false, ex.Message, status);
        }
    }

    private async Task DownloadXBridgeAsync(CancellationToken cancellationToken)
    {
        logger?.LogInformation("[XBRIDGE] Downloading xbridge from {Url}", XBridgeDownloadUrl);
        using var httpClient = new HttpClient();
        await using var responseStream = await httpClient.GetStreamAsync(XBridgeDownloadUrl, cancellationToken);
        await using var output = File.Create(XBridgeLocalPath);
        await responseStream.CopyToAsync(output, cancellationToken);
        logger?.LogInformation("[XBRIDGE] xbridge downloaded to {Path}", XBridgeLocalPath);
    }

    private async Task InstallXBridgeInPrefixAsync(bool forceReinstall, CancellationToken cancellationToken)
    {
        if (_winePaths == null)
            throw new PlatformNotSupportedException("Wine path service is unavailable.");
        if (!File.Exists(_winePaths.Wine))
            throw new FileNotFoundException("Wine executable is missing.", _winePaths.Wine);
        if (!File.Exists(XBridgeLocalPath))
            throw new FileNotFoundException("xbridge.exe is missing.", XBridgeLocalPath);

        if (forceReinstall && File.Exists(PrefixXBridgeExePath))
        {
            var wineUninstallPath = $"Z:{PrefixXBridgeExePath.Replace("/", "\\")}";
            await RunWineProcessAsync($"\"{wineUninstallPath}\" --uninstall", cancellationToken, ignoreNonZeroExitCode: true);
        }

        var wineInstallPath = $"Z:{XBridgeLocalPath.Replace("/", "\\")}";
        var result = await RunWineProcessAsync($"\"{wineInstallPath}\" --install", cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"xbridge --install failed: {result.StandardError}");

        if (!File.Exists(PrefixXBridgeExePath))
            throw new InvalidOperationException("xbridge install completed but C:\\windows\\xbridge.exe is missing in prefix.");

        await WriteDiscordIpcPathToRegistryAsync(cancellationToken);
    }

    private async Task WriteDiscordIpcPathToRegistryAsync(CancellationToken cancellationToken)
    {
        var tmpDir = Environment.GetEnvironmentVariable("TMPDIR");
        if (string.IsNullOrEmpty(tmpDir))
        {
            logger?.LogWarning("[XBRIDGE] TMPDIR environment variable is not set, skipping DISCORD_IPC_PATH registry write.");
            return;
        }

        const string regKey = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
        var regArgs = $"reg add \"{regKey}\" /v DISCORD_IPC_PATH /t REG_SZ /d \"{tmpDir}\" /f";

        logger?.LogInformation("[XBRIDGE] Writing DISCORD_IPC_PATH={TmpDir} to registry", tmpDir);
        var result = await RunWineProcessAsync(regArgs, cancellationToken, ignoreNonZeroExitCode: true);
        if (result.ExitCode != 0)
            logger?.LogWarning("[XBRIDGE] Failed to write DISCORD_IPC_PATH to registry: {Error}", result.StandardError);
        else
            logger?.LogInformation("[XBRIDGE] DISCORD_IPC_PATH written to registry successfully");
    }

    private async Task UninstallXBridgeFromPrefixAsync(CancellationToken cancellationToken)
    {
        if (_winePaths == null)
            throw new PlatformNotSupportedException("Wine path service is unavailable.");

        if (!File.Exists(PrefixXBridgeExePath))
        {
            logger?.LogWarning("[XBRIDGE] xbridge.exe not found in prefix, skipping uninstall.");
            return;
        }

        var wineUninstallPath = $"Z:{PrefixXBridgeExePath.Replace("/", "\\")}";
        await RunWineProcessAsync($"\"{wineUninstallPath}\" --uninstall", cancellationToken, ignoreNonZeroExitCode: true);

        if (File.Exists(PrefixXBridgeExePath))
        {
            logger?.LogInformation("[XBRIDGE] Deleting xbridge.exe from prefix: {Path}", PrefixXBridgeExePath);
            File.Delete(PrefixXBridgeExePath);
        }
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunWineProcessAsync(
        string arguments,
        CancellationToken cancellationToken,
        bool ignoreNonZeroExitCode = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _winePaths!.Wine,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var (key, value) in _winePaths.GetEnvironment())
            startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start Wine process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 && !ignoreNonZeroExitCode)
        {
            logger?.LogWarning("[XBRIDGE] Wine process failed: wine {Args} (exit {Code})\n{Stderr}",
                arguments, process.ExitCode, stderr);
        }

        return (process.ExitCode, stdout, stderr);
    }
}

