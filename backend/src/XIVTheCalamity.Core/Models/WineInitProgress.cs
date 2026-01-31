namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Wine initialization stage
/// </summary>
public enum WineInitStage
{
    /// <summary>
    /// Checking if Wine Prefix exists
    /// </summary>
    Checking,
    
    /// <summary>
    /// Creating Wine Prefix (wineboot)
    /// </summary>
    CreatingPrefix,
    
    /// <summary>
    /// Configuring MediaFoundation (GStreamer)
    /// </summary>
    ConfiguringMedia,
    
    /// <summary>
    /// Installing fonts (Noto Sans TC)
    /// </summary>
    InstallingFonts,
    
    /// <summary>
    /// Setting locale (zh-TW)
    /// </summary>
    SettingLocale,
    
    /// <summary>
    /// Complete
    /// </summary>
    Complete
}

/// <summary>
/// Wine initialization progress
/// </summary>
public class WineInitProgress
{
    /// <summary>
    /// Current stage
    /// </summary>
    public WineInitStage Stage { get; set; }
    
    /// <summary>
    /// i18n message key for frontend translation
    /// </summary>
    public string MessageKey { get; set; } = "";
    
    /// <summary>
    /// Parameters for message interpolation
    /// </summary>
    public Dictionary<string, object>? Params { get; set; }
    
    /// <summary>
    /// Is complete
    /// </summary>
    public bool IsComplete { get; set; }
    
    /// <summary>
    /// Has error
    /// </summary>
    public bool HasError { get; set; }
    
    /// <summary>
    /// i18n error message key for frontend translation
    /// </summary>
    public string? ErrorMessageKey { get; set; }
    
    /// <summary>
    /// Parameters for error message interpolation
    /// </summary>
    public Dictionary<string, object>? ErrorParams { get; set; }
}
