using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Loads a texture from file. The input is the path to load, relative to the Assets folder.
/// Load([SubDirectory1], [SubDirectory2], ..., Filename)
/// </summary>
public class TextureGenerator_Load : ITextureGenerator
{
    public const string Name = "Load";
    public static string TexturesPath => AssetBundle.AssetDirectory;

    public virtual Texture GenerateTexture(string? thisName, ConversionContext context)
    {
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length == 0)
        {
            context.Log.LogDebug($"TextureGenerator_File expected a file path in its parameters, but instead got nothing");
            return context.Factory.RunTextureGenerator(null, context);
        }

        string filename = Path.Combine(parameters);
        string filepath = Path.Combine(TexturesPath, filename);

        if (File.Exists(filepath))
        {
            Texture2D tex = new Texture2D(2, 2); // LoadImage will overwrite this
            tex.name = filename;
            tex.LoadImage(File.ReadAllBytes(filepath));
            tex.Apply();
            return tex;
        }
        else
        {
            context.Log.LogError($"TextureGenerator_File failed to load file; file not found\n - File name: {filename}\n - Full path: {filepath}");
            return context.Factory.RunTextureGenerator(null, context);
        }
    }
}
