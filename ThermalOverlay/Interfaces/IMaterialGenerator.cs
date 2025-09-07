
using ReTFO.ThermalOverlay.Factories;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Interfaces;

/// <summary>
/// Interface for generating materials (typically thermal materials)
/// </summary>
public interface IMaterialGenerator
{
    public abstract Material GenerateMaterial(string? thisName, ConversionContext context);
}
