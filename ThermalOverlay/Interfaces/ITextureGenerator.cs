
using ReTFO.ThermalOverlay.Factories;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Interfaces;

/// <summary>
/// Interface for generating a texture, typically a texture 2D. Used for material configs
/// </summary>
public interface ITextureGenerator
{
    public abstract Texture GenerateTexture(string? thisName, ConversionContext context);
}
