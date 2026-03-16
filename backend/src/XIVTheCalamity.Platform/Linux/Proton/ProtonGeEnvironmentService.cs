using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Platform.Linux.Proton;

/// <summary>
/// Linux Proton-GE runtime service.
/// </summary>
public class ProtonGeEnvironmentService(
    ProtonGeDownloadService downloadService,
    ConfigService configService,
    ILogger<ProtonGeEnvironmentService>? logger = null
) : IEnvironmentService
{
    private readonly PlatformPathService _platformPaths = PlatformPathService.Instance;

    private string ProtonRoot => downloadService.ProtonCurrentDirectory;
    private string ProtonWine => downloadService.ProtonWinePath;
    private string ProtonWineboot => Path.Combine(ProtonRoot, "files", "bin", "wineboot");
    private string ProtonWineserver => Path.Combine(ProtonRoot, "files", "bin", "wineserver");
    private string WinePrefix => _platformPaths.GetWinePrefixPath();
    private string PrefixSystem32 => Path.Combine(WinePrefix, "drive_c", "windows", "system32");

    public async IAsyncEnumerable<EnvironmentProgressEvent> InitializeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[PROTON-GE] Starting Proton-GE environment initialization");

        yield return new EnvironmentProgressEvent
        {
            Stage = "check_proton",
            MessageKey = "progress.checking_wine",
            CompletedItems = 5,
            TotalItems = 100
        };

        var protonStatus = await downloadService.GetStatusAsync();
        var needsDownload = !protonStatus.IsInstalled;

        if (needsDownload)
        {
            logger?.LogInformation("[PROTON-GE] Proton-GE not installed, downloading latest release");

            var downloadFailed = false;
            string? downloadError = null;

            await foreach (var downloadProgress in downloadService.DownloadLatestAsync(cancellationToken))
            {
                var mappedPercentage = 5 + (int)(downloadProgress.Percentage * 0.75);
                yield return new EnvironmentProgressEvent
                {
                    Stage = downloadProgress.Stage,
                    MessageKey = downloadProgress.MessageKey,
                    CompletedItems = mappedPercentage,
                    TotalItems = 100
                };

                if (downloadProgress.HasError)
                {
                    downloadFailed = true;
                    downloadError = downloadProgress.ErrorMessage;
                    break;
                }
            }

            if (downloadFailed)
            {
                yield return new EnvironmentProgressEvent
                {
                    Stage = "error",
                    MessageKey = "error.wine_download_failed",
                    HasError = true,
                    ErrorMessage = downloadError ?? "Proton-GE download failed"
                };
                yield break;
            }
        }
        else
        {
            logger?.LogInformation("[PROTON-GE] Proton-GE already installed: {Version}", protonStatus.Version ?? "unknown");
        }

        yield return new EnvironmentProgressEvent
        {
            Stage = "init_prefix",
            MessageKey = "progress.init_wine_prefix",
            CompletedItems = 82,
            TotalItems = 100
        };

        await EnsurePrefixAsync(cancellationToken);

        yield return new EnvironmentProgressEvent
        {
            Stage = "complete",
            MessageKey = "progress.environment_ready",
            CompletedItems = 100,
            TotalItems = 100,
            IsComplete = true
        };

        logger?.LogInformation("[PROTON-GE] Environment initialization complete");
    }

    public async Task EnsurePrefixAsync(CancellationToken cancellationToken = default)
    {
        var driveC = Path.Combine(WinePrefix, "drive_c");
        var userReg = Path.Combine(WinePrefix, "user.reg");
        var systemReg = Path.Combine(WinePrefix, "system.reg");
        if (Directory.Exists(driveC) && File.Exists(userReg) && File.Exists(systemReg))
        {
            EnsureProtonSystemDllsInPrefix();
            logger?.LogDebug("[PROTON-GE] Wine prefix already initialized: {Prefix}", WinePrefix);
            return;
        }

        if (!File.Exists(ProtonWine))
        {
            throw new FileNotFoundException($"Proton wine64 executable not found: {ProtonWine}");
        }

        logger?.LogInformation("[PROTON-GE] Initializing Wine prefix via Proton: {Prefix}", WinePrefix);

        var env = GetEnvironment();
        var winebootExe = File.Exists(ProtonWineboot) ? ProtonWineboot : ProtonWine;
        var winebootArgs = File.Exists(ProtonWineboot) ? "-u" : "wineboot -u";
        await RunProtonCommandAsync(winebootExe, winebootArgs, env, "wineboot", cancellationToken);

        var wineserverExe = File.Exists(ProtonWineserver) ? ProtonWineserver : ProtonWine;
        var wineserverArgs = File.Exists(ProtonWineserver) ? "-w" : "wineserver -w";
        await RunProtonCommandAsync(wineserverExe, wineserverArgs, env, "wineserver -w", cancellationToken);

        EnsureProtonSystemDllsInPrefix();
        logger?.LogInformation("[PROTON-GE] Wine prefix initialized successfully");
    }

    private void EnsureProtonSystemDllsInPrefix()
    {
        try
        {
            Directory.CreateDirectory(PrefixSystem32);

            var sourceDirs = new[]
            {
                Path.Combine(ProtonRoot, "files", "lib", "vkd3d", "x86_64-windows"),
                Path.Combine(ProtonRoot, "files", "lib64", "vkd3d", "x86_64-windows"),
                Path.Combine(ProtonRoot, "files", "share", "default_pfx", "drive_c", "windows", "system32"),
            }.Where(Directory.Exists).ToArray();

            if (sourceDirs.Length == 0)
            {
                logger?.LogWarning("[PROTON-GE] No source directories found for Proton system DLL installation");
                return;
            }

            var requiredDlls = new[]
            {
                "libvkd3d-1.dll",
                "libvkd3d-shader-1.dll",
            };

            foreach (var dll in requiredDlls)
            {
                var sourcePath = sourceDirs
                    .Select(dir => Path.Combine(dir, dll))
                    .FirstOrDefault(File.Exists);

                if (string.IsNullOrEmpty(sourcePath))
                {
                    logger?.LogWarning("[PROTON-GE] Required DLL not found in Proton runtime: {Dll}", dll);
                    continue;
                }

                var targetPath = Path.Combine(PrefixSystem32, dll);
                File.Copy(sourcePath, targetPath, overwrite: true);
                logger?.LogDebug("[PROTON-GE] Installed {Dll} to prefix system32", dll);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[PROTON-GE] Failed to install required Proton system DLLs into prefix");
        }
    }

    private async Task RunProtonCommandAsync(
        string executable,
        string arguments,
        Dictionary<string, string> environment,
        string commandName,
        CancellationToken cancellationToken)
    {
        logger?.LogDebug("[PROTON-GE] Running {Command}: {Executable} {Arguments}", commandName, executable, arguments);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var (key, value) in environment)
        {
            processStartInfo.Environment[key] = value;
        }

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            throw new Exception($"Failed to start Proton command: {commandName}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new Exception(
                $"Proton {commandName} failed with exit code {process.ExitCode}. " +
                $"Executable: {executable} {arguments}. stderr: {stderr} stdout: {stdout}");
        }
    }

    public string GetEmulatorDirectory()
    {
        return ProtonRoot;
    }

    public string GetWineExecutablePath()
    {
        return ProtonWine;
    }

    public Dictionary<string, string> GetEnvironment()
    {
        // Ensure required Proton-side system DLLs are present before generating launch environment.
        // This keeps launches resilient even if environment initialization was skipped/interrupted.
        EnsureProtonSystemDllsInPrefix();

        var config = configService.LoadConfigAsync().GetAwaiter().GetResult();
        var wineConfig = config.WineXIV ?? new WineXIVConfig();

        var protonFiles = Path.Combine(ProtonRoot, "files");
        var wineLibPath = Directory.Exists(Path.Combine(protonFiles, "lib64", "wine"))
            ? Path.Combine(protonFiles, "lib64", "wine")
            : Path.Combine(protonFiles, "lib", "wine");
        var dxvkDllPath = Directory.Exists(Path.Combine(protonFiles, "lib", "wine", "dxvk", "x86_64-windows"))
            ? Path.Combine(protonFiles, "lib", "wine", "dxvk", "x86_64-windows")
            : Path.Combine(protonFiles, "lib64", "wine", "dxvk", "x86_64-windows");
        var vkd3dDllPath = Directory.Exists(Path.Combine(protonFiles, "lib", "vkd3d", "x86_64-windows"))
            ? Path.Combine(protonFiles, "lib", "vkd3d", "x86_64-windows")
            : Path.Combine(protonFiles, "lib64", "vkd3d", "x86_64-windows");

        var wineDllPath = Path.Combine(wineLibPath, "x86_64-windows");
        var unixLibPath = Path.Combine(wineLibPath, "x86_64-unix");

        var ldLibraryParts = new List<string>();
        var protonLib64 = Path.Combine(protonFiles, "lib64");
        var protonLib = Path.Combine(protonFiles, "lib");

        if (Directory.Exists(protonLib64))
        {
            ldLibraryParts.Add(protonLib64);
        }

        if (Directory.Exists(protonLib))
        {
            ldLibraryParts.Add(protonLib);
        }

        if (Directory.Exists(unixLibPath))
        {
            ldLibraryParts.Add(unixLibPath);
        }

        var inheritedLdLibrary = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        if (!string.IsNullOrWhiteSpace(inheritedLdLibrary))
        {
            ldLibraryParts.Add(inheritedLdLibrary);
        }

        var wineDllParts = new List<string>();
        if (Directory.Exists(dxvkDllPath))
        {
            wineDllParts.Add(dxvkDllPath);
        }

        if (Directory.Exists(wineDllPath))
        {
            wineDllParts.Add(wineDllPath);
        }

        if (Directory.Exists(vkd3dDllPath))
        {
            wineDllParts.Add(vkd3dDllPath);
        }

        var env = new Dictionary<string, string>
        {
            ["WINEPREFIX"] = WinePrefix,
            ["WINEDLLPATH"] = string.Join(":", wineDllParts),
            ["LD_LIBRARY_PATH"] = string.Join(":", ldLibraryParts),
            ["WINEDLLOVERRIDES"] = "mshtml=;d3d11,dxgi,d3d10core,d3d9=n,b",
            ["PROTON_USE_WINED3D"] = "0",
            ["WINEESYNC"] = wineConfig.EsyncEnabled ? "1" : "0",
            ["WINEFSYNC"] = wineConfig.FsyncEnabled ? "1" : "0",
            ["DXVK_HUD"] = wineConfig.DxvkHudEnabled ? "fps,frametime,memory" : "0",
            ["DXVK_ASYNC"] = "0",
            ["WINEDEBUG"] = string.IsNullOrEmpty(wineConfig.WineDebug) ? "-all" : wineConfig.WineDebug,
            ["XL_WINEONLINUX"] = "true",
        };

        if (wineConfig.GameModeEnabled)
        {
            env["LD_PRELOAD"] = "/usr/lib/libgamemodeauto.so.0";
            logger?.LogDebug("[PROTON-GE] GameMode enabled");
        }

        logger?.LogDebug("[PROTON-GE] Generated environment with config: Esync={Esync}, Fsync={Fsync}, DXVK HUD={DxvkHud}, GameMode={GameMode}",
            wineConfig.EsyncEnabled, wineConfig.FsyncEnabled, wineConfig.DxvkHudEnabled, wineConfig.GameModeEnabled);

        return env;
    }

    public async Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default)
    {
        var winePath = GetWineExecutablePath();
        if (!File.Exists(winePath))
        {
            throw new FileNotFoundException($"Proton wine64 executable not found: {winePath}");
        }

        logger?.LogDebug("[PROTON-GE] Executing: {Command} {Args}", command, string.Join(" ", args));

        var startInfo = new ProcessStartInfo
        {
            FileName = winePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add(command);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var env = GetEnvironment();
        foreach (var (key, value) in env)
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new Exception("Failed to start Proton process");
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, output, error);
    }

    public Task<bool> IsAvailableAsync()
    {
        return Task.FromResult(File.Exists(ProtonWine));
    }

    public string GetDebugInfo()
    {
        var version = File.Exists(downloadService.ProtonVersionFilePath)
            ? File.ReadAllText(downloadService.ProtonVersionFilePath).Trim()
            : "unknown";

        return $"Proton-GE Environment:\n" +
               $"  Version: {version}\n" +
               $"  Proton Root: {ProtonRoot}\n" +
               $"  Wine Prefix: {WinePrefix}\n" +
               $"  Wine Executable: {ProtonWine}\n" +
               $"  Installed: {File.Exists(ProtonWine)}";
    }

    public Task ApplyConfigAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("[PROTON-GE] ApplyConfigAsync called (no-op)");
        return Task.CompletedTask;
    }

    public void StartAudioRouter(int gamePid, bool esyncEnabled, bool msyncEnabled)
    {
        logger?.LogDebug("[PROTON-GE] StartAudioRouter called (no-op for Linux)");
    }
}
