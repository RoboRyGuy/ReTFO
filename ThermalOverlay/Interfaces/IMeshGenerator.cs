
using ReTFO.ThermalOverlay.Factories;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Interfaces;

/// <summary>
/// Interface for generating a mesh, typically (but not explicitly) for use with Thermal Overlays
/// </summary>
public interface IMeshGenerator
{
    public abstract Mesh GenerateMesh(string? thisName, ConversionContext context);
}
