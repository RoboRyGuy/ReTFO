
namespace ReTFO.ThermalOverlay.Config;

// Wraps config data for things that can be granted a thermal overlay
public class ThermalConfig
{
    // Which thermal converter to use to apply this configuration
    public string? Handler { get; set; } = null;

    // Which sight converter to use, if needed
    public string? SightConverter { get; set; } = null;

    // Which overlay generator to use, if needed
    public string? OverlayGenerator { get; set; } = null;

    // Extra config information for overlay generation
    public OverlayConfig OverlayConfig { get; set; } = new();

    // Which material generator to use when creating a thermal material from scratch
    public string? MaterialGenerator { get; set; } = null;

    // Properties to apply to the material on the overlay
    public MaterialConfig MaterialConfig { get; set; } = new();
}
