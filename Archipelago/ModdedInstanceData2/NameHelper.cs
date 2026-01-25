
using GameData;
using LevelGeneration;
using ReTFO.Archipelago.ModdedInstanceData;
using System.Runtime.CompilerServices;
using UnityEngine.UI;

namespace ReTFO.Archipelago.ModdedInstanceData2;

// Set of extension and helper methods for creating names for regions
public static class NameHelper
{
    // Get the short name of an expedition
    public static string GetName(this ProcessExpeditionData expeditionData)
        => expeditionData.Expedition.GetShortName(expeditionData.IndexInTier);

    // Get the name of a layer based on its type
    public static string GetName(this LayerType layerType)
    {
        return layerType.value switch
        {
            0 => "Main",
            -1 => "Secondary",
            -2 => "Overload",
            _ => $"Dim #{layerType.value}",
        };
    }

    // Get the name of the layer being processed
    public static string GetName(this ProcessLayerData layer)
        => layer.LayerType.GetName();

    // Get the dimension datablock ID for an expedition by layer, or 0 if none is found
    public static uint GetDimensionDataID(this ExpeditionInTierData expedition, LayerType type)
        => expedition.DimensionDatas.FirstOrDefault(d => (int)d.DimensionIndex == type.value)?.DimensionData ?? 0;

    // Get the dimension datablock ID by layer, or 0 if none is found
    public static uint GetDimensionDataID(this ProcessLayerData layer)
        => layer.Expedition.GetDimensionDataID(layer.LayerType);

    // Get the DimensionDataBlock for a layer, or null if none is found
    public static DimensionData? GetDimensionData(this ExpeditionInTierData expedition, LayerType type)
        => DimensionDataBlock.GetBlock(expedition.GetDimensionDataID(type)).DimensionData;

    // Get the DimensionDataBlock for a layer, or null if none is found
    public static DimensionData? GetDimensionData(this ProcessLayerData layer)
        => layer.Expedition.GetDimensionData(layer.LayerType);

    // Get the layout ID for an expedition by layer, or 0 if none exists (single-zone dimension)
    public static uint GetLayoutID(this ExpeditionInTierData expedition, LayerType type)
        => type.value switch
        {
            -0 => expedition.LevelLayoutData,
            -1 => expedition.SecondaryLayout,
            -2 => expedition.ThirdLayout,
            _ => expedition.GetDimensionData(type)?.LevelLayoutData ?? 0,
        };

    // Get the layout ID for a layer, or 0 if none exists (single-zone dimension)
    public static uint GetLayoutID(this ProcessLayerData layer)
        => GetLayoutID(layer.Expedition, layer.LayerType);

    // Get the expedition's layout datablock by layer, if it exists
    public static LevelLayoutDataBlock? GetLayout(this ExpeditionInTierData expedition, LayerType type)
        => LevelLayoutDataBlock.GetBlock(expedition.GetLayoutID(type));

    // Get the layer's layout datablock, if it exists
    public static LevelLayoutDataBlock? GetLayout(this ProcessLayerData layer)
        => layer.Expedition.GetLayout(layer.LayerType);
    
    // Get the layer's layout, and get the start alias from that layout. Defaults to 0 if the layer as no layout (single-zone dimension)
    public static int GetLayoutAliasStart(this ProcessLayerData layer) 
        => layer.GetLayout()?.ZoneAliasStart ?? 0;

    // Get the layer's layer data
    public static LayerData? GetLayerData(this ProcessLayerData layer)
        => layer.LayerType.value switch
        {
            0 => layer.Expedition.MainLayerData,
            -1 => layer.Expedition.SecondaryLayerData,
            -2 => layer.Expedition.ThirdLayerData,
            _ => null
        };

    // Get the layer's build from data
    public static BuildLayerFromData? GetBuildFromData(this ProcessLayerData layer)
        => layer.LayerType.value switch
        {
            -1 => layer.Expedition.BuildSecondaryFrom,
            -2 => layer.Expedition.BuildThirdFrom,
            _ => null
        };


    // Check the alias override. If it's -1 (or null), return the alias; else return the override
    public static int WithOverride(int overide, int alias)
        => overide == -1 ? alias : overide;

    // Get the alias of a zone given its layer
    public static int CalcAlias(this ProcessLayerData layer, ExpeditionZoneData? zone)
        => WithOverride(zone?.AliasOverride ?? layer.GetDimensionData()?.StaticAliasOverride ?? -1, layer.GetLayoutAliasStart() + (int)(zone?.LocalIndex ?? 0));

    // Get the alias of the currently processing zone
    public static int CalcAlias(this ProcessZoneData zoneData)
        => zoneData.Layer.CalcAlias(zoneData.Zone);

    // Get the name of a zone in a layer
    public static string GetName(this ProcessLayerData layer, ExpeditionZoneData? zone)
        => $"{layer.ExpeditionData.GetName()} ({layer.LayerType.GetName()}) ZONE_{layer.CalcAlias(zone)}";

    // Get the name of the currently processing zone
    public static string GetName(this ProcessZoneData zoneData)
        => zoneData.Layer.GetName(zoneData.Zone);

    // Get the name of a zone by localIndex in a layer
    public static string GetName(this ProcessLayerData layer, eLocalZoneIndex index)
        => layer.GetName(layer.GetLayout()?.Zones.First(z => z.LocalIndex == index));

    // Get the name of the nth zone in a layer
    public static string GetNthZoneName(this ProcessLayerData layer, int index)
            => layer.GetName(layer.GetLayout()?.Zones[index]);
    
    // Get the name of the first zone in a layer
    public static string GetFirstZoneName(this ProcessLayerData layer)
        => layer.GetNthZoneName(0);

    // Get the name of a zone by zone placement relative to a layer
    public static string GetName(this ProcessLayerData layer, ZonePlacementData placement)
    {
        LayerType type;
        if (placement.DimensionIndex == eDimensionIndex.Reality)
            type = layer.LayerType.value > 0 ? LG_LayerType.MainLayer : layer.LayerType;
        else
            type = placement.DimensionIndex;
        return new ProcessLayerData(layer.ExpeditionData, type).GetName(placement.LocalIndex);
    }

}
