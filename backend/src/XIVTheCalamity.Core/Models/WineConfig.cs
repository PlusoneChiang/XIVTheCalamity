namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Wine graphics configuration
/// </summary>
public class WineConfig
{
    /// <summary>
    /// Enable DXMT (DirectX to Metal)
    /// </summary>
    public bool DxmtEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable MetalFX spatial upscaling
    /// </summary>
    public bool MetalFxSpatialEnabled { get; set; } = false;
    
    /// <summary>
    /// MetalFX spatial upscaling factor (1.0 - 4.0, integer multiples)
    /// </summary>
    public double MetalFxSpatialFactor { get; set; } = 2.0;
    
    /// <summary>
    /// Enable Metal3 performance overlay (DXMT HUD)
    /// </summary>
    public bool Metal3PerformanceOverlay { get; set; } = false;
    
    /// <summary>
    /// HUD scale (0.5 - 2.0)
    /// </summary>
    public double HudScale { get; set; } = 1.0;
    
    /// <summary>
    /// Use native resolution (Retina mode, no scaling)
    /// true = native resolution (high quality but higher performance requirement)
    /// false = use macOS scaling (lower resolution but better performance)
    /// </summary>
    public bool NativeResolution { get; set; } = false;
    
    /// <summary>
    /// Maximum framerate (30 - 240)
    /// </summary>
    public int MaxFramerate { get; set; } = 60;
    
    /// <summary>
    /// Enable audio routing
    /// </summary>
    public bool AudioRouting { get; set; } = false;
    
    /// <summary>
    /// Enable Esync synchronization
    /// </summary>
    public bool EsyncEnabled { get; set; } = true;
    
    /// <summary>
    /// Enable Fsync synchronization (Linux only)
    /// </summary>
    public bool FsyncEnabled { get; set; } = false;
    
    /// <summary>
    /// Enable Msync synchronization
    /// </summary>
    public bool Msync { get; set; } = true;
    
    /// <summary>
    /// Wine debug flags (e.g., "-all,+module" or empty to disable)
    /// </summary>
    public string WineDebug { get; set; } = "";
    
    /// <summary>
    /// Map left Option key to Alt (macOS)
    /// </summary>
    public bool LeftOptionIsAlt { get; set; } = true;
    
    /// <summary>
    /// Map right Option key to Alt (macOS)
    /// </summary>
    public bool RightOptionIsAlt { get; set; } = true;
    
    /// <summary>
    /// Map left Command key to Ctrl (macOS)
    /// </summary>
    public bool LeftCommandIsCtrl { get; set; } = true;
    
    /// <summary>
    /// Map right Command key to Ctrl (macOS)
    /// </summary>
    public bool RightCommandIsCtrl { get; set; } = true;
}
