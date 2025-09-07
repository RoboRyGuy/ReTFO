
using ReTFO.ThermalOverlay.Factories;

namespace ReTFO.ThermalOverlay.Interfaces;

/// <summary>
/// Interface for generating overlays, for use when converting weapons without sights to thermal weapons
/// </summary>
public interface IOverlayGenerator
{
    public abstract bool GenerateOverlay(string? thisName, ConversionContext context);
}
