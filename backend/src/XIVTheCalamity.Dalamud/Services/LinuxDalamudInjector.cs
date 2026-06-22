using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace XIVTheCalamity.Dalamud.Services;

/// <summary>
/// Linux-specific Wine-based Dalamud injector
/// </summary>
public class LinuxDalamudInjector : WineDalamudInjector
{
    public LinuxDalamudInjector(
        ILogger<LinuxDalamudInjector> logger,
        DalamudPathService pathService)
        : base(logger, pathService)
    {
    }

    protected override void PreInjectHook()
    {
        ClearLinuxCachedSignatures();
    }

    private void ClearLinuxCachedSignatures()
    {
        var cacheDir = Path.Combine(_pathService.HooksDevPath, "cachedSigs");
        if (!Directory.Exists(cacheDir))
        {
            _logger.LogDebug("[DALAMUD-INJECT] cachedSigs directory not found: {CacheDir}", cacheDir);
            return;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(cacheDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "cs.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deleted++;
                _logger.LogWarning("[DALAMUD-INJECT] Cleared cached signature file: {File}", file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DALAMUD-INJECT] Failed to remove cached signature file: {File}", file);
            }
        }

        if (deleted > 0)
        {
            _logger.LogWarning("[DALAMUD-INJECT] Cleared {Count} cached signature file(s) in {CacheDir}", deleted, cacheDir);
        }
        else
        {
            _logger.LogDebug("[DALAMUD-INJECT] No removable cached signature files found in {CacheDir}", cacheDir);
        }
    }
}
