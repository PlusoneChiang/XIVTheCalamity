using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Core.Services;

namespace XIVTheCalamity.Platform.MacOS.MetalSharp;

public sealed class MetalSharpEnvironmentService(string runtimePath, ILogger<MetalSharpEnvironmentService> logger)
    : IEnvironmentService
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _prefixReady;

    private static string DataDirectory => Path.Combine(PlatformPathService.Instance.UserDataDirectory, "metalsharp");

    public static string? GetRequestedRuntimePath()
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        var configuredPath = Environment.GetEnvironmentVariable("XIVTC_METALSHARP_APP");
        return configuredPath == string.Empty ? null : configuredPath ?? Path.Combine(DataDirectory, "MetalSharp-Wine.app");
    }

    public string GetFfxivConfigDirectory() =>
        Path.Combine(DataDirectory, "ffxivConfig", PlatformPathService.Instance.ActiveProfile);

    public string GetEmulatorDirectory() => Path.Combine(runtimePath, "Contents");

    private string BinDirectory => Path.Combine(GetEmulatorDirectory(), "MacOS");

    public string GetWineExecutablePath() => Path.Combine(BinDirectory, "wine");

    private string WineServer => Path.Combine(BinDirectory, "wineserver");
    private string Prefix => Path.Combine(DataDirectory, "prefix");

    public Dictionary<string, string> GetEnvironment() => new()
    {
        ["HOME"] = HomePathService.GetEffectiveHomePath(),
        ["WINEPREFIX"] = Prefix,
        ["WINESERVER"] = WineServer,
        ["GRAPHICS_BACKEND"] = "dxvk",
        ["SharpVK_Switch"] = "1",
        ["WINEDEBUG"] = "+seh,-dbghelp,-dbghelp_dwarf",
        ["MTL_HUD_ENABLED"] = "1"
    };

    private bool HasRuntime() =>
        OperatingSystem.IsMacOSVersionAtLeast(15) &&
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 &&
        Path.IsPathFullyQualified(runtimePath) &&
        File.Exists(GetWineExecutablePath()) &&
        File.Exists(WineServer);

    public Task<bool> IsAvailableAsync() => Task.FromResult(HasRuntime());

    public async IAsyncEnumerable<EnvironmentProgressEvent> InitializeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new EnvironmentProgressEvent
        {
            Stage = "checking", MessageKey = "progress.checking_wine", Percentage = 5
        };

        string? failure = null;
        try
        {
            await EnsurePrefixAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MetalSharp Wine 初始化失敗");
            failure = ex.Message;
        }

        if (failure is not null)
        {
            yield return new EnvironmentProgressEvent
            {
                Stage = "error",
                HasError = true,
                ErrorMessageKey = "error.metalsharp_init_failed",
                Params = new() { ["message"] = failure }
            };
            yield break;
        }

        yield return new EnvironmentProgressEvent
        {
            Stage = "complete", CompletedItems = 100, TotalItems = 100, IsComplete = true
        };
    }

    public async Task EnsurePrefixAsync(CancellationToken cancellationToken = default)
    {
        if (!HasRuntime())
            throw new InvalidOperationException($"請在 macOS 15 以上使用原生 ARM64 Launcher，並確認 {runtimePath} 是含有 wine 和 wineserver 的 MetalSharp Wine ARM64 .app 套件。");

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_prefixReady && File.Exists(Path.Combine(Prefix, "system.reg")))
                return;

            logger.LogInformation("正在初始化 MetalSharp Wine。執行檔 {Wine}，prefix {Prefix}",
                GetWineExecutablePath(), Prefix);

            Directory.CreateDirectory(DataDirectory);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            try
            {
                await RunInitializationCommandAsync(GetWineExecutablePath(), ["wineboot", "-u"], timeout.Token);
                await RunInitializationCommandAsync(WineServer, ["-w"], timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("MetalSharp Wine prefix 初始化逾時。");
            }

            _prefixReady = File.Exists(Path.Combine(Prefix, "system.reg"));
            if (!_prefixReady)
                throw new InvalidOperationException("MetalSharp Wine 未建立 prefix 的 system.reg。");
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task RunInitializationCommandAsync(string executable, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/arch")
        {
            UseShellExecute = false,
            ArgumentList = { "-arm64", executable }
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in GetEnvironment())
            startInfo.Environment[key] = value;

        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"無法啟動 MetalSharp Wine 程序 {executable}。");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync();
            }

            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"MetalSharp Wine 初始化失敗，{Path.GetFileName(executable)} 結束碼為 {process.ExitCode}。");
    }

    public Task ApplyConfigAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("MetalSharp Wine 實驗模式目前不支援 Wine 工具。");
}
