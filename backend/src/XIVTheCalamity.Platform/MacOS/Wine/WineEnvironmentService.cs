using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform.MacOS.Audio;

namespace XIVTheCalamity.Platform.MacOS.Wine;

/// <summary>
/// Wine environment service for macOS
/// Implements IEnvironmentService interface
/// </summary>
public class WineEnvironmentService(
    ConfigService configService,
    AudioRouterService? audioRouterService = null,
    ILogger<WineEnvironmentService>? logger = null
) : IEnvironmentService
{
    private readonly WinePathService _paths = WinePathService.Instance;
    private readonly WinePrefixService _prefixService = new();

    public async Task InitializeAsync(IProgress<EnvironmentInitProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WINE-ENV] Starting Wine environment initialization");
        
        // Use WinePrefixService with detailed progress reporting
        var wineProgress = new Progress<WineInitProgress>(p =>
        {
            logger?.LogDebug("[WINE-ENV] Wine progress: Stage={Stage}, MessageKey={MessageKey}", 
                p.Stage, p.MessageKey);
            
            // Convert WineInitProgress to EnvironmentInitProgress
            // Map Wine stages to percent (rough estimation)
            var percent = p.Stage switch
            {
                WineInitStage.Checking => 10,
                WineInitStage.CreatingPrefix => 30,
                WineInitStage.ConfiguringMedia => 50,
                WineInitStage.InstallingFonts => 70,
                WineInitStage.SettingLocale => 90,
                WineInitStage.Complete => 100,
                _ => 0
            };
            
            progress?.Report(new EnvironmentInitProgress
            {
                Stage = p.Stage.ToString().ToLower(),
                MessageKey = p.MessageKey,
                Percent = percent,
                IsComplete = p.IsComplete,
                HasError = p.HasError,
                ErrorMessageKey = p.ErrorMessageKey,
                ErrorMessage = p.ErrorParams?.ContainsKey("message") == true ? p.ErrorParams["message"].ToString() : null,
                ExtraData = p.ErrorParams
            });
        });
        
        await _prefixService.InitializePrefixAsync(wineProgress, cancellationToken);
        logger?.LogInformation("[WINE-ENV] Wine environment initialization complete");
    }

    public async Task EnsurePrefixAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("[WINE-ENV] EnsurePrefixAsync called");
        await _prefixService.EnsurePrefixAsync(cancellationToken);
    }

    public string GetEmulatorDirectory()
    {
        return _paths.WineRoot;
    }

    public Dictionary<string, string> GetEnvironment()
    {
        return _paths.GetEnvironment();
    }

    public async Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("[WINE-ENV] Executing: {Command} {Args}", command, string.Join(" ", args));
        
        var startInfo = new ProcessStartInfo
        {
            FileName = _paths.Wine,
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
        if (process is null)
        {
            throw new Exception($"Failed to start Wine process: {command}");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    public Task<bool> IsAvailableAsync()
    {
        var available = _prefixService.IsWineInstalled();
        logger?.LogDebug("[WINE-ENV] Wine available: {Available}", available);
        return Task.FromResult(available);
    }

    public string GetDebugInfo()
    {
        return $"Wine Environment (macOS)\n" +
               $"Wine Root: {_paths.WineRoot}\n" +
               $"Wine Prefix: {_paths.WinePrefix}\n" +
               $"Wine Executable: {_paths.Wine}\n" +
               $"Prefix Initialized: {_prefixService.IsPrefixInitialized()}";
    }

    public async Task ApplyConfigAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WINE-ENV] Applying Wine configuration");
        var config = await configService.LoadConfigAsync();
        if (config.Wine != null)
        {
            await _prefixService.ApplyGraphicsSettingsAsync(config.Wine);
            logger?.LogInformation("[WINE-ENV] Wine configuration applied successfully");
        }
        else
        {
            logger?.LogWarning("[WINE-ENV] No Wine configuration found, skipping apply");
        }
    }

    public void StartAudioRouter(int gamePid, bool esyncEnabled, bool msyncEnabled)
    {
        if (audioRouterService == null)
        {
            logger?.LogWarning("[WINE-ENV] AudioRouterService not available");
            return;
        }

        try
        {
            logger?.LogInformation("[WINE-ENV] Starting audio router for game PID: {Pid}, Esync: {Esync}, Msync: {Msync}", 
                gamePid, esyncEnabled, msyncEnabled);
            logger?.LogInformation("[WINE-ENV] Audio router params - WinePath: {WinePath}, WinePrefix: {WinePrefix}", 
                _paths.Wine, _paths.WinePrefix);
            
            var result = audioRouterService.StartRouter(gamePid, _paths.WinePrefix, _paths.Wine, esyncEnabled, msyncEnabled);
            
            if (result)
            {
                logger?.LogInformation("[WINE-ENV] Audio router started successfully");
            }
            else
            {
                logger?.LogWarning("[WINE-ENV] Audio router failed to start");
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[WINE-ENV] Failed to start audio router");
        }
    }

    // Expose WinePrefixService methods for backward compatibility
    public WinePrefixService GetPrefixService() => _prefixService;
}
