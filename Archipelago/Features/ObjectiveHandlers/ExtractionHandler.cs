using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class ExtractionHandler_Tags
{
    extension (Game.Data data)
    {
        public LocationID Location_Extractions
            => LocationID.From(data, "Extraction Locations", data => new("Parent tag of all extraction locations", data.Location_Never));

        public ItemID Item_Extractions
            => ItemID.From(data, "Extractions", data => new("Items indicating extraction is reachable on a particular level", data.Item_Never));
    }

    extension (Expedition.Data data)
    {
        public LocationID Location_Extraction_Instance
            => LocationID.From(data, $"{data.ExpeditionName} Extraction Location", data => new("An extraction location for a particular expedition", data.Location_Extractions));

        public ItemID Item_Extraction_Instance
            => ItemID.From(
                data,
                $"{data.ExpeditionName} Extraction",
                data => new("An item indicating extraction is reachable for a particular expedition", data.Item_Extractions),
                new ExtractionHandler.ExtractionReachableItem(data.Region_Expedition)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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

    /// <summary>
    /// A purely event item used to identify when extraction is reachable
    /// </summary>
    public class ExtractionReachableItem : Item
    {
        public ExtractionReachableItem(RegionID expedition)
            : base(new ItemData() { IsProgression = true })
        {
            ExpeditionRegion = expedition;
        }

        public RegionID ExpeditionRegion { get; private init; }
    }

    [Expedition.Callback]
    public void AddExtraction(Expedition.Data data)
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

        data.Locations.CreateValue(
            data.Location_Extraction_Instance,
            zone.Region_Zone,
            new LocationData() { IsAutoDiscovered = true },
            data.Item_Extraction_Instance
        );
    }

}
