using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Game.Models;

namespace XIVTheCalamity.Game.Services;

/// <summary>
/// Game update manager using IAsyncEnumerable for progress reporting
/// Coordinates version checking, downloads, installation, and cleanup
/// Flow: Download (parallel) → Install (sequential) → Cleanup (optional)
/// </summary>
public class UpdateManager
{
    private readonly ILogger<UpdateManager> _logger;
    private readonly GameVersionService _versionService;
    private readonly PatchListParser _patchListParser;
    private readonly PatchInstallService _patchInstallService;
    private readonly PatchDownloadManager _downloadManager;

    private bool _keepPatches = false;

    public UpdateManager(
        ILogger<UpdateManager> logger,
        GameVersionService versionService,
        PatchListParser patchListParser,
        PatchInstallService patchInstallService,
        PatchDownloadManager downloadManager)
    {
        _logger = logger;
        _versionService = versionService;
        _patchListParser = patchListParser;
        _patchInstallService = patchInstallService;
        _downloadManager = downloadManager;
    }

    /// <summary>
    /// Set whether to keep patch files after installation
    /// </summary>
    public void SetKeepPatches(bool keep)
    {
        _keepPatches = keep;
    }

    /// <summary>
    /// Check for updates without installing
    /// </summary>
    public async Task<UpdateCheckResult> CheckUpdatesAsync(
        string gamePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Checking for updates...");

            var versionInfo = _versionService.GetLocalVersions(gamePath);
            var gameVersion = versionInfo.GetVersion(GameRepository.Game)?.Version ?? "2012.01.01.0000.0000";

            var allPatches = await _patchListParser.GetPatchesFromOfficialApiAsync(
                gameVersion, versionInfo, cancellationToken);
            
            var requiredPatches = _patchListParser.GetRequiredPatches(allPatches, versionInfo);

            if (requiredPatches.Count == 0)
            {
                _logger.LogInformation("Game is up to date");
                return new UpdateCheckResult
                {
                    NeedsUpdate = false,
                    CurrentVersions = versionInfo
                };
            }

            _logger.LogInformation("Found {Count} patches to download", requiredPatches.Count);
            
            return new UpdateCheckResult
            {
                NeedsUpdate = true,
                RequiredPatches = requiredPatches,
                TotalDownloadSize = requiredPatches.Sum(p => p.Size),
                CurrentVersions = versionInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed");
            return new UpdateCheckResult
            {
                NeedsUpdate = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Check and install updates with progress reporting via IAsyncEnumerable
    /// Yields progress events for download, install, and cleanup phases
    /// </summary>
    public async IAsyncEnumerable<PatchProgressEvent> CheckAndInstallUpdatesAsync(
        string gamePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== Starting update check and install ===");

        // Yield initial progress immediately to keep SSE connection alive
        yield return new PatchProgressEvent
        {
            Stage = "checking",
            MessageKey = "progress.checking_updates",
            Phase = "checking",
            Percentage = 0
        };

        // Set patch download directory
        var patchDownloadPath = Path.Combine(gamePath, ".patches");
        if (!Directory.Exists(patchDownloadPath))
        {
            Directory.CreateDirectory(patchDownloadPath);
        }

        // Check for updates
        var checkResult = await CheckUpdatesAsync(gamePath, cancellationToken);
        
        if (!checkResult.NeedsUpdate)
        {
            yield return new PatchProgressEvent
            {
                Stage = "up_to_date",
                MessageKey = "progress.up_to_date",
                Phase = "checking",
                IsComplete = true,
                Percentage = 100
            };
            yield break;
        }

        if (checkResult.RequiredPatches == null || checkResult.RequiredPatches.Count == 0)
        {
            yield return new PatchProgressEvent
            {
                Stage = "error",
                MessageKey = "progress.check_failed",
                Phase = "checking",
                HasError = true,
                ErrorMessage = checkResult.ErrorMessage
            };
            yield break;
        }

        var requiredPatches = checkResult.RequiredPatches;
        _logger.LogInformation("Found {Count} patches to process", requiredPatches.Count);

        // Phase 1: Download all patches (parallel)
        await foreach (var downloadProgress in DownloadPatchesAsync(
            requiredPatches, patchDownloadPath, cancellationToken))
        {
            yield return downloadProgress;
            
            // If download failed, stop
            if (downloadProgress.HasError)
            {
                yield break;
            }
        }

        // Phase 2: Install all patches (sequential)
        await foreach (var installProgress in InstallPatchesAsync(
            gamePath, requiredPatches, cancellationToken))
        {
            yield return installProgress;
            
            // If install failed, stop
            if (installProgress.HasError)
            {
                yield break;
            }
        }

        // Phase 3: Cleanup patch files (if not keeping)
        if (!_keepPatches)
        {
            await foreach (var cleanupProgress in CleanupPatchesAsync(
                requiredPatches, cancellationToken))
            {
                yield return cleanupProgress;
            }
        }

        // Final completion
        yield return new PatchProgressEvent
        {
            Stage = "all_complete",
            MessageKey = "progress.all_complete",
            Phase = "complete",
            TotalPatches = requiredPatches.Count,
            CompletedPatches = requiredPatches.Count,
            InstalledPatches = requiredPatches.Count,
            IsComplete = true,
            Percentage = 100
        };

        _logger.LogInformation("=== Update completed successfully ===");
    }

    /// <summary>
    /// Phase 1: Download all patches with parallel execution
    /// </summary>
    private async IAsyncEnumerable<PatchProgressEvent> DownloadPatchesAsync(
        List<PatchInfo> patches,
        string downloadPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Phase 1: Downloading {Count} patches...", patches.Count);

        // Directly forward all events from download manager
        // Let exceptions propagate to caller (CheckAndInstallUpdatesAsync)
        await foreach (var progress in _downloadManager.DownloadAllPatchesAsync(
            patches, downloadPath, cancellationToken))
        {
            yield return progress;
        }
    }

    /// <summary>
    /// Phase 2: Install all patches sequentially
    /// </summary>
    private async IAsyncEnumerable<PatchProgressEvent> InstallPatchesAsync(
        string gamePath,
        List<PatchInfo> patches,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Phase 2: Installing {Count} patches...", patches.Count);

        yield return new PatchProgressEvent
        {
            Stage = "install_started",
            MessageKey = "progress.install_started",
            Phase = "installing",
            TotalPatches = patches.Count,
            InstalledPatches = 0,
            InstallingCount = 0
        };

        for (int i = 0; i < patches.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var patch = patches[i];
            
            _logger.LogInformation("Installing patch {Index}/{Total}: {FileName}",
                i + 1, patches.Count, patch.FileName);

            // Report installing start
            yield return new PatchProgressEvent
            {
                Stage = "installing_patch",
                MessageKey = "progress.installing_patch",
                Phase = "installing",
                TotalPatches = patches.Count,
                InstalledPatches = i,
                InstallingCount = 1,
                InstallingFileName = patch.FileName,
                Percentage = (int)((i * 100.0) / patches.Count)
            };

            // Perform installation (collect events)
            var installSuccess = false;
            PatchProgressEvent? errorEvent = null;

            try
            {
                await Task.Run(() =>
                {
                    _patchInstallService.InstallPatch(patch.LocalPath!, gamePath, patch.Repository);
                    _patchInstallService.UpdateVersionFile(gamePath, patch.Repository, patch.Version);
                }, cancellationToken);

                _logger.LogInformation("Patch installed: {FileName}", patch.FileName);
                installSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to install patch: {FileName}", patch.FileName);
                
                errorEvent = new PatchProgressEvent
                {
                    Stage = "install_error",
                    MessageKey = "progress.install_error",
                    Phase = "installing",
                    HasError = true,
                    ErrorMessage = $"Failed to install {patch.FileName}: {ex.Message}"
                };
            }

            // Yield appropriate event
            if (errorEvent != null)
            {
                yield return errorEvent;
                yield break;
            }

            if (installSuccess)
            {
                // Report patch installed
                yield return new PatchProgressEvent
                {
                    Stage = "patch_installed",
                    MessageKey = "progress.patch_installed",
                    Phase = "installing",
                    TotalPatches = patches.Count,
                    InstalledPatches = i + 1,
                    InstallingCount = 0,
                    CompletedPatches = i + 1,
                    Percentage = (int)(((i + 1) * 100.0) / patches.Count)
                };
            }

            // Small delay between installations
            await Task.Delay(50, cancellationToken);
        }

        // All installations complete
        yield return new PatchProgressEvent
        {
            Stage = "install_complete",
            MessageKey = "progress.install_complete",
            Phase = "installing",
            TotalPatches = patches.Count,
            InstalledPatches = patches.Count,
            CompletedPatches = patches.Count,
            InstallingCount = 0,
            Percentage = 100
        };

        _logger.LogInformation("All patches installed successfully");
    }

    /// <summary>
    /// Phase 3: Cleanup patch files
    /// </summary>
    private async IAsyncEnumerable<PatchProgressEvent> CleanupPatchesAsync(
        List<PatchInfo> patches,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Phase 3: Cleaning up {Count} patch files...", patches.Count);

        yield return new PatchProgressEvent
        {
            Stage = "cleanup_started",
            MessageKey = "progress.cleanup_started",
            Phase = "cleanup",
            TotalPatches = patches.Count
        };

        int deletedCount = 0;
        
        foreach (var patch in patches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!string.IsNullOrEmpty(patch.LocalPath) && File.Exists(patch.LocalPath))
            {
                try
                {
                    await Task.Run(() => File.Delete(patch.LocalPath), cancellationToken);
                    deletedCount++;
                    _logger.LogDebug("Deleted patch file: {FileName}", patch.FileName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete patch file: {FileName}", patch.FileName);
                }
            }
        }

        yield return new PatchProgressEvent
        {
            Stage = "cleanup_complete",
            MessageKey = "progress.cleanup_complete",
            Phase = "cleanup",
            TotalPatches = patches.Count,
            Params = new Dictionary<string, object>
            {
                ["deletedCount"] = deletedCount
            }
        };

        _logger.LogInformation("Cleanup complete: deleted {Count} files", deletedCount);
    }
}
