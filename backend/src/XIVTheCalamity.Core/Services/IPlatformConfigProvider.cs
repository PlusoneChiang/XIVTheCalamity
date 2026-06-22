using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Core.Services;

/// <summary>
/// Strategy provider for platform-specific configuration defaults and validation
/// </summary>
public interface IPlatformConfigProvider
{
    /// <summary>
    /// Check if this provider matches the current operating system platform
    /// </summary>
    bool MatchesPlatform();

    /// <summary>
    /// Apply platform-specific configuration default values
    /// </summary>
    void ApplyPlatformDefaults(AppConfig config);

    /// <summary>
    /// Perform platform-specific configuration validation
    /// </summary>
    void ValidatePlatformConfig(AppConfig config);
}
