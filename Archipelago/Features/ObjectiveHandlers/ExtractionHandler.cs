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
        public TagResolver Tag_ExtractionLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Extraction Locations", "Locations checked by reaching extraction on an expedition", gd.Tag_Never));

        public TagResolver Tag_ExtractionItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Extraction Items", "Items indicating extraction is reachable on a particular level", gd.Tag_Never));
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

    private static class ExtractionReachableLocation
    {
        public static TagResolver MakeTag(Expedition.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Extraction Location", "The locaiton of extraction for a particular expedition", gd.Tag_ExtractionLocations));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    /// <summary>
    /// A purely event item used to identify when extraction is reachable
    /// </summary>
    private class ExtractionReachableItem : Item
    {
        public ExtractionReachableItem(Expedition.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ExpeditionData = data;
        }

        public static TagResolver MakeTag(Expedition.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Extraction Reachable", "Indiciates extraction is reachable for a particular expedition", gd.Tag_ExtractionItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        Expedition.Data ExpeditionData { get; set; }
    }

    public static KeyedItem GetExtractionReachableItem(Expedition.Data data)
    {
        if (data.TryLookupItem(ExtractionReachableItem.MakeTag(data), out var item))
            return item;

        Item newItem = new ExtractionReachableItem(data);
        return new(data.AddItem(newItem), newItem);
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

        KeyedItem item = GetExtractionReachableItem(data);
        data.AddLocation(
            ExtractionReachableLocation.MakeTag(data),
            data.LookupOrCreateRegion(zone.ZoneName),
            ExtractionReachableLocation.MakeRandData(),
            item.ID
        );
    }

}
