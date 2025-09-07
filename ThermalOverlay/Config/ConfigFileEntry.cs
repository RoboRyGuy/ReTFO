
namespace ReTFO.ThermalOverlay.Config;

/// <summary>
/// Wraps a ThermalConfig to associate it with IDs and an optional name (to make debugging easier)
/// </summary>
public struct ConfigFileEntry
{
    public ConfigFileEntry() { }

    // Name of this config, purely for ease of use
    public string? ConfigName { get; set; } = null;

    // ID used to associate this config with gear. By default, the GearRangeID's checksum for the item
    public List<uint>? IDs { get; set; } = null;

    // ThermalConfig to apply
    public ThermalConfig? Config { get; set; } = null;
}
