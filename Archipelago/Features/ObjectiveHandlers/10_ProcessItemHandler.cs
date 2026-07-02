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
        public LocationID Location_ProcessItemCages
            => LocationID.From(data, "Process Item Cage Locations", data => new("Locations checked by picking up the start item for Process Item objectives (if it spawned in the elevator cage)", data.Location_BigPickups));

        public LocationID Location_ProcessItemProcessors
            => LocationID.From(data, "Process Item Processor Locations", data => new("Locations containing the processor for a Process Item objective", data.Location_Never));

        public ItemID Item_ProcessItemProcessors
            => ItemID.From(data, "Process Item Processor Items", data => new("Items indicating a Process Items processor is reachable", data.Item_Never));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.ActivateSmallHSU;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension (Objective.Data data)
    {
        public RegionID Region_ProcessItemObtained
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Item Obtained", data => new("Region entered by obtaining the process item objective's unprocessed item.", data.Region_Objective));

        public RegionID Region_ProcessItemProcessed
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Item Processed", data => new("Region entered by processing the process item objective's item.", data.Region_Objective));


        public LocationID Location_ProcessItemCage_Instance
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Process Item Cage Locations", data => new("A particular Process Item objective cage spawn location.", data.Location_ProcessItemCages));

        public LocationID Location_ProcessItemProcessor_Instance
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Process Item Processor Locations", data => new("A particular Process Item processor location", data.Location_ProcessItemProcessors));

        public ItemID Item_ProcessItemProcessor_Instance
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Process Item Processor Items", 
                data => new("A particular Process Item processor", data.Item_ProcessItemProcessors),
                new ProcessItemHandler.ProcessItem_ProcessorItem(data.Region_Objective)
            );
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

    public class ProcessItem_ProcessorItem : Item
    {
        public ProcessItem_ProcessorItem(RegionID objective)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
        }

        public RegionID ObjectiveRegion { get; private init; }
    }

    // Objective requiring an item be brought to be "processed" and then brought to extraction
    [Objective.Callback]
    public void HandleActivateSmallHSUObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.ActivateSmallHSU)
            return;

        // Two-step objective: Find the item, then get to the processor
        // Fun fact: Any item with the correct id can be processed to complete the objective. There's only ever one such item per level, though

        // Add the item to the elevator zone, if necessary
        ItemID startItem = data.Item_BigPickup_Instance(data.Objective.ActivateHSU_ItemFromStart);
        if (data.Objective.ActivateHSU_BringItemInElevator)
        {
            RegionList region = data.GetLayer(LayerType.Main).FirstZone.Region_Zone;
            data.Locations.CreateValue(
                data.Location_ProcessItemCage_Instance,
                region,
                new LocationData(),
                startItem
            );
        }

        // Collected item region
        RegionID collectItemRegion = data.Region_ProcessItemObtained;
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Objective,
            EndingRegion = collectItemRegion,
            ReqItem = new(Path.RequiredItem.eType.Item, startItem),
            ReqCount = 1u,
        });

        // Add the processor to the expedition
        ItemID processorItem = data.Item_ProcessItemProcessor_Instance;
        data.Locations.CreateValue(
            data.Location_ProcessItemProcessor_Instance,
            data.ObjectiveData.ZonePlacementDatas.SelectMany(data.PlacementsToZoneRegions).Select(info => info.Region).ToList(),
            new LocationData() { IsAutoDiscovered = true },
            processorItem
        );

        // Processed item region
        RegionID processedItemRegion = data.Region_ProcessItemProcessed;
        data.AddPath(new Path()
        {
            StartingRegion = collectItemRegion,
            EndingRegion = processedItemRegion,
            ReqItem = new(Path.RequiredItem.eType.Item, processorItem),
            ReqCount = 1u,
        });

        // Events triggered by initiating processing on the small HSU - both sets are always triggered (I think)
        if (data.Objective.EventsOnActivate.Any())
            data.ProcessEvents(processedItemRegion, data.Objective.EventsOnActivate);
        data.ProcessEvents(processedItemRegion, data.Objective.ActivateHSU_Events ??= new(1));

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
            var expeditionData = Expedition.Data.GetFromCurrentExpedition();
            var firstData = expeditionData.MainLayer.GetObjectiveDatas().First();

            int count = 0;
            if (firstData.Objective.GenericItemFromStart != 0) ++count;
            if (firstData.Objective.Type == eWardenObjectiveType.PowerCellDistribution) 
                count += firstData.Objective.PowerCellsToDistribute;

            foreach (var data in expeditionData.RealLayers.SelectMany(l => l.GetObjectiveDatas()))
            {
                if ((data.Objective.Type != eWardenObjectiveType.ActivateSmallHSU) || !data.Objective.ActivateHSU_BringItemInElevator)
                    continue;

                var comp = ElevatorCage.Current.m_cargoCage.m_itemsToMoveToCargo[count].GetComponentInChildren<CarryItemPickup_Core>();
                if (comp.ItemDataBlock.persistentID != data.Objective.ActivateHSU_ItemFromStart)
                    FeatureLogger.Warning("Associating wrong item with processor objective starting item!");

                PickupHelper.AssociateItem(comp, data.Location_ProcessItemCage_Instance);
            }
        }
    }

}
