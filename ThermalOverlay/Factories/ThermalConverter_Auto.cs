
using ReTFO.ThermalOverlay.Config;
using ReTFO.ThermalOverlay.Factories;
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Configurers;

/// <summary>
/// A simple thermal converter which forces a conversion, either by using the existing scope
///  if it has a reticule, using a tool's screen, or by generating an overlay for the item.
/// Accepts one parameter dictating which conversions it's allowed to attempt. AllowAll is the default
/// Auto([AllowAll/SightOnly/OverlayOnly/AllowNone])
/// </summary>
public class ThermalConverter_Auto : IThermalConverter
{
    public const string Name = "Auto";
    public virtual void ConvertItem(string? thisName, ConversionContext context)
    {
        Renderer? sight = null;
        if (context.Item.GearPartHolder.SightData != null)
        {
            sight = context.Item.GearPartHolder.SightPart?.GetComponentsInChildren<Renderer>()
                .FirstOrDefault(r => r.sharedMaterial.HasProperty(MaterialConfig.ReticuleA_Name));
        }
        else if (context.Item.GearPartHolder.ToolScreenPart != null)
        {
            sight = context.Item.GearPartHolder.SightPart?.GetComponentsInChildren<Renderer>()
                .FirstOrDefault(r => r.sharedMaterial.shader.name == "Cell/Player/Display_GearShader");
        }

        // Define and check parameters
        bool allowSightConversion = true;
        bool allowOverlayGeneration = true;
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length > 0)
        {
            string item = parameters[0];
            if (item.Length == 0) { }
            else if (item == "AllowAll") { }
            else if (item == "SightOnly") allowOverlayGeneration = false;
            else if (item == "OverlayOnly") allowSightConversion = false;
            else if (item == "AllowNone") { allowSightConversion = false; allowOverlayGeneration = false; }
            else context.Log.LogError($"ThermalConverter_Auto expected one of {{ \"AllowAll\", \"OverlayOnly\", \"SightOnly\", \"AllowNone\" }} for its first parameter, but instead got \"{item}\"");

        }
        if (parameters.Length > 1)
            context.Log.LogWarning($"ThermalConverter_Auto ignoring extra parameters: {FactoryManager.FormatParams(parameters[1..])}");

        // Attempt sight conversion
        bool isConverted = false;
        if ((sight != null) && allowSightConversion)
        {
            context.Renderer = sight;
            Shader? thermalShader = context.Plugin.AssetBundle.ThermalOverlayShader;
            if (thermalShader == null)
            {
                context.Log.LogError($"ThermalConverter_Auto failed; could not load thermal overlay shader");
                return;
            }

            if (context.Renderer.sharedMaterials.Select(m => m.shader).Contains(thermalShader))
            {
                context.Log.LogDebug($"Skipping sight conversion on item \"{context.Item.name}\" due to sight already being converted");
                return;
            }

            bool isThermal = sight.sharedMaterial.shader.name.Contains("thermal", StringComparison.OrdinalIgnoreCase);
            isConverted = context.Plugin.FactoryManager.RunSightConverter(context.Config?.SightConverter, context);
        }

        // Attempt overlay generation
        if ((!isConverted) && allowOverlayGeneration)
        {
            GameObject? existingOverlay = context.Item.transform.Find(Plugin.OverlayGameObjectName)?.gameObject;
            if (existingOverlay != null)
            {
                context.Log.LogDebug($"Skipping overlay creation on item \"{context.Item.name}\" due to overlay already being created");
                return;
            }
            else
                isConverted = context.Factory.RunOverlayGenerator(context.Config?.OverlayGenerator, context);
        }

        // Applying these configs at the end is best for consistency
        if (isConverted)
        {
            if (context.Material == null)
                context.Log.LogError($"Error: A converter or generator has failed to set the material property for ConversionContext");
            else
            {
                FactoryManager.CalcScopeCenter(context);
                context.Config?.MaterialConfig.ApplyAll(context.Material, context);
            }
        }
        else context.Log.LogWarning($"ThermalConverter_Auto: conversion ended in failure for item \"{context.Item.name}\"");
    }
}
