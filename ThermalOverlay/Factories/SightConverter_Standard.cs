
using ReTFO.ThermalOverlay.Config;
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Converts a sight by detecing it it is already a thermal sight or a standard one, then applying
///  changes accordingly. You can set which mode it should use, overriding builtin decision-making.
/// Standard([Thermal/NonThermal])
/// </summary>
public class SightConverter_Standard : ISightConverter
{
    public const string Name = "Standard";

    public virtual bool ConvertSight(string? thisName, ConversionContext context)
    {
        // Ensure everything is loaded in
        Shader? thermalShader = context.Plugin.AssetBundle.ThermalOverlayShader;
        if (thermalShader == null)
        {
            context.Log.LogError("SightConverter_Standard failed; failed to load thermal shader");
            return false;
        }

        // A renderer must be provided
        if (context.Renderer == null)
        {
            context.Log.LogError("SightConverter_Standard failed; context.Renderer was null");
            return false;
        }

        bool isThermal = context.Renderer.sharedMaterial.shader.name.Contains("Thermal", StringComparison.OrdinalIgnoreCase);
        bool isTool = false; // TODO: Detect tools

        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length > 0)
        {
            string item = parameters[0];
            if (item.Length == 0) { }
            else if (item == "Thermal") isThermal = true;
            else if (item == "NonThermal") isThermal = false;
            else context.Log.LogError($"SightConverter_Standard expected either \"Thermal\" or \"NonThermal\" for its first parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 1)
            context.Log.LogWarning($"SightConverter_Standard ignoring extra parameters: {FactoryManager.FormatParams(parameters[1..])}");

        bool isSuccessful;
        if (isThermal) isSuccessful = ConvertThermalSight(context);
        else           isSuccessful = ConvertNonThermalSight(context);

        if (isSuccessful)
        {
            if (context.Material == null)
                throw new NullReferenceException("context.Material should not be null here!");

            context.Material.SetFloat(MaterialConfig.CenterWhenUnscoped_Name, 1f);
            if (isTool)
            {
                context.Material.SetFloat(MaterialConfig.UncenterWhenScoped_Name, 0f);
                context.Material.mainTexture = context.Factory.RunTextureGenerator("red", context);
            }

            context.Log.LogDebug($"SightConverter_Standard successfully converted sight on item \"{context.Item.name}\"");
        }

        return isSuccessful;
    }

    protected virtual bool ConvertThermalSight(ConversionContext context)
    {
        if (context.Renderer == null) // This should never trigger
            throw new NullReferenceException("context.Renderer should not be null here!");

        context.Material = context.Renderer.sharedMaterial;
        context.Material.shader = context.Plugin.AssetBundle.ThermalOverlayShader;
        context.Material.mainTexture = Texture2D.redTexture;

        // Zoom is calculated differently, this estimates the new value
        context.Material.SetFloat(
            MaterialConfig.Zoom_Name, 
            Mathf.Clamp(context.Material.GetFloat(MaterialConfig.Zoom_Name) - .5f, 0f, 1f)
        );
        return true;
    }

    protected virtual bool ConvertNonThermalSight(ConversionContext context)
    {
        if (context.Renderer == null) // This should never trigger
            throw new NullReferenceException("context.Renderer should not be null here!"); 

        context.Material = context.Plugin.FactoryManager.RunMaterialGenerator(context.Config?.MaterialGenerator, context);
        context.Material.mainTexture = context.Renderer.sharedMaterial.mainTexture;

        // Copy keywords, namely the FPS_RENDERING_ENABLED keyword
        foreach (string keyword in context.Renderer.sharedMaterial.GetShaderKeywords())
            context.Material.SetKeywordEnabled(keyword, true);
        
        context.Renderer.sharedMaterials = Enumerable.Prepend(context.Renderer.sharedMaterials, context.Material).ToArray();
        return true;
    }

}
