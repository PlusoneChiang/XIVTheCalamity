namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Wine graphics configuration
/// </summary>
public class WineGraphicsConfig
{
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
}
