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
        public TagResolver Tag_PowerCellDistributionCellLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("PowerCell Distribution Cell Locations", "Locations checked by picking up cells spawned for the PowerCell Distribution objective (always in the starting lift, if spawned)", gd.Tag_BigPickupLocations));

        // Note: Cell items are just normal big pickups, so they don't get their own tag

        public TagResolver Tag_PowerCellDistributionGenLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("PowerCell Distribution Gen Locations", "Locations checked by finding generators for the PowerCell Distribution objective", gd.Tag_Never));

        public TagResolver Tag_PowerCellDistributionGenItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("PowerCell Distribution Gen Items", "Items indicating access to a PowerCell Distribution generator", gd.Tag_Never));
    }

    extension(Objective.Data data)
    {
        public TagResolver Tag_PowerCellDistributionCellLocations_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} PowerCell Distribution Cell Locations", "Locations checked by picking up cells spawned for a particular objective", gd.Tag_PowerCellDistributionCellLocations));

        // Note: Cell items are just normal big pickups, so they don't get their own tag

        public TagResolver Tag_PowerCellDistributionGenLocations_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} PowerCell Distribution Gen Locations", "Locations checked by finding generators for a particular objective", gd.Tag_PowerCellDistributionGenLocations));

        public TagResolver Tag_PowerCellDistributionGenItems_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} PowerCell Distribution Gen Items", "Items indicating access to a PowerCell Distribution generator for a particular objective", gd.Tag_PowerCellDistributionGenItems));
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

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.PowerCellDistribution;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return $"Distribute {data.Objective.PowerCellsToDistribute} Power Cells";
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
        // Region reached by finding a cell
        public static string CellFound(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Obtained {count} Cells";

        // Region reached by powering a generator
        public static string GeneratorPowered(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Powered {count} Generators";
    }

    private static class PowerCellDistribution_CellLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Cell Location #{count}", "A particular cell spawned for a particular objective", data.Tag_PowerCellDistributionCellLocations_PerObjective));

        public static LocationData MakeRandData() => new LocationData();
    }

    private static class PowerCellDistribution_GenLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Gen Location #{count}", "A particular generator spawn for a particular objective", data.Tag_PowerCellDistributionGenLocations_PerObjective));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private class PowerCellDistribution_GenItem : Item
    {
        public PowerCellDistribution_GenItem(Objective.Data data, int count)
            : base(MakeTag(data, count), MakeRandData())
        {
            ObjectiveData = data;
            Count = count;
        }

        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Gen Item #{count}", "A particular generator for a particular objective", data.Tag_PowerCellDistributionGenItems_PerObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }

        public int Count { get; set; }

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, ObjectiveData.Tag_PowerCellDistributionGenItems_PerObjective);

        public override Expedition.Data? RequiredExpedition => ObjectiveData;
    }

    public static KeyedItem GetGenItem(Objective.Data data, int count)
    {
        if (data.TryLookupItem(PowerCellDistribution_GenItem.MakeTag(data, count), out var item))
            return item;

        Item newItem = new PowerCellDistribution_GenItem(data, count);
        return new(data.AddItem(newItem), newItem);
    }

    // Objective requiring power cells be taken from the elevator zone and to various generators throughout the layer
    [Objective.Callback]
    public void HandlePowerCellDistributionObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // Place starting cells in elevator zone - Only for main layer (and possibly only for first objective?)
        KeyedItem cellItem = BigPickupHandler.GetBigPickupItem(data, BigPickupHandler.CellItemID);
        if (data.LayerType.IsMainLayer) // && data.ObjectiveIndex == 0)
        {
            RegionID region = data.LookupOrCreateRegion(data.FirstZone.ZoneName);
            for (int i = 1; i <= data.Objective.PowerCellsToDistribute; i++)
            {
                data.AddLocation(
                    PowerCellDistribution_CellLocation.MakeTag(data, i),
                    region,
                    PowerCellDistribution_CellLocation.MakeRandData(),
                    cellItem.ID
                );
            }
        }

        // TODO: This objective has somewhat complicated cell implications, ie if doors are locked by cells. Can't think of any issues in vanilla, off the top of my head
        // For each gen needed, create two regions: One checks for access to cells, the other to gens
        List<List<RegionID>> regionSets = data.ObjectiveToZoneRegionSets(data.Objective.PowerCellsToDistribute).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        RegionID last = data.ObjectiveStartRegion;
        for (int i = 1; i <= data.Objective.PowerCellsToDistribute; i++)
        {
            // Place gen
            KeyedItem genItem = GetGenItem(data, i);
            data.AddLocation(
                PowerCellDistribution_GenLocation.MakeTag(data, i),
                regionSets[i - 1],
                PowerCellDistribution_GenLocation.MakeRandData(),
                genItem.ID
            );

            // Check that we have enough cells
            RegionID cellRegion = data.LookupOrCreateRegion(ThisRegions.CellFound(data, i));
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = cellRegion,
                ReqItem = cellItem.PathReqs,
                ReqCount = 1u,
            });

            // Check that we've found enough gens
            string genName = ThisRegions.GeneratorPowered(data, i);
            RegionID genRegion = data.LookupOrCreateRegion(genName);
            data.AddPath(new Path()
            {
                StartingRegion = cellRegion,
                EndingRegion = genRegion,
                ReqItem = genItem.PathReqs,
                ReqCount = 1u,
            });
            last = genRegion;

            // Recgonize events triggered by inserting a cell
            eventWrapper.Process(genRegion, genName);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, last);
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
            var data = Expedition.Data.FromCurrentExpedition()
                .MainLayer.GetObjectiveDatas().First();
            
            if (!This.IsCorrectObjective(data))
                return;

            var items = ElevatorCage.Current.m_cargoCage.m_itemsToMoveToCargo.Iter();
            if (data.Objective.GenericItemFromStart != 0)
                items = items.Skip(1);

            int count = 0;
            foreach (var item in items)
            {
                if (!data.TryLookupLocation(PowerCellDistribution_CellLocation.MakeTag(data, ++count), out var loc))
                {
                    FeatureLogger.Error("Failed to identify powercell distribution cell location during association!");
                    continue;
                }
                var comp = item.GetComponentInChildren<CarryItemPickup_Core>();
                if (comp.ItemDataBlock.persistentID != BigPickupHandler.CellItemID)
                    FeatureLogger.Warning("Associated a non-cell item with distribution objective starting cell location!");
                PickupHelper.AssociateItem(comp, loc.ID);
            }
        }
    }

}
