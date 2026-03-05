namespace XIVTheCalamity.Game.Models;

/// <summary>
/// Patch state for download-and-install flow
/// </summary>
public enum PatchState
{
    Nothing,
    IsDownloading,
    Downloaded,
    IsInstalling,
    Finished
}

/// <summary>
/// Patch download with state tracking
/// </summary>
public class PatchDownload
{
    public PatchInfo Patch { get; set; } = null!;
    public PatchState State { get; set; } = PatchState.Nothing;
}

/// <summary>
/// Patch information (parsed from Taiwan API)
/// </summary>
public class PatchInfo
{
    /// <summary>
    /// Patch filename
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Download URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Hash type (e.g. "sha1")
    /// </summary>
    public string HashType { get; set; } = string.Empty;

    /// <summary>
    /// Hash block size for block-based verification
    /// </summary>
    public long HashBlockSize { get; set; }

    /// <summary>
    /// Hash values (one per block)
    /// </summary>
    public string[] Hashes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Version number
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Repository type
    /// </summary>
    public GameRepository Repository { get; set; }

    /// <summary>
    /// Whether this is a full installer (2012.01.01)
    /// </summary>
    public bool IsFullInstaller => Version.StartsWith("2012.01.01");

    /// <summary>
    /// Local path after download
    /// </summary>
    public string? LocalPath { get; set; }

    public override string ToString() => $"{FileName} ({Repository}) - {Version}";
}
