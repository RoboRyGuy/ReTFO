
using ReTFO.ThermalOverlay.Factories;

namespace ReTFO.ThermalOverlay.Interfaces;

/// <summary>
/// Interface for converting an existing sight object to a thermal sight
/// </summary>
public interface ISightConverter
{
    public abstract bool ConvertSight(string? thisName, ConversionContext context);
}
