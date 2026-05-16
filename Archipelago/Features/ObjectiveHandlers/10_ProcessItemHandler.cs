using GameData;
using LevelGeneration;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class ProcessItemHandler_Tags
{
    extension(Game.Data data)
    {
        public TagResolver Tag_ProcessItemStartLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Process Item Start Locations", "Locations checked by starting picking up the start item for Process Item objectives (if it spawned in the elevator)", gd.Tag_BigPickupLocations));

        public TagResolver Tag_ProcessItemProcessorLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Process Item Processor Locations", "Locations containing the processor for a Process Item objective", gd.Tag_Never));

        public TagResolver Tag_ProcessItemProcessorItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Process Item Processor Items", "Items indicating a Process Items processor is reachable", gd.Tag_Never));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class ProcessItemHandler : ArchipelagoFeature
{
    public override string Name => "Process Item Handler";
    public override string Description
        => "Handles the ActiveSmallHSU objective type.\n"
        + "Example: R3A1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.ActivateSmallHSU;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            ItemDataBlock startItem = ItemDataBlock.GetBlock(data.Objective.ActivateHSU_ItemFromStart);
            if (startItem == null)
                FeatureLogger.Error($"Failed to find start item for objective: {data.ObjectiveName(null)}");
            ItemDataBlock endItem = ItemDataBlock.GetBlock(data.Objective.ActivateHSU_ItemAfterActivation);
            if (endItem == null)
                return $"Process \"{startItem?.publicName ?? "null!"}\"";
            else
                return $"Process \"{startItem?.publicName ?? "null!"}\" into \"{endItem?.publicName ?? "null!"}\"";
        }

        // True if This is the correct objective
        public static bool IsCorrectObjective(Objective.Data data)
            => data.Objective.Type == ObjectiveType;

        // Assert This is the correct objective, and log an error if it is not
        public static void CheckIsCorrectObjective(Objective.Data data)
        {
            if (!IsCorrectObjective(data))
                FeatureLogger.Error($"Wrong objective type! Expected {Enum.GetName(ObjectiveType)}, got {data.Objective.Type}");
        }
    }

    // Names of regions for this objective
    private static class ThisRegions
    {
        // Region reached by obtaining the start item for the objective (typically in the elevator)
        public static string ItemObtained(Objective.Data data)
            => $"{data.ObjectiveName()} Item Obtained";

        // Region reached by processing the item
        public static string ItemProcessed(Objective.Data data)
            => $"{data.ObjectiveName()} Item Processed";
    }

    private static class ProcessItem_StartLocation
    {
        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Start Location", "Location checked by grabbing a particular big pickup", gd.Tag_ProcessItemStartLocations));

        public static LocationData MakeRandData() => new LocationData();
    }

    private static class ProcessItem_ProcessorLocation
    {
        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Processor Location", "Location checked by finding a particular processor", gd.Tag_ProcessItemProcessorLocations));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private class ProcessItem_ProcessorItem : Item
    {
        public ProcessItem_ProcessorItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Processor Item", "Item indicating a particular processor is reachable", gd.Tag_ProcessItemProcessorItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }
    }

    public static KeyedItem GetProcessorItem(Objective.Data data)
    {
        if (data.TryLookupItem(ProcessItem_ProcessorItem.MakeTag(data), out var item))
            return item;

        Item newItem = new ProcessItem_ProcessorItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    // Objective requiring an item be brought to be "processed" and then brought to extraction
    [Objective.Callback]
    public void HandleActivateSmallHSUObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // Two-step objective: Find the item, then get to the processor
        // Fun fact: Any item with the correct id can be processed to complete the objective. There's only ever one such item per level, though

        // Add the item to the elevator zone, if necessary
        KeyedItem startItem = BigPickupHandler.GetBigPickupItem(data, data.Objective.ActivateHSU_ItemFromStart);
        if (data.Objective.ActivateHSU_BringItemInElevator)
        {
            RegionList region = data.LookupOrCreateRegion(data.GetLayer(LayerType.Main).FirstZone.ZoneName);
            data.AddLocation(
                ProcessItem_StartLocation.MakeTag(data),
                region,
                ProcessItem_StartLocation.MakeRandData(),
                startItem.ID
            );
        }

        // Collected item zone
        RegionID collectItemRegion = data.LookupOrCreateRegion(ThisRegions.ItemObtained(data));
        data.AddPath(new Path()
        {
            StartingRegion = data.ObjectiveStartRegion,
            EndingRegion = collectItemRegion,
            ReqItem = startItem.PathReqs,
            ReqCount = 1u,
        });

        // Add the processor to the expedition
        KeyedItem processorItem = GetProcessorItem(data);
        data.AddLocation(
            ProcessItem_ProcessorLocation.MakeTag(data),
            data.ObjectiveData.ZonePlacementDatas.SelectMany(data.PlacementsToZoneRegions).Select(info => info.Region).ToList(),
            ProcessItem_ProcessorLocation.MakeRandData(),
            processorItem.ID
        );

        // Processed item region
        string processedItemName = ThisRegions.ItemProcessed(data);
        RegionID processedItemRegion = data.LookupOrCreateRegion(processedItemName);
        data.AddPath(new Path()
        {
            StartingRegion = collectItemRegion,
            EndingRegion = processedItemRegion,
            ReqItem = processorItem.PathReqs,
            ReqCount = 1u,
        });

        // Events triggered by initiating processing on the small HSU - both sets are always triggered (I think)
        if (data.Objective.EventsOnActivate.Any())
            data.ProcessEvents(processedItemRegion, processedItemName, data.Objective.EventsOnActivate);
        data.ProcessEvents(processedItemRegion, processedItemName, data.Objective.ActivateHSU_Events ??= new(1));

        // Place objective complete item in the post-processing region if the objective can be completed that way
        if (data.Objective.ActivateHSU_ObjectiveCompleteAfterInsertion)
            SharedObjectiveHandler.AddObjectiveCompleteItem(data, processedItemRegion);
    }

    /// <summary>
    /// Normally we'd patch the relevant job, but that can causes null reference errors
    ///  for cargo cage items. Fortunately, we can grab them when it's done building
    /// </summary>
    [ArchivePatch(typeof(LG_Factory), nameof(LG_Factory.FactoryDone))]
    public static class LG_Factory__FactoryDone__Patch
    {
        public static void Postfix()
        {
            var expeditionData = Expedition.Data.FromCurrentExpedition();
            var firstData = expeditionData.MainLayer.GetObjectiveDatas().First();

            int count = 0;
            if (firstData.Objective.GenericItemFromStart != 0) ++count;
            if (firstData.Objective.Type == eWardenObjectiveType.PowerCellDistribution) 
                count += firstData.Objective.PowerCellsToDistribute;

            foreach (var data in expeditionData.RealLayers.SelectMany(l => l.GetObjectiveDatas()))
            {
                if (!This.IsCorrectObjective(data) || !data.Objective.ActivateHSU_BringItemInElevator)
                    continue;

                var comp = ElevatorCage.Current.m_cargoCage.m_itemsToMoveToCargo[count].GetComponentInChildren<CarryItemPickup_Core>();
                if (comp.ItemDataBlock.persistentID != data.Objective.ActivateHSU_ItemFromStart)
                    FeatureLogger.Warning("Associating wrong item with processor objective starting item!");

                if (data.TryLookupLocation(ProcessItem_StartLocation.MakeTag(data), out var loc))
                    PickupHelper.AssociateItem(comp, loc.ID);
                else
                    FeatureLogger.Error("Failed to create association for process item objective's starting item!");
            }
        }
    }

}
