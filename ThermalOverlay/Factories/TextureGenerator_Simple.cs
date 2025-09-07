
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// A simple texture generator which, when created, stores a lambda, and uses that lambda to generate the texture.
/// Intended for very straightforward generators, namely fetching existing textures from elsewhere.
/// </summary>
public class TextureGenerator_Simple : ITextureGenerator
{
    Func<Texture> Func;

    public TextureGenerator_Simple(Func<Texture> func) {  Func = func; }
    public virtual Texture GenerateTexture(string? thisName, ConversionContext context)
    {
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length > 0)
            context.Log.LogWarning($"TextureGenerator_Simple ignoring parameters: {FactoryManager.FormatParams(parameters[0..])}");
        return Func();
    }
}
