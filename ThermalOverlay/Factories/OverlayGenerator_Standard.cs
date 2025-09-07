
using ReTFO.ThermalOverlay.Config;
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Generates an overlay using the provided values. Accepts one parameter which decides whether the overlay
///  mounts to the front or the back of the weapon. This only makes a real difference if the weapon has
///  iron sights, because those are the transforms used to calculate the ending positions
/// Standard([AttachRear/AttachFront])
/// </summary>
public class OverlayGenerator_Standard : IOverlayGenerator
{
    public const string Name = "Standard";

    public virtual bool GenerateOverlay(string? thisName, ConversionContext context)
    {
        GameObject overlay = new()
        {
            name = Plugin.OverlayGameObjectName,
            hideFlags = HideFlags.DontSave,
        };

        bool attachRear = true;
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length > 0)
        {
            string item = parameters[0];
            if (item.Length == 0) { }
            else if (item == "AttachRear") attachRear = true;
            else if (item == "AttachFront") attachRear = false;
            else context.Log.LogError($"OverlayGenerator_Standard expected either \"AttachRear\" or \"AttachFront\" for its first parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 1)
            context.Log.LogWarning($"OverlayGenerator_Standard ignoring extra parameters: {FactoryManager.FormatParams(parameters[1..])}");

        // Mesh creation
        MeshFilter filter = overlay.AddComponent<MeshFilter>();
        filter.mesh = context.Factory.RunMeshGenerator(context.Config?.OverlayConfig.MeshGenerator, context);

        // zOffset controls whether the overlay lands at the front or the back of the gun
        // Moving the overlay to the front can half the size (1/4 the area)
        Transform sightAlign = context.Item.SightLookAlign;
        int childCount = sightAlign.GetChildCount();
        float zOffset = 0f;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = sightAlign.GetChild(i);
            if (attachRear) zOffset = Mathf.Min(zOffset, sightAlign.TransformDirection(child.localPosition).z);
            else            zOffset = Mathf.Max(zOffset, sightAlign.TransformDirection(child.localPosition).z);
        }

        // Applying transforms
        overlay.transform.SetParent(sightAlign);
        overlay.transform.localPosition = (context.Config?.OverlayConfig.MeshOffset ?? Vector3.zero) + zOffset * Vector3.forward;
        overlay.transform.localRotation =  context.Config?.OverlayConfig.MeshRotation ?? Quaternion.identity;
        overlay.transform.localScale    =  context.Config?.OverlayConfig.MeshScale ?? Vector3.one;

        // Simply creating the material with the provided properties
        context.Renderer = overlay.AddComponent<MeshRenderer>();
        context.Material = context.Factory.RunMaterialGenerator(context.Config?.MaterialGenerator, context);
        context.Material.mainTexture = context.Factory.RunTextureGenerator(context.Config?.OverlayConfig.TextureGenerator, context);
        context.Material.SetFloat(MaterialConfig.CenterWhenUnscoped_Name, 0f);
        context.Renderer.sharedMaterial = context.Material;

        context.Log.LogDebug($"OverlayGenerator_Standard successfully added overlay to item \"{context.Item.name}\"");
        return true;
    }
}
