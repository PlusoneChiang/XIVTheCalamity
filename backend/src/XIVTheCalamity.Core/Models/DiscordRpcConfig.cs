namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Discord Rich Presence bridge configuration (macOS Wine).
/// </summary>
public class DiscordRpcConfig
{
    /// <summary>
    /// Enable Discord Rich Presence bridge integration.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Automatically install or repair bridge integration before game launch.
    /// </summary>
    public bool AutoInstall { get; set; } = true;

    /// <summary>
    /// Bridge version channel. "latest" uses latest release download URL.
    /// </summary>
    public string BridgeVersion { get; set; } = "latest";
}
