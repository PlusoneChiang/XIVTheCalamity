using Microsoft.Extensions.Logging;
using XIVTheCalamity.Game.Models;
using XIVTheCalamity.Game.Patching.ZiPatch;
using XIVTheCalamity.Game.Patching.ZiPatch.Util;

namespace XIVTheCalamity.Game.Services;

/// <summary>
/// Patch install service - applies downloaded patches to game files
/// Reference: XIVLauncher.Common.Patching.RemotePatchInstaller
/// </summary>
public class PatchInstallService
{
    private readonly ILogger<PatchInstallService> _logger;
    
    public PatchInstallService(ILogger<PatchInstallService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Install a single patch file
    /// </summary>
    /// <param name="patchPath">Path to the .patch file</param>
    /// <param name="gamePath">Path to game directory (containing boot/game folders)</param>
    /// <param name="repository">Repository type (Game, Ex1, etc.)</param>
    public void InstallPatch(string patchPath, string gamePath, GameRepository repository)
    {
        if (!File.Exists(patchPath))
            throw new FileNotFoundException("Patch file not found", patchPath);

        // Determine target path based on repository
        var targetPath = GetTargetPath(gamePath, repository);
        
        if (!Directory.Exists(targetPath))
            Directory.CreateDirectory(targetPath);

        _logger.LogInformation("Installing patch: {PatchFile} to {TargetPath}", 
            Path.GetFileName(patchPath), targetPath);

        using var patchFile = ZiPatchFile.FromFileName(patchPath);
        using var store = new SqexFileStreamStore();
        var config = new ZiPatchConfig(targetPath) { Store = store };

        int chunkCount = 0;
        foreach (var chunk in patchFile.GetChunks())
        {
            chunk.ApplyChunk(config);
            chunkCount++;
        }

        _logger.LogInformation("Patch installed: {ChunkCount} chunks applied", chunkCount);
    }

    /// <summary>
    /// Install patch with progress reporting
    /// </summary>
    public void InstallPatchWithProgress(
        string patchPath, 
        string gamePath, 
        GameRepository repository,
        Action<int, int>? progressCallback = null)
    {
        if (!File.Exists(patchPath))
            throw new FileNotFoundException("Patch file not found", patchPath);

        var targetPath = GetTargetPath(gamePath, repository);
        
        if (!Directory.Exists(targetPath))
            Directory.CreateDirectory(targetPath);

        _logger.LogInformation("Installing patch: {PatchFile}", Path.GetFileName(patchPath));

        using var patchFile = ZiPatchFile.FromFileName(patchPath);
        using var store = new SqexFileStreamStore();
        var config = new ZiPatchConfig(targetPath) { Store = store };

        // First pass: count chunks
        var chunks = patchFile.GetChunks().ToList();
        var totalChunks = chunks.Count;

        // Second pass: apply chunks with progress
        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].ApplyChunk(config);
            progressCallback?.Invoke(i + 1, totalChunks);
        }

        _logger.LogInformation("Patch installed: {ChunkCount} chunks applied", totalChunks);
    }

    /// <summary>
    /// Get target path for patch installation based on repository
    /// </summary>
    private string GetTargetPath(string gamePath, GameRepository repository)
    {
        return repository switch
        {
            GameRepository.Boot => Path.Combine(gamePath, "boot"),
            GameRepository.Game => Path.Combine(gamePath, "game"),
            GameRepository.Ex1 => Path.Combine(gamePath, "game"),
            GameRepository.Ex2 => Path.Combine(gamePath, "game"),
            GameRepository.Ex3 => Path.Combine(gamePath, "game"),
            GameRepository.Ex4 => Path.Combine(gamePath, "game"),
            GameRepository.Ex5 => Path.Combine(gamePath, "game"),
            _ => Path.Combine(gamePath, "game")
        };
    }

    /// <summary>
    /// Update version file after patch installation
    /// </summary>
    public void UpdateVersionFile(string gamePath, GameRepository repository, string newVersion)
    {
        var versionFilePath = GetVersionFilePath(gamePath, repository);
        var directory = Path.GetDirectoryName(versionFilePath);
        
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(versionFilePath, newVersion);
        _logger.LogInformation("Updated version file: {Repository} -> {Version}", repository, newVersion);
    }

    /// <summary>
    /// Get version file path for repository
    /// </summary>
    private string GetVersionFilePath(string gamePath, GameRepository repository)
    {
        return repository switch
        {
            GameRepository.Boot => Path.Combine(gamePath, "boot", "ffxivboot.ver"),
            GameRepository.Game => Path.Combine(gamePath, "game", "ffxivgame.ver"),
            GameRepository.Ex1 => Path.Combine(gamePath, "game", "sqpack", "ex1", "ex1.ver"),
            GameRepository.Ex2 => Path.Combine(gamePath, "game", "sqpack", "ex2", "ex2.ver"),
            GameRepository.Ex3 => Path.Combine(gamePath, "game", "sqpack", "ex3", "ex3.ver"),
            GameRepository.Ex4 => Path.Combine(gamePath, "game", "sqpack", "ex4", "ex4.ver"),
            GameRepository.Ex5 => Path.Combine(gamePath, "game", "sqpack", "ex5", "ex5.ver"),
            _ => Path.Combine(gamePath, "game", "ffxivgame.ver")
        };
    }
}
