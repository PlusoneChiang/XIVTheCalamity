namespace XIVTheCalamity.Dalamud.Models;

/// <summary>
/// Options for Dalamud injection
/// </summary>
public class DalamudInjectionOptions
{
    /// <summary>使用 Pre-release 的 dev 目錄，否則使用正式版目錄。</summary>
    public bool UseLatestPreRelease { get; set; }

    /// <summary>Wait time before injection (milliseconds)</summary>
    public int? InjectionDelayMs { get; set; }
    
    /// <summary>Dalamud delay initialization time (milliseconds)</summary>
    public int? DelayInitializeMs { get; set; }
    
    /// <summary>Do not load any plugins</summary>
    public bool NoPlugin { get; set; }
    
    /// <summary>Do not load third-party plugins</summary>
    public bool NoThirdPartyPlugin { get; set; }

    /// <summary>Main plugin repository URL (passed as DALAMUD_MAIN_REPO_URL)</summary>
    public string? PluginRepoUrl { get; set; }
}
