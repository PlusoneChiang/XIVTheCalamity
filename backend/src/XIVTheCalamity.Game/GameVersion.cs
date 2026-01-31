namespace XIVTheCalamity.Game.Models;

/// <summary>
/// Game repository type
/// </summary>
public enum GameRepository
{
    Boot,      // ffxivboot
    Game,      // ffxivgame (ex0)
    Ex1,       // Heavensward
    Ex2,       // Stormblood
    Ex3,       // Shadowbringers
    Ex4,       // Endwalker
    Ex5        // Dawntrail
}

/// <summary>
/// Game version information
/// </summary>
public class GameVersion
{
    /// <summary>
    /// Version string, format: YYYY.MM.DD.XXXX.YYYY
    /// </summary>
    public string Version { get; set; } = "2012.01.01.0000.0000";

    /// <summary>
    /// Repository type
    /// </summary>
    public GameRepository Repository { get; set; }

    /// <summary>
    /// .ver file path
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Check if this is a base version (fresh install)
    /// </summary>
    public bool IsBaseVersion => Version.StartsWith("2012.01.01");

    /// <summary>
    /// Compare version (string comparison)
    /// </summary>
    public int CompareTo(GameVersion other)
    {
        return string.Compare(Version, other.Version, StringComparison.Ordinal);
    }

    /// <summary>
    /// Check if update is needed
    /// </summary>
    public bool NeedsUpdate(GameVersion targetVersion)
    {
        return CompareTo(targetVersion) < 0;
    }

    public override string ToString() => $"{Repository}: {Version}";
}

/// <summary>
/// Version information for all repositories
/// </summary>
public class GameVersionInfo
{
    public Dictionary<GameRepository, GameVersion> Versions { get; set; } = new();

    /// <summary>
    /// Get version for specified repository
    /// </summary>
    public GameVersion? GetVersion(GameRepository repo)
    {
        return Versions.TryGetValue(repo, out var version) ? version : null;
    }

    /// <summary>
    /// Set version for specified repository
    /// </summary>
    public void SetVersion(GameRepository repo, GameVersion version)
    {
        Versions[repo] = version;
    }

    /// <summary>
    /// Check if this is a fresh install (all versions are base versions)
    /// </summary>
    public bool IsFreshInstall()
    {
        return Versions.Values.All(v => v.IsBaseVersion);
    }
}
