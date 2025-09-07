
namespace ReTFO.ThermalOverlay.Config;

/// <summary>
/// Simple wrapper to make conversion to/from JSON easier
/// </summary>
public class ConfigFile
{
    public List<ConfigFileEntry>? ConfigEntries { get; set; } = null;
}
