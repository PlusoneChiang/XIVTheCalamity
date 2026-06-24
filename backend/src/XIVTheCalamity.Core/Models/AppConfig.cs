namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Application configuration
/// </summary>
public class AppConfig
{
    public GameConfig Game { get; set; } = new();
    public WineConfig? Wine { get; set; }  // macOS only
    public WineGraphicsConfig? WineGraphics { get; set; }  // macOS only
    public WinePerformanceConfig? WinePerformance { get; set; }  // macOS only
    public WineCompatConfig? WineCompat { get; set; }  // macOS only
    public ProtonGeConfig? ProtonGe { get; set; }  // Linux only (GE-Proton)
    public DiscordRpcConfig DiscordRpc { get; set; } = new();
    public DalamudConfig Dalamud { get; set; } = new();
    public LauncherConfig Launcher { get; set; } = new();

    /// <summary>
    /// Create a default configuration instance
    /// </summary>
    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Game = new GameConfig
            {
                GamePath = "",
                Region = "TraditionalChinese"
            },
            Dalamud = new DalamudConfig
            {
                Enabled = false,
                InjectDelay = 5000,
                SafeMode = false,
                PluginRepoUrl = DalamudConfig.ManagedPluginRepoUrl
            },
            DiscordRpc = new DiscordRpcConfig(),
            Launcher = new LauncherConfig
            {
                EncryptedArguments = true,
                ExitWithGame = true,
                NonZeroExitError = true,
                DevelopmentMode = false,
                EnablePreRelease = false
            }
        };
    }
}
