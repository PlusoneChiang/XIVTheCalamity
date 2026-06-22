using System.Runtime.InteropServices;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Core.Services;

/// <summary>
/// macOS specific config provider (Wine settings)
/// </summary>
public class MacOSConfigProvider : IPlatformConfigProvider
{
    public bool MatchesPlatform() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public void ApplyPlatformDefaults(AppConfig config)
    {
        config.Wine ??= new WineConfig
        {
            MetalFxSpatialEnabled = false,
            MetalFxSpatialFactor = 2.0,
            Metal3PerformanceOverlay = false,
            HudScale = 1.0,
            NativeResolution = false,
            MaxFramerate = 60,
            AudioRouting = false,
            Msync = true,
            WineDebug = "",
            UseHomeAlias = false,
            LeftOptionIsAlt = true,
            RightOptionIsAlt = true,
            LeftCommandIsCtrl = true,
            RightCommandIsCtrl = true,
            ImeCandidatePositionX = 25,
            ImeCandidatePositionY = 85
        };

        config.WineGraphics ??= new WineGraphicsConfig
        {
            MetalFxSpatialEnabled = config.Wine.MetalFxSpatialEnabled,
            MetalFxSpatialFactor = config.Wine.MetalFxSpatialFactor,
            Metal3PerformanceOverlay = config.Wine.Metal3PerformanceOverlay,
            HudScale = config.Wine.HudScale,
            NativeResolution = config.Wine.NativeResolution,
            MaxFramerate = config.Wine.MaxFramerate
        };

        config.WinePerformance ??= new WinePerformanceConfig
        {
            Msync = config.Wine.Msync,
            WineDebug = config.Wine.WineDebug
        };

        config.WineCompat ??= new WineCompatConfig
        {
            AudioRouting = config.Wine.AudioRouting,
            UseHomeAlias = config.Wine.UseHomeAlias,
            LeftOptionIsAlt = config.Wine.LeftOptionIsAlt,
            RightOptionIsAlt = config.Wine.RightOptionIsAlt,
            LeftCommandIsCtrl = config.Wine.LeftCommandIsCtrl,
            RightCommandIsCtrl = config.Wine.RightCommandIsCtrl,
            ImeCandidatePositionX = config.Wine.ImeCandidatePositionX,
            ImeCandidatePositionY = config.Wine.ImeCandidatePositionY
        };
    }

    public void ValidatePlatformConfig(AppConfig config)
    {
        if (config.Wine != null)
        {
            if (config.Wine.MetalFxSpatialFactor < 1.0 || config.Wine.MetalFxSpatialFactor > 4.0)
            {
                throw new ArgumentException("MetalFxSpatialFactor must be between 1.0 and 4.0");
            }

            if (config.Wine.MaxFramerate < 30 || config.Wine.MaxFramerate > 240)
            {
                throw new ArgumentException("MaxFramerate must be between 30 and 240");
            }

            if (config.Wine.ImeCandidatePositionX < 0 || config.Wine.ImeCandidatePositionX > 100)
            {
                throw new ArgumentException("ImeCandidatePositionX must be between 0 and 100");
            }

            if (config.Wine.ImeCandidatePositionY < 0 || config.Wine.ImeCandidatePositionY > 100)
            {
                throw new ArgumentException("ImeCandidatePositionY must be between 0 and 100");
            }
        }
    }
}
