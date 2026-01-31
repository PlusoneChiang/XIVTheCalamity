namespace XIVTheCalamity.Platform.Windows;

using Microsoft.Extensions.Logging;

/// <summary>
/// Windows environment service (native execution, no emulation needed)
/// Empty implementation for future extensibility
/// </summary>
public class WindowsEnvironmentService(
    ILogger<WindowsEnvironmentService>? logger = null
) : IEnvironmentService
{
    public Task InitializeAsync(IProgress<EnvironmentInitProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WIN-ENV] Windows native execution, no initialization needed");
        
        progress?.Report(new EnvironmentInitProgress
        {
            Stage = "complete",
            MessageKey = "progress.skip_windows",
            CompletedItems = 100,
            TotalItems = 100,
            IsComplete = true
        });
        
        return Task.CompletedTask;
    }

    public Task EnsurePrefixAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WIN-ENV] Windows native execution, no prefix initialization needed");
        return Task.CompletedTask;
    }

    public string GetEmulatorDirectory()
    {
        return string.Empty;
    }

    public Dictionary<string, string> GetEnvironment()
    {
        return new Dictionary<string, string>();
    }

    public Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default)
    {
        logger?.LogWarning("[WIN-ENV] Direct process execution not supported on Windows through environment service");
        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }

    public Task<bool> IsAvailableAsync()
    {
        return Task.FromResult(true);
    }

    public string GetDebugInfo()
    {
        return "Windows Native Environment (No emulation)";
    }

    public Task ApplyConfigAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("[WIN-ENV] Windows native, no config to apply");
        return Task.CompletedTask;
    }

    public void StartAudioRouter(int gamePid, bool esyncEnabled, bool msyncEnabled)
    {
        logger?.LogDebug("[WIN-ENV] Audio router not needed on Windows");
    }
}
