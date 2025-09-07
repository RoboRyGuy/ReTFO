
using ReTFO.ThermalOverlay.Config;
using ReTFO.ThermalOverlay.Interfaces;
using System.Text.Json;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Generates a material by loading default values as json from a file. 
/// Uses the GameData directory as the base path
/// Load([SubDirectory1], [SubDirectory2], ..., FileName)
/// </summary>
public class MaterialGenerator_Load : IMaterialGenerator
{
    public const string Name = "Load";
    public static string GameDataPath => Path.Combine(Path.GetDirectoryName(BepInEx.Paths.PluginPath) ?? "", "GameData");

    public Material GenerateMaterial(string? thisName, ConversionContext context)
    {
        Shader? thermalOverlayShader = context.Plugin.AssetBundle.ThermalOverlayShader;
        if (thermalOverlayShader == null)
        {
            context.Log.LogError("Failed to generate material; overlay shader failed to load");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length == 0)
        {
            context.Log.LogDebug($"MaterialGenerator_Load expected a file path in its parameters, but instead got nothing");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        string filename = Path.Combine(parameters);
        string filepath = Path.Combine(GameDataPath, filename);

        if (File.Exists(filepath))
        {
            string jsonText = File.ReadAllText(filepath);
            MaterialConfig config;
            try
            {
                config = JsonSerializer.Deserialize<MaterialConfig>(jsonText);
            }
            catch (JsonException)
            {
                context.Log.LogError($"MaterialGenerator_Load failed to deserialize material config from file \"{filename}\"");
                throw;
            }

            Material mat = new(thermalOverlayShader)
            {
                name = $"Thermal Overlay - {filename}",
                hideFlags = HideFlags.HideAndDontSave,
            };
            config.ApplyAllWithDefaults(mat, context);
            return mat;
        }
        else
        {
            context.Log.LogError($"MaterialGenerator_Load failed to load file; file not found\n - File name: {filename}\n - Full path: {filepath}");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }
    }
}
