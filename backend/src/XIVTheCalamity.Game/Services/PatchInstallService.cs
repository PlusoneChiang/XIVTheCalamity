using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Game.Models;
using XIVTheCalamity.Game.Patching.ZiPatch;
using XIVTheCalamity.Game.Patching.ZiPatch.Util;

namespace XIVTheCalamity.Game.Services;

public enum HashCheckResult
{
    Pass,
    BadHash,
    BadLength,
    CannotParse,
    CrcMismatch,
    UnknownHashType
}

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
    /// Verify patch file integrity after download
    /// Reference: XIVLauncher.Common.Game.Patch.PatchManager.CheckPatchValidity
    /// </summary>
    public HashCheckResult VerifyPatchHash(PatchInfo patch, string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            return HashCheckResult.BadLength;

        if (patch.HashType != "sha1")
        {
            // Boot patches: validate ZiPatch CRC32 checksums
            if (patch.Repository == GameRepository.Boot)
            {
                try
                {
                    using var fileStream = fileInfo.OpenRead();
                    using var patchFile = new ZiPatchFile(fileStream, true);
                    foreach (var chunk in patchFile.GetChunks())
                    {
                        if (!chunk.IsChecksumValid)
                        {
                            _logger.LogError("Boot patch {Patch} has invalid checksum in {ChunkType} chunk",
                                patch.FileName, chunk.ChunkType);
                            return HashCheckResult.CrcMismatch;
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Could not parse boot patch {Patch}", patch.FileName);
                    return HashCheckResult.CannotParse;
                }
                return HashCheckResult.Pass;
            }

            // No hash info available — skip verification
            if (string.IsNullOrEmpty(patch.HashType))
            {
                _logger.LogDebug("No hash info for {Patch}, skipping verification", patch.FileName);
                return HashCheckResult.Pass;
            }

            _logger.LogWarning("Unknown HashType: {HashType} for {Patch}", patch.HashType, patch.FileName);
            return HashCheckResult.UnknownHashType;
        }

        // SHA1 block verification
        using var stream = fileInfo.OpenRead();

        if (stream.Length != patch.Size)
        {
            _logger.LogError("Patch {Patch} size mismatch: expected {Expected}, got {Actual}",
                patch.FileName, patch.Size, stream.Length);
            return HashCheckResult.BadLength;
        }

        if (patch.Hashes.Length == 0 || patch.HashBlockSize <= 0)
        {
            _logger.LogDebug("No hash blocks for {Patch}, skipping block verification", patch.FileName);
            return HashCheckResult.Pass;
        }

        var parts = (int)Math.Ceiling((double)patch.Size / patch.HashBlockSize);
        var block = new byte[patch.HashBlockSize];

        for (var i = 0; i < parts; i++)
        {
            var read = stream.Read(block, 0, (int)patch.HashBlockSize);

            byte[] dataToHash;
            if (read < patch.HashBlockSize)
            {
                dataToHash = new byte[read];
                Array.Copy(block, 0, dataToHash, 0, read);
            }
            else
            {
                dataToHash = block;
            }

            var hash = SHA1.HashData(dataToHash);
            var hashStr = Convert.ToHexString(hash).ToLowerInvariant();

            if (i < patch.Hashes.Length && hashStr != patch.Hashes[i])
            {
                _logger.LogError("Patch {Patch} block {Block} hash mismatch: expected {Expected}, got {Actual}",
                    patch.FileName, i, patch.Hashes[i], hashStr);
                return HashCheckResult.BadHash;
            }
        }

        return HashCheckResult.Pass;
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

    /// <summary>
    /// Backup all .ver files to .bck after patching completes
    /// Reference: XIVLauncher.Common.Patching.RemotePatchInstaller.VerToBck
    /// </summary>
    public void BackupVersionFiles(string gamePath)
    {
        var repositories = new[]
        {
            GameRepository.Boot, GameRepository.Game,
            GameRepository.Ex1, GameRepository.Ex2, GameRepository.Ex3,
            GameRepository.Ex4, GameRepository.Ex5
        };

        foreach (var repo in repositories)
        {
            var verPath = GetVersionFilePath(gamePath, repo);
            if (!File.Exists(verPath))
                continue;

            var bckPath = Path.ChangeExtension(verPath, ".bck");
            try
            {
                File.Copy(verPath, bckPath, overwrite: true);
                _logger.LogDebug("Backed up version file: {Ver} -> {Bck}", verPath, bckPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to backup version file: {Ver}", verPath);
            }
        }

        _logger.LogInformation("Version file backup (.ver -> .bck) completed");
    }
}
