using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class ExtractionHandler : ArchipelagoFeature
{
    public override string Name => "Extraction Handler";
    public override string Description 
        => "Adds an \"Extraction Reachable\" item used to help identify when an expedition is clearable.\n"
        + "This item has no impact on play and is purely an internal item.";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private class ExtractionReachableLocation : Location
    {
        public ExtractionReachableLocation(string name, RegionList regions, Item? item)
            : base(name, regions, item) { }

        private static RandomizationData s_randData = new()
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    /// <summary>
    /// A purely event item used to identify when extraction is reachable
    /// </summary>
    private class ExtractionReachableItem : Item
    {
        public ExtractionReachableItem(Expedition.Data data)
            : base($"{data.ExpeditionName} Extraction Reachable")
        {
            ExpeditionData = data;
        }

        [JsonIgnore]
        Expedition.Data ExpeditionData { get; set; }

        private static RandomizationData s_randData = new();
        public override RandomizationData RandData => s_randData;
    }

    public static Item GetExtractionReachableItem(Expedition.Data data)
        => data.GetItem(new ExtractionReachableItem(data));

    [Expedition.Callback]
    public static void AddExtraction(Expedition.Data data)
    {
        // The win condition indicates the intended extraction point
        // However, if a forward extract geo is found, it takes priority over the rear extract win condition
        ComplexResourceSetDataBlock complex = ComplexResourceSetDataBlock.GetBlock(data.Expedition.Expedition.ComplexResourceData);
        Layer.Data layerData = data.MainLayer;
        Zone.Data? zone = null;
        foreach (var z in layerData.AllZones.Reverse()) // Reverse, since extraction is typically at the back
        {
            if (z.CustomGeo == null || z.CustomGeo == "")
                continue;

            if (complex.CustomGeomorphs_Exit_1x1.Any(c => c.Prefab == z.CustomGeo))
            {
                zone = z;
                break;
            }
        }

        if (zone == null && data.Expedition.MainLayerData.ObjectiveData.WinCondition != eWardenObjectiveWinCondition.GoToExitGeo)
            zone = layerData.FirstZone;
        else if (zone == null)
        {   // This doesn't necessarily mean a failure. R8E1 has no extraction, for example
            FeatureLogger.Warning($"Failed to place extraction for {data.ExpeditionName}");
            return;
        }

        data.GetLocation(new ExtractionReachableLocation(
            $"{data.ExpeditionName} Extraction",
            data.GetOrCreateRegion(zone.ZoneName),
            GetExtractionReachableItem(data)
        ));
    }

}
