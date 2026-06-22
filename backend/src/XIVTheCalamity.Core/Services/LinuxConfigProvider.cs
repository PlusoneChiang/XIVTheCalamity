using System.Runtime.InteropServices;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Core.Services;

/// <summary>
/// Linux specific config provider (ProtonGe settings)
/// </summary>
public class LinuxConfigProvider : IPlatformConfigProvider
{
    public bool MatchesPlatform() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public void ApplyPlatformDefaults(AppConfig config)
    {
        config.ProtonGe ??= new ProtonGeConfig
        {
            DxvkHudEnabled = false,
            MaxFramerate = 0,
            EsyncEnabled = true,
            FsyncEnabled = true,
            GameModeEnabled = true,
            WineDebug = ""
        };
    }

    public void ValidatePlatformConfig(AppConfig config)
    {
        // No custom validations in the legacy code for ProtonGe, but we satisfy the interface
    }
}
