
using ReTFO.ThermalOverlay.Factories;

namespace ReTFO.ThermalOverlay.Interfaces;

/// <summary>
/// Main interface used for thermal conversion, this selects the conversion method/mode/type and
///  calls sub converters and generators accordingly
/// </summary>
public interface IThermalConverter
{
    abstract void ConvertItem(string? thisName, ConversionContext context);
}
