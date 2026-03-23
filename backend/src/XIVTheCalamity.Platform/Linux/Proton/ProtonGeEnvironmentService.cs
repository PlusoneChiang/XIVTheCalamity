using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform;
using XIVTheCalamity.Platform.Linux.Umu;
using XIVTheCalamity.Platform.Linux.Wine;

namespace XIVTheCalamity.Platform.Linux.Proton;

/// <summary>
/// Linux Proton-GE runtime service.
/// Uses umu-launcher (when available) to execute Windows programs inside pressure-vessel,
/// which resolves FASMX64/Reloaded.Hooks native-AV crashes that occur with raw wine64.
/// </summary>
public class ProtonGeEnvironmentService(
    ProtonGeDownloadService downloadService,
    UmuDownloadService umuDownloadService,
    DxvkDownloadService dxvkDownloadService,
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

        // Step 2: Download umu-launcher before prefix creation.
        // umu must be available so EnsurePrefixAsync can use pressure-vessel for initialization.
        // Failure is non-fatal: prefix will be created via direct wine64 as fallback.
        yield return new EnvironmentProgressEvent
        {
            Stage = "init_umu",
            MessageKey = "progress.checking_wine",
            CompletedItems = 82,
            TotalItems = 100
        };

        try
        {
            await umuDownloadService.EnsureAvailableAsync(cancellationToken);
            logger?.LogInformation("[PROTON-GE] umu-launcher ready: {Path}", umuDownloadService.UmuRunPath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[PROTON-GE] umu download failed; prefix will be created via wine64 fallback");
        }

        // Step 3: Create wineprefix (via umu if available, otherwise direct wine64).
        yield return new EnvironmentProgressEvent
        {
            Stage = "init_prefix",
            MessageKey = "progress.init_wine_prefix",
            CompletedItems = 90,
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

        logger?.LogInformation("[PROTON-GE] Initializing Wine prefix: {Prefix}", WinePrefix);
        Directory.CreateDirectory(WinePrefix);

        var env = GetEnvironment();

        if (umuDownloadService.IsAvailable())
        {
            // Use umu/pressure-vessel to initialize the prefix.
            // This runs Proton's wineboot inside the Steam Runtime container,
            // ensuring the prefix is correctly configured for Proton games.
            var python3Path = ResolvePython3Path();
            if (!string.IsNullOrEmpty(python3Path))
            {
                logger?.LogInformation("[PROTON-GE] Initializing prefix via umu (pressure-vessel)");
                var umuEnv = new Dictionary<string, string>(env)
                {
                    ["WINEPREFIX"] = WinePrefix,
                    ["GAMEID"] = "0",
                    ["PROTONPATH"] = ProtonRoot,
                    ["STORE"] = "none",
                };
                await RunProtonCommandAsync(python3Path, $"\"{umuDownloadService.UmuRunPath}\" wineboot -u", umuEnv, "umu wineboot", cancellationToken);
                EnsureProtonSystemDllsInPrefix();
                logger?.LogInformation("[PROTON-GE] Wine prefix initialized via umu");
                return;
            }
        }

        // Fallback: initialize prefix directly via Proton wine64
        logger?.LogInformation("[PROTON-GE] Initializing prefix via Proton wine64 (direct)");
        var winebootExe = File.Exists(ProtonWineboot) ? ProtonWineboot : ProtonWine;
        var winebootArgs = File.Exists(ProtonWineboot) ? "-u" : "wineboot -u";
        await RunProtonCommandAsync(winebootExe, winebootArgs, env, "wineboot", cancellationToken);

        var wineserverExe = File.Exists(ProtonWineserver) ? ProtonWineserver : ProtonWine;
        var wineserverArgs = File.Exists(ProtonWineserver) ? "-w" : "wineserver -w";
        await RunProtonCommandAsync(wineserverExe, wineserverArgs, env, "wineserver -w", cancellationToken);

        EnsureProtonSystemDllsInPrefix();
        logger?.LogInformation("[PROTON-GE] Wine prefix initialized successfully");
    }

    private string DxvkCachePath => Path.Combine(WinePrefix, "dxvk_cache");

    private void EnsureProtonSystemDllsInPrefix()
    {
        try
        {
            Directory.CreateDirectory(PrefixSystem32);
            Directory.CreateDirectory(DxvkCachePath);

            InstallVkd3dDlls();
            InstallIcuDlls();
            SyncDxvkAsyncDlls();
            EnsureDxvkConf();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[PROTON-GE] Failed to install required Proton system DLLs into prefix");
        }
    }

    private void EnsureDxvkConf()
    {
        var destPath = Path.Combine(WinePrefix, "dxvk.conf");
        if (File.Exists(destPath))
            return;

        var sourcePath = FindResourceFile(Path.Combine("dxvk", "dxvk.conf"));
        if (sourcePath != null)
        {
            File.Copy(sourcePath, destPath);
            logger?.LogInformation("[PROTON-GE] Copied dxvk.conf from {Source} to {Dest}", sourcePath, destPath);
        }
        else
        {
            File.WriteAllText(destPath, string.Empty);
            logger?.LogWarning("[PROTON-GE] dxvk.conf not found in resources, created empty file at {Path}", destPath);
        }
    }

    private string? FindResourceFile(string relativePath)
    {
        var appDir = AppContext.BaseDirectory;

        // Production (AppImage): resources/ is sibling of backend/
        var bundlePath = Path.GetFullPath(Path.Combine(appDir, "..", "resources", relativePath));
        if (File.Exists(bundlePath)) return bundlePath;

        // Development: search upward for shared/resources
        var dir = new DirectoryInfo(appDir);
        while (dir != null)
        {
            var devPath = Path.Combine(dir.FullName, "shared", "resources", relativePath);
            if (File.Exists(devPath)) return devPath;
            dir = dir.Parent;
        }

        return null;
    }

    private void InstallVkd3dDlls()
    {
        var sourceDirs = new[]
        {
            Path.Combine(ProtonRoot, "files", "lib", "vkd3d", "x86_64-windows"),
            Path.Combine(ProtonRoot, "files", "lib64", "vkd3d", "x86_64-windows"),
            Path.Combine(ProtonRoot, "files", "share", "default_pfx", "drive_c", "windows", "system32"),
        }.Where(Directory.Exists).ToArray();

        var requiredDlls = new[] { "libvkd3d-1.dll", "libvkd3d-shader-1.dll" };

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

    // icu.dll (Wine builtin) is a forwarder DLL that delegates to icuuc68.dll/icuin68.dll/icudt68.dll.
    // Without these DLLs in the prefix, .NET's ICU initialization fails with Error 127 (ERROR_PROC_NOT_FOUND).
    // This mirrors what the Proton script does when initializing a user prefix.
    private void InstallIcuDlls()
    {
        var icuSourceDir = Path.Combine(ProtonRoot, "files", "lib", "wine", "icu", "x86_64-windows");
        if (!Directory.Exists(icuSourceDir))
        {
            logger?.LogWarning("[PROTON-GE] Proton ICU DLL directory not found: {Dir}", icuSourceDir);
            return;
        }

        var icuDlls = new[] { "icuuc68.dll", "icuin68.dll", "icudt68.dll" };
        foreach (var dll in icuDlls)
        {
            var sourcePath = Path.Combine(icuSourceDir, dll);
            if (!File.Exists(sourcePath))
            {
                logger?.LogWarning("[PROTON-GE] Proton ICU DLL not found: {Path}", sourcePath);
                continue;
            }

            var targetPath = Path.Combine(PrefixSystem32, dll);
            File.Copy(sourcePath, targetPath, overwrite: true);
            logger?.LogDebug("[PROTON-GE] Installed {Dll} to prefix system32 (ICU)", dll);
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

    /// <summary>
    /// Returns umu-based launcher when umu-run is available, falling back to raw wine64.
    /// umu uses pressure-vessel to sandbox the Proton environment, which fixes
    /// FASMX64.dll/Reloaded.Hooks AV crashes on Linux.
    /// </summary>
    public WineLauncher GetLauncherCommand()
    {
        WineLauncher baseLauncher;
        if (umuDownloadService.IsAvailable())
        {
            var python3Path = ResolvePython3Path();
            if (!string.IsNullOrEmpty(python3Path))
            {
                logger?.LogDebug("[PROTON-GE] Using umu launcher: {Python3} {UmuRun}", python3Path, umuDownloadService.UmuRunPath);
                baseLauncher = new WineLauncher(python3Path, [umuDownloadService.UmuRunPath]);
            }
            else
            {
                logger?.LogWarning("[PROTON-GE] python3 not found, falling back to wine64");
                baseLauncher = new WineLauncher(ProtonWine, []);
            }
        }
        else
        {
            logger?.LogDebug("[PROTON-GE] umu not available, falling back to wine64: {Wine}", ProtonWine);
            baseLauncher = new WineLauncher(ProtonWine, []);
        }

        var config = configService.LoadConfigAsync().GetAwaiter().GetResult();
        var launchOptions = config.ProtonGe?.LaunchOptions ?? "%command%";
        return ApplyLaunchOptions(baseLauncher, launchOptions);
    }

    private WineLauncher ApplyLaunchOptions(WineLauncher baseLauncher, string launchOptions)
    {
        var trimmed = launchOptions.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == "%command%")
            return baseLauncher;

        var cmdIndex = trimmed.IndexOf("%command%", StringComparison.OrdinalIgnoreCase);
        if (cmdIndex < 0)
        {
            logger?.LogWarning("[PROTON-GE] LaunchOptions missing %%command%% placeholder, ignoring: {Options}", trimmed);
            return baseLauncher;
        }

        var prefix = trimmed[..cmdIndex].Trim();
        if (string.IsNullOrEmpty(prefix))
            return baseLauncher;

        var tokens = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var wrapperExe = tokens[0].Replace("~", home);

        // new PrefixArgs = wrapper extra tokens + original exe + original prefix args
        var newPrefixArgs = tokens[1..]
            .Concat([baseLauncher.Executable])
            .Concat(baseLauncher.PrefixArgs)
            .ToList();

        logger?.LogInformation("[PROTON-GE] Launch wrapper applied: {Exe} [{Prefix}]", wrapperExe, string.Join(", ", newPrefixArgs));
        return new WineLauncher(wrapperExe, newPrefixArgs);
    }

    private static string ResolvePython3Path()
    {
        // Check common fixed locations first (faster than PATH search)
        string[] candidates = ["/usr/bin/python3", "/usr/local/bin/python3", "/bin/python3"];
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return string.Empty;
    }

    public Dictionary<string, string> GetEnvironment()
    {
        // Ensure required Proton-side system DLLs are present before generating launch environment.
        // This keeps launches resilient even if environment initialization was skipped/interrupted.
        EnsureProtonSystemDllsInPrefix();

        var config = configService.LoadConfigAsync().GetAwaiter().GetResult();
        var wineConfig = config.ProtonGe ?? new ProtonGeConfig();

        // umu/pressure-vessel manages LD_LIBRARY_PATH and WINEDLLPATH internally.
        // Passing these manually causes library conflicts and crashes.
        if (umuDownloadService.IsAvailable())
        {
            return GetUmuEnvironment(wineConfig);
        }

        return GetDirectWineEnvironment(wineConfig);
    }

    /// <summary>
    /// Environment for umu/pressure-vessel mode.
    /// umu handles all library paths internally — we only set high-level directives.
    /// </summary>
    private Dictionary<string, string> GetUmuEnvironment(ProtonGeConfig wineConfig)
    {
        var env = new Dictionary<string, string>
        {
            ["WINEPREFIX"] = WinePrefix,
            ["GAMEID"] = "0",
            ["PROTONPATH"] = ProtonRoot,
            ["STORE"] = "none",
            ["WINEDLLOVERRIDES"] = wineConfig.DxvkAsyncEnabled
                ? "mshtml=;d3d11,dxgi,d3d10core,d3d9=n,b"
                : "mshtml=",
            ["WINEESYNC"] = wineConfig.EsyncEnabled ? "1" : "0",
            ["WINEFSYNC"] = wineConfig.FsyncEnabled ? "1" : "0",
            ["DXVK_HUD"] = wineConfig.DxvkHudEnabled ? "fps,frametime,memory" : "0",
            ["DXVK_ASYNC"] = wineConfig.DxvkAsyncEnabled ? "1" : "0",
            ["DXVK_SHADER_CACHE_PATH"] = DxvkCachePath,
            ["DXVK_CONFIG_FILE"] = Path.Combine(WinePrefix, "dxvk.conf"),
            ["WINEDEBUG"] = string.IsNullOrEmpty(wineConfig.WineDebug) ? "-all" : wineConfig.WineDebug,
            ["XL_WINEONLINUX"] = "true",
        };

        if (wineConfig.GameModeEnabled)
            env["LD_PRELOAD"] = "/usr/lib/libgamemodeauto.so.0";

        // IME (input method) support — detect fcitx5/ibus and set required env vars
        var imeFramework = DetectImeFramework();
        if (imeFramework != null)
        {
            env["XMODIFIERS"] = $"@im={imeFramework}";
            env["GTK_IM_MODULE"] = imeFramework;
            env["QT_IM_MODULE"] = imeFramework;
            logger?.LogInformation("[PROTON-GE] IME detected: {Framework}, set XMODIFIERS/GTK_IM_MODULE/QT_IM_MODULE", imeFramework);
        }

        // Apply extra user-defined environment variables (these override defaults)
        foreach (var (key, value) in wineConfig.ExtraEnvironmentVariables)
        {
            env[key] = value;
            logger?.LogDebug("[PROTON-GE] Extra env: {Key}={Value}", key, value);
        }

        logger?.LogDebug("[PROTON-GE] umu environment: GAMEID=0, PROTONPATH={ProtonRoot}, WINEPREFIX={Prefix}", ProtonRoot, WinePrefix);
        return env;
    }

    private void SyncDxvkAsyncDlls()
    {
        var config = configService.LoadConfigAsync().GetAwaiter().GetResult();
        var wineConfig = config.ProtonGe ?? new ProtonGeConfig();

        if (wineConfig.DxvkAsyncEnabled)
        {
            logger?.LogInformation("[DXVK-ASYNC] DxvkAsync enabled — ensuring GPLAsync is installed");
            dxvkDownloadService.EnsureDxvk();

            foreach (var srcPath in Directory.GetFiles(dxvkDownloadService.DxvkDllDirectory, "*.dll"))
            {
                var dllName  = Path.GetFileName(srcPath);
                var destPath = Path.Combine(PrefixSystem32, dllName);
                var bakPath  = destPath + ".bak";

                if (File.Exists(destPath) && !File.Exists(bakPath))
                {
                    File.Move(destPath, bakPath);
                    logger?.LogInformation("[DXVK-ASYNC] Backed up {Dll} → .bak", dllName);
                }

                File.Copy(srcPath, destPath, overwrite: true);
                logger?.LogInformation("[DXVK-ASYNC] Installed GPLAsync {Dll}", dllName);
            }
        }
        else
        {
            if (!Directory.Exists(dxvkDownloadService.DxvkDllDirectory)) return;

            foreach (var srcPath in Directory.GetFiles(dxvkDownloadService.DxvkDllDirectory, "*.dll"))
            {
                var dllName  = Path.GetFileName(srcPath);
                var dllPath  = Path.Combine(PrefixSystem32, dllName);
                var bakPath  = dllPath + ".bak";

                if (File.Exists(dllPath))
                {
                    File.Delete(dllPath);
                    logger?.LogInformation("[DXVK-ASYNC] Removed GPLAsync {Dll}", dllName);
                }

                if (File.Exists(bakPath))
                {
                    File.Move(bakPath, dllPath);
                    logger?.LogInformation("[DXVK-ASYNC] Restored {Dll} from .bak", dllName);
                }
            }
        }
    }

    private static string? DetectImeFramework()
    {
        if (System.Diagnostics.Process.GetProcessesByName("fcitx5").Length > 0 ||
            System.Diagnostics.Process.GetProcessesByName("fcitx5-bin").Length > 0 ||
            System.Diagnostics.Process.GetProcessesByName("fcitx").Length > 0)
            return "fcitx";

        if (System.Diagnostics.Process.GetProcessesByName("ibus-daemon").Length > 0)
            return "ibus";

        return null;
    }

    /// <summary>
    /// Environment for direct wine64 mode (no umu).
    /// Must manually configure all library paths that Proton normally sets up.
    /// </summary>
    private Dictionary<string, string> GetDirectWineEnvironment(ProtonGeConfig wineConfig)
    {
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

        if (Directory.Exists(protonLib64)) ldLibraryParts.Add(protonLib64);
        if (Directory.Exists(protonLib)) ldLibraryParts.Add(protonLib);
        if (Directory.Exists(unixLibPath)) ldLibraryParts.Add(unixLibPath);

        var inheritedLdLibrary = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        if (!string.IsNullOrWhiteSpace(inheritedLdLibrary))
            ldLibraryParts.Add(inheritedLdLibrary);

        var wineDllParts = new List<string>();
        if (Directory.Exists(dxvkDllPath)) wineDllParts.Add(dxvkDllPath);
        if (Directory.Exists(wineDllPath)) wineDllParts.Add(wineDllPath);
        if (Directory.Exists(vkd3dDllPath)) wineDllParts.Add(vkd3dDllPath);

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
            ["DXVK_ASYNC"] = "1",
            ["WINEDEBUG"] = string.IsNullOrEmpty(wineConfig.WineDebug) ? "-all" : wineConfig.WineDebug,
            ["XL_WINEONLINUX"] = "true",
        };

        if (wineConfig.GameModeEnabled)
            env["LD_PRELOAD"] = "/usr/lib/libgamemodeauto.so.0";

        // Apply extra user-defined environment variables (these override defaults)
        foreach (var (key, value) in wineConfig.ExtraEnvironmentVariables)
        {
            env[key] = value;
            logger?.LogDebug("[PROTON-GE] Extra env: {Key}={Value}", key, value);
        }

        logger?.LogDebug("[PROTON-GE] Direct wine64 environment: Esync={Esync}, Fsync={Fsync}, DXVK HUD={DxvkHud}, GameMode={GameMode}",
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

        var umuVersion = umuDownloadService.GetInstalledVersionAsync().GetAwaiter().GetResult() ?? "not installed";

        return $"Proton-GE Environment:\n" +
               $"  Version: {version}\n" +
               $"  Proton Root: {ProtonRoot}\n" +
               $"  Wine Prefix: {WinePrefix}\n" +
               $"  Wine Executable: {ProtonWine}\n" +
               $"  Installed: {File.Exists(ProtonWine)}\n" +
               $"  umu-launcher: {umuVersion} ({(umuDownloadService.IsAvailable() ? umuDownloadService.UmuRunPath : "unavailable")})\n" +
               $"  Launcher Mode: {(umuDownloadService.IsAvailable() ? "umu (pressure-vessel)" : "wine64 (direct)")}";
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
