
using Gear;
using ReTFO.ThermalOverlay.Config;
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Generates a thermal material using item properties and some deterministic randomness.
/// </summary>
public class MaterialGenerator_Auto : IMaterialGenerator
{
    public const string Name = "Auto";

    public readonly static List<string> ThermalTextures = new()
    {   // This order has been chosen to be the most interesting
        "rainbow.png",
        "Thremal_Gradient_FLIR.png",
        "blackhot.png",
        "lava.png",
        "whitehot.png",
        "Thremal_Gradient_EVIL.png",
    };

    public virtual Material GenerateMaterial(string? thisName, ConversionContext context)
    {
        // Ensure everything is loaded in
        Shader? thermalOverlayShader = context.Plugin.AssetBundle.ThermalOverlayShader;
        if (thermalOverlayShader == null)
        {
            context.Log.LogError("SightConverter_Standard failed; failed to load thermal shader");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        float averageRange = Vector2.Dot(context.Item.ArchetypeData?.DamageFalloff ?? new Vector2(0f, 0f), new Vector2(.5f, .5f));
        bool bTransparent = true;
        bool bPDW = averageRange <= 45f;
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length > 0)
        {
            string item = parameters[0];
            if (item.Length == 0) { }
            else if (item == "Transparent") bTransparent = true;
            else if (item == "Opaque") bTransparent = false;
            else context.Log.LogError($"MaterialGenerator_Auto expected either \"Transparent\" or \"Opaque\" for its first parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 1)
        {
            string item = parameters[1];
            if (item.Length == 0) { }
            else if (item == "PDW") bPDW = true;
            else if (item == "PR") bPDW = false;
            else context.Log.LogError($"MaterialGenerator_Auto expected either \"PDW\" or \"PR\" for its second parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 2)
            context.Log.LogWarning($"MaterialGenerator_Auto ignoring extra parameters: {FactoryManager.FormatParams(parameters[2..])}");

        // Create the material and apply configs!
        Material thermalMat = new(thermalOverlayShader)
        {
            name = $"ThermalOverlay - Auto ({(bTransparent ? "Transparent" : "Opaque")}, {(bPDW ? "PDW" : "PR")})",
            hideFlags = HideFlags.HideAndDontSave
        };

        MaterialConfig clone;
        if (bPDW) clone = MaterialGenerator_PDW.PDWMaterialConfig.Clone();
        else      clone = MaterialGenerator_PR.PRMaterialConfig.Clone();

        foreach (var offlineGear in GameData.PlayerOfflineGearDataBlock.GetAllBlocks())
        {
            GearIDRange range = new(offlineGear.GearJSON);
            int hash;
            if (range.GetCompBool(eGearComponent.SightPart))
                hash = range.GetCompID(eGearComponent.SightPart).GetHashCode();
            else
                hash = range.GetChecksum().GetHashCode();
            context.Log.LogMessage($"Gear {offlineGear.name} uses mode {Mathf.Abs(hash % ThermalTextures.Count)}");
        }

        int index = context.Item.GearPartHolder.SightData?.persistentID.GetHashCode() 
            ?? context.Item.GearIDRange.GetChecksum().GetHashCode();
        index = Mathf.Abs(index % ThermalTextures.Count);
        clone.HeatTex = $"Load(ThermalOverlay, {ThermalTextures[index]})";
        if (bTransparent) clone.HeatTex = clone.HeatTex.Value.Replace(".png", "_Transparent.png");

        clone.Zoom = 0f;
        clone.OffAngleFade = 0f; // OffAngleFade rarely looks good, and often looks bad
        
        clone.ApplyAllWithDefaults(thermalMat, context);
        return thermalMat;
    }
}
