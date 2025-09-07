
using ReTFO.ThermalOverlay.Config;
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Generates a thermal overlay using the PDW's config as a base. Accepts one parameter dictating whether
///  to use the PDW's default texture or to use the transparent variant stored in file. Defaults to transparent.
/// PR([Transparent/Opaque])
/// </summary>
public class MaterialGenerator_PR : IMaterialGenerator
{
    public const string Name = "PR";

    // Material config for the PR (minus reticule configs) modified for use with the ThermalOverlay shader
    public static readonly MaterialConfig PRMaterialConfig = new()
    {
        Zoom = .25f,
        RatioAdjust = .89f,
        ScreenIntensity = .152f,

        HeatTex = "Load(ThermalOverlay, Thremal_Gradient_EVIL.png)", // Yes, the actual file, in-game, is named "thremal"
        HeatFalloff = .01f,
        FogFalloff = .5f,
        BackgroundTemp = 0f,
        AmbientColorFactor = 5f,
        AlbedoColorFactor = .5f,
        AmbientTemp = .15f,
        OcclusionHeat = .25f,
        BodyOcclusionHeat = 1f,

        DistortionTex = "Load(ThermalOverlay, Scanline.png)",
        // DistortionCenter = .8f,
        DistortionScale = 1f,
        DistortionSpeed = 1f,

        DistortionSignal = "Load(ThermalOverlay, ThermalDistortionSignal.png)",
        DistortionSignalSpeed = .025f,
        DistortionMin = .1f,
        DistortionMax = .4f,

        DistortionMinShadowEnemies = .3f,
        DistortionMaxShadowEnemies = .9f,
        DistortionSignalSpeedShadowEnemies = .05f,
        ShadowEnemyFresnel = 50,
        ShadowEnemyHeat = .6f,

        MainTex = "red", // The vanilla value is black, but the thermal shader requires red to function correctly
        // ReticuleA, B, C
        // Reticule colors
        // Sight Dirt, lit glass
        // Axises, flip, distances, sizes, etc

        FPSRenderingEnabled = true,
    };

    public virtual Material GenerateMaterial(string? thisName, ConversionContext context)
    {
        // State checks
        Shader? thermalOverlayShader = context.Plugin.AssetBundle.ThermalOverlayShader;
        if (thermalOverlayShader == null)
        {
            context.Log.LogError("Failed to generate material; overlay shader failed to load");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        // Set up parameters
        bool bTransparent = true;
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length > 0)
        {
            string item = parameters[0];
            if (item.Length == 0) { }
            else if (item == "Transparent") bTransparent = true;
            else if (item == "Opaque")      bTransparent = false;
            else context.Log.LogError($"MaterialGenerator_PR passed bad first parameter: {item}, expected \"Transparent\" or \"Opaque\"");
        }
        if (parameters.Length > 1)
            context.Log.LogWarning($"MaterialGenerator_PR ignoring extra parameters: {FactoryManager.FormatParams(parameters[1..])}");

        // Create the material and apply configs!
        Material thermalMat = new(thermalOverlayShader)
        {
            name = $"ThermalOverlay - PR ({(bTransparent ? "Transparent" : "Opaque")})",
            hideFlags = HideFlags.HideAndDontSave
        };

        MaterialConfig clone = PRMaterialConfig.Clone();
        if (bTransparent) clone.HeatTex = clone.HeatTex.Value.Replace(".png", "_Transparent.png");
        clone.Zoom = 0f;
        clone.OffAngleFade = 0f; // OffAngleFade rarely looks good, and often looks bad
        clone.ApplyAllWithDefaults(thermalMat, context);
        return thermalMat;
    }
}
