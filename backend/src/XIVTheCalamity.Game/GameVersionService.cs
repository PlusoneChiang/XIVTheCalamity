using Microsoft.Extensions.Logging;
using XIVTheCalamity.Game.Models;

namespace XIVTheCalamity.Game.Services;

/// <summary>
/// Game version management service
/// </summary>
public class GameVersionService
{
    private readonly ILogger<GameVersionService> _logger;

    public GameVersionService(ILogger<GameVersionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get local game version information
    /// </summary>
    public GameVersionInfo GetLocalVersions(string gamePath)
    {
        var versionInfo = new GameVersionInfo();

        if (!Directory.Exists(gamePath))
        {
            _logger.LogWarning("Game directory does not exist: {GamePath}", gamePath);
            return versionInfo;
        }

        // Read version files for each repository
        ReadVersion(Path.Combine(gamePath, "boot"), "ffxivboot.ver", GameRepository.Boot, versionInfo);
        ReadVersion(Path.Combine(gamePath, "game"), "ffxivgame.ver", GameRepository.Game, versionInfo);
        ReadVersion(Path.Combine(gamePath, "game/sqpack/ex1"), "ex1.ver", GameRepository.Ex1, versionInfo);
        ReadVersion(Path.Combine(gamePath, "game/sqpack/ex2"), "ex2.ver", GameRepository.Ex2, versionInfo);
        ReadVersion(Path.Combine(gamePath, "game/sqpack/ex3"), "ex3.ver", GameRepository.Ex3, versionInfo);
        ReadVersion(Path.Combine(gamePath, "game/sqpack/ex4"), "ex4.ver", GameRepository.Ex4, versionInfo);
        ReadVersion(Path.Combine(gamePath, "game/sqpack/ex5"), "ex5.ver", GameRepository.Ex5, versionInfo);

        return versionInfo;
    }

    /// <summary>
    /// Read single version file
    /// </summary>
    private void ReadVersion(string basePath, string fileName, GameRepository repository, GameVersionInfo versionInfo)
    {
        var filePath = Path.Combine(basePath, fileName);

        if (!File.Exists(filePath))
        {
            _logger.LogDebug("Version file does not exist: {FilePath}", filePath);
            // Use base version
            versionInfo.SetVersion(repository, new GameVersion
            {
                Repository = repository,
                Version = "2012.01.01.0000.0000"
            });
            return;
        }

        try
        {
            var versionString = File.ReadAllText(filePath).Trim();
            var version = new GameVersion
            {
                Repository = repository,
                Version = versionString,
                FilePath = filePath
            };

            versionInfo.SetVersion(repository, version);
            _logger.LogInformation("Read version: {Repository} = {Version}", repository, versionString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read version file: {FilePath}", filePath);
            // Use base version
            versionInfo.SetVersion(repository, new GameVersion
            {
                Repository = repository,
                Version = "2012.01.01.0000.0000"
            });
        }
    }

    /// <summary>
    /// Update version file
    /// </summary>
    public async Task UpdateVersionFileAsync(string gamePath, GameRepository repository, string newVersion)
    {
        var filePath = GetVersionFilePath(gamePath, repository);
        var directory = Path.GetDirectoryName(filePath);

        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, newVersion);
        _logger.LogInformation("Updated version file: {Repository} -> {Version}", repository, newVersion);
    }

    /// <summary>
    /// Get version file path
    /// </summary>
    private string GetVersionFilePath(string gamePath, GameRepository repository)
    {
        return repository switch
        {
            GameRepository.Boot => Path.Combine(gamePath, "boot/ffxivboot.ver"),
            GameRepository.Game => Path.Combine(gamePath, "game/ffxivgame.ver"),
            GameRepository.Ex1 => Path.Combine(gamePath, "game/sqpack/ex1/ex1.ver"),
            GameRepository.Ex2 => Path.Combine(gamePath, "game/sqpack/ex2/ex2.ver"),
            GameRepository.Ex3 => Path.Combine(gamePath, "game/sqpack/ex3/ex3.ver"),
            GameRepository.Ex4 => Path.Combine(gamePath, "game/sqpack/ex4/ex4.ver"),
            GameRepository.Ex5 => Path.Combine(gamePath, "game/sqpack/ex5/ex5.ver"),
            _ => throw new ArgumentException($"Unknown repository type: {repository}")
        };
    }

    /// <summary>
    /// Get repository name (for API requests)
    /// </summary>
    public string GetRepositoryName(GameRepository repository)
    {
        return repository switch
        {
            GameRepository.Boot => "ffxivboot",
            GameRepository.Game => "ffxivgame",
            GameRepository.Ex1 => "ex1",
            GameRepository.Ex2 => "ex2",
            GameRepository.Ex3 => "ex3",
            GameRepository.Ex4 => "ex4",
            GameRepository.Ex5 => "ex5",
            _ => throw new ArgumentException($"Unknown repository type: {repository}")
        };
    }
}
