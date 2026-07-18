using LevelGeneration;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class PowerCellDistributionHandler_Tags
{
    extension(Game.Data data)
    {
        public LocationID Location_PowerCellDistributionCells
            => LocationID.From(data, "PowerCell Distribution Cell Locations", data => new("Locations checked by picking up cells spawned for the PowerCell Distribution objective (always in the starting lift, if spawned)", data.Location_BigPickups));

        // Note: Cell items are just normal big pickups, so they don't get their own tag

        public LocationID Location_PowerCellDistributionGens
            => LocationID.From(data, "PowerCell Distribution Gen Locations", data => new("Locations checked by finding generators for the PowerCell Distribution objective", data.Location_Never));

        public ItemID Item_PowerCellDistributionGens
            => ItemID.From(data, "PowerCell Distribution Gen Items", data => new("Items indicating access to a PowerCell Distribution generator", data.Item_Never));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.PowerCellDistribution;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension(Objective.Data data)
    {
        public RegionID Region_PowercellDistributionGeneratorPowered(int count)
            => RegionID.From(data, $"{data.ObjectiveName} Powered {count} Generators", data => new("Region entered by powering a specific number of cells during a powercell distribution objective", data.Region_Objective));

        public LocationID Location_PowerCellDistributionCells_PerObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} PowerCell Distribution Cell Locations", data => new("Locations checked by picking up cells spawned for a particular objective", data.Location_PowerCellDistributionCells));


        public LocationID Location_PowerCellDistributionGens_PerObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} PowerCell Distribution Gen Locations", data => new("Locations checked by finding generators for a particular objective", data.Location_PowerCellDistributionGens));

        public ItemID Item_PowerCellDistributionGens_PerObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} PowerCell Distribution Gen Items", data => new("Items indicating access to a PowerCell Distribution generator for a particular objective", data.Item_PowerCellDistributionGens));


        public LocationID Location_PowerCellDistributionCell_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} PowerCell Distribution Cell Location #{count}", data => new("Locations checked by picking up a particular cell", data.Location_PowerCellDistributionCells_PerObjective));


        public LocationID Location_PowerCellDistributionGen_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} PowerCell Distribution Gen Location #{count}", data => new("Locations checked by finding a particular generator", data.Location_PowerCellDistributionGens_PerObjective));

        public ItemID Item_PowerCellDistributionGen_Instance(int count)
            => ItemID.From(
                Checked(data),
                $"{data.ObjectiveName} PowerCell Distribution Gen #{count}",
                data => new("Item indicating access to a particular PowerCell Distribution generator", data.Item_PowerCellDistributionGens_PerObjective),
                new PowerCellDistributionHandler.PowerCellDistribution_GenItem(data.Region_Objective, count)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class PowerCellDistributionHandler : ArchipelagoFeature
{
    public override string Name => "Powercell Distribution Handler";
    public override string Description
        => "Handles the PowerCellDistribution objective type.\n"
        + "Example: R2B2";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public class PowerCellDistribution_GenItem : Item
    {
        public PowerCellDistribution_GenItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public int Count { get; private init; }
    }

    // Objective requiring power cells be taken from the elevator zone and to various generators throughout the layer
    [Objective.Callback]
    public void HandlePowerCellDistributionObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.PowerCellDistribution)
            return;

        // Place starting cells in elevator zone - Only for main layer (and possibly only for first objective?)
        ItemID cellItem = data.Item_BigPickup_Cell;
        if (data.LayerType.IsMainLayer) // && data.ObjectiveIndex == 0)
        {
            RegionID region = data.FirstZone.Region_Zone;
            for (int i = 1; i <= data.Objective.PowerCellsToDistribute; i++)
            {
                data.Locations.CreateValue(
                    data.Location_PowerCellDistributionCell_Instance(i),
                    region,
                    new LocationData(),
                    cellItem
                );
            }
        }

        // For each gen needed, create two regions: One checks for access to cells, the other to gens
        List<List<RegionID>> regionSets = data.ObjectiveToZoneRegionSets(data.Objective.PowerCellsToDistribute).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        ItemID genCategory = data.Item_PowerCellDistributionGens_PerObjective;
        RegionID last = data.Region_Objective;
        for (int i = 1; i <= data.Objective.PowerCellsToDistribute; i++)
        {
            // Place gen
            ItemID genItem = data.Item_PowerCellDistributionGen_Instance(i);
            data.Locations.CreateValue(
                data.Location_PowerCellDistributionGen_Instance(i),
                regionSets[i - 1],
                new LocationData() { IsAutoDiscovered = true},
                genItem
            );

            // Add a region for finding and powering a gen
            RegionID genRegion = data.Region_PowercellDistributionGeneratorPowered(i);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = genRegion,
                Reqs = new(
                    new(Path.eType.ItemConsumed, cellItem, 1u),
                    new(Path.eType.Category, genCategory, (uint)i)
                )
            });
            last = genRegion;

            // Recgonize events triggered by inserting a cell
            eventWrapper.Process(genRegion);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, last);
    }

    /// <summary>
    /// Normally we'd patch the relevant job, but that can causes null reference errors
    ///  for cargo cage items. Fortunately, we can just grab them when it's done building
    /// </summary>
    [ArchivePatch(typeof(LG_Factory), nameof(LG_Factory.FactoryDone))]
    public static class LG_Factory__FactoryDone__Patch
    {
        public static void Postfix()
        {
            var data = Expedition.Data.GetFromCurrentExpedition()
                .MainLayer.GetObjectiveDatas().First();

            if (data.Objective.Type != eWardenObjectiveType.PowerCellDistribution)
                return;

            var items = ElevatorCage.Current.m_cargoCage.m_itemsToMoveToCargo.Iter();
            if (data.Objective.GenericItemFromStart != 0)
                items = items.Skip(1);

            int count = 0;
            foreach (var item in items)
            {
                var comp = item.GetComponentInChildren<CarryItemPickup_Core>();
                if (comp.ItemDataBlock.persistentID != BigPickupHandler.CellItemID)
                    FeatureLogger.Warning("Associated a non-cell item with distribution objective starting cell location!");
                PickupHelper.AssociateItem(comp, data.Location_PowerCellDistributionCell_Instance(++count));
            }
        }
    }

}
