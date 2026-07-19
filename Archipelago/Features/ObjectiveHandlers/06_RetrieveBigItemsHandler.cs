using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelGeneration;
using Player;
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

using PlayFab.ClientModels;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Runtime.InteropServices;

public static class RetrieveBigItemsHandler_Tags
{ 
    extension (Game.Data data)
    {
        public LocationID Location_BigRetrievals
            => LocationID.From(data, "Big Retrieval Locations", data => new("Locations checked by picking up big retrieval objective items, IE in R2A1", data.Location_BigPickups));

        public ItemID Item_BigRetrievals
            => ItemID.From(data, "Big Retrieval Items", data => new("Big pickup items marked as retrieval objective items, IE the cargos in R2A1", data.Item_BigPickups));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.RetrieveBigItems;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension (Objective.Data data)
    {
        public RegionID Region_RetrievedItem(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} {count} Big Items Retrieved", data => new("Region entered when a particular big objective item is retrieved (picked up)", data.Region_Objective));


        public LocationID Location_BigRetrievals_ByObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Big Retrieval Locations", data => new("Locations checked by picking up big retrieval items for a particular objective", data.Location_BigRetrievals));

        public ItemID Item_BigRetrievals_ByObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Big Retrieval Items", data => new("Big pickup items marked as big retrieval items for a particular objective", data.Item_BigRetrievals));


        public LocationID Location_BigRetrieval_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Big Retrieval Location #{count}", data => new("A particular big retrieval item's location", data.Location_BigRetrievals_ByObjective));

        public ItemID Item_BigRetrieval_Instance(int count)
            => ItemID.From(
                Checked(data),
                $"{data.ObjectiveName} Big Retrieval Item #{count}",
                data => new("A particular big retrieval item", data.Item_BigRetrievals_ByObjective),
                new RetrieveBigItemsHandler.BigRetrieval_Item(data, count)
            );
    }
}


[EnableFeatureByDefault, AutomatedFeature]
public class RetrieveBigItemsHandler : ArchipelagoFeature
{
    public override string Name => "Retrieve Big Items Handler";
    public override string Description
        => "Handles the RetrieveBigItems objective type.\n"
        + "Example: R2A1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Big retrieval item itself - note that despite this being usable as a normal item, we disallow it by changing its name
    public class BigRetrieval_Item : BigPickupHandler.BigPickupItem
    {
        public BigRetrieval_Item(Objective.Data objective, int count)
            : base(objective.Region_Expedition, ItemDataBlock.GetBlock(objective.Objective.Retrieve_Items[count - 1]))
        {
            ObjectiveRegion = objective.Region_Objective;
            ItemIndex = count - 1;
        }

        /// <summary>
        /// The expedition this item was created for
        /// </summary>
        public RegionID ObjectiveRegion { get; private init; }

        /// <summary>
        /// Which item in the obejctive this refers to, 0-indexed
        /// </summary>
        public int ItemIndex { get; private init; }

        public override RegionID TargetRegion => ObjectiveRegion;

        protected override AsyncItemSpawnWrapper TrySpawnAsync()
        {
            var wrapper = base.TrySpawnAsync();

            void SetupObjectiveCarryItem(ISyncedItem item)
            {
                Objective.Data data = new(StateTracker.Get().GameData, ObjectiveRegion);
                CarryItemPickup_Core carryItem = item.Cast<CarryItemPickup_Core>();

                carryItem.SpawnNode = data.GetLG_Layer()!.m_zones[0].m_courseNodes[0];
                carryItem.Set_pItemData(new pItemData()
                {
                    itemID_gearCRC = carryItem.pItemData.itemID_gearCRC,
                    originLayer = data.LayerType
                });
                carryItem.m_isWardenObjective = true;

                // Replacing the item in the objective manager and in the extraction requirements
                var items = WardenObjectiveManager.GetObjectiveItemCollection(data.LayerType, data.ObjectiveIndex);
                ChainedPuzzles.CP_Bioscan_Core? core;
                if (WardenObjectiveManager.m_customGeoExitWinConditionItem != null)
                    core = WardenObjectiveManager.m_customGeoExitWinConditionItem.Cast<LG_LevelExitGeo>().m_puzzle.m_chainedPuzzleCores[0].TryCast<ChainedPuzzles.CP_Bioscan_Core>();
                else
                    core = WardenObjectiveManager.m_elevatorExitWinConditionItem.Cast<ElevatorShaftLanding>().m_puzzle.m_chainedPuzzleCores[0].TryCast<ChainedPuzzles.CP_Bioscan_Core>();

                // If the original item is registered as required for the exit scan...
                if (core != null && core.m_reqItems.Any(i => i.Pointer == items[ItemIndex].Pointer))
                {   // Try to swap in our new item instead
                    Il2CppReferenceArray<iWardenObjectiveItem> originalItem = new(1);
                    originalItem[0] = items[ItemIndex];

                    Il2CppReferenceArray<iWardenObjectiveItem> newItem = new(1);
                    newItem[0] = carryItem.Cast<iWardenObjectiveItem>();

                    WardenObjectiveManager.RemoveObjectiveItemAsRequiredForExitScan(originalItem);
                    WardenObjectiveManager.AddObjectiveItemAsRequiredForExitScan(true, newItem);
                }

                // Copy the existing item's objective properties - this is mostly to account for
                // recalls, since in that case we'll be replacing an existing, possibly touched item
                carryItem.IsRegistered = items[ItemIndex].IsRegistered;
                carryItem.ObjectiveItemSolved = items[ItemIndex].ObjectiveItemSolved;
                carryItem.ObjectiveItemSolvedHasBeenHandled = items[ItemIndex].ObjectiveItemSolvedHasBeenHandled;
                carryItem.WardenObjectiveChainIndex = items[ItemIndex].WardenObjectiveChainIndex;

                // Swap our new item into the objective item array
                items[ItemIndex] = carryItem.Cast<iWardenObjectiveItem>();
            }

            wrapper.AddSpawnCallback(SetupObjectiveCarryItem);
            return wrapper;
        }
    }

    // Objective requiring the retrieval of one or more big pickups, which may be of multiple (varying) item types
    [Objective.Callback]
    public void HandleRetrieveBigItemsObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.RetrieveBigItems)
            return;

        /* Similar to small items, we create one region per item we need to pick up
         * Each region will contain the events relevant to picking up that number of pickups
         * Placements are looped, so if we only have one list of zones it's reused; if two, it alternates; etc
         */
        List<List<RegionID>> regionSets = data.ObjectiveToZoneRegionSets(data.Objective.Retrieve_Items.Count).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();

        ItemID category = data.Item_BigRetrievals_ByObjective;
        RegionID last = data.Region_Objective;
        for (int i = 1; i <= data.Objective.Retrieve_Items.Count; i++)
        {
            // Note that retrieval targets cannot be used as normal items, and so cannot currently be added the same way
            data.Locations.CreateValue(
                data.Location_BigRetrieval_Instance(i),
                regionSets[i - 1],
                new LocationData(),
                data.Item_BigRetrieval_Instance(i)
            );

            RegionID newRegion = data.Region_RetrievedItem(i);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = newRegion,
                Reqs = new(Path.eType.Category, category, (uint)i),
            });
            last = newRegion;

            eventWrapper.Process(newRegion);
        }

        SharedObjectiveHandler.AddObjectiveCompleteItem(data, last);
    }

    /// <summary>
    /// See the similar explanation in 09_CentralGenClusterHandler: <see cref="CentralGenClusterHandler.LG_Distribute_WardenObjective____c__DisplayClass8_1___DistributePickupItems_b__0__Patch"/>
    /// </summary>
    [ArchivePatch(typeof(LG_Distribute_WardenObjective.__c__DisplayClass8_1), nameof(LG_Distribute_WardenObjective.__c__DisplayClass8_1._DistributePickupItems_b__0))]
    public static class LG_Distribute_WardenObjective____c__DisplayClass8_1___DistributePickupItems_b__0__Patch
    {
        public static void Postfix(LG_Distribute_WardenObjective.__c__DisplayClass8_1 __instance, LG_Zone zone)
        {
            Objective.Data data = Layer.Data.GetFromLayerFlattened(zone.Layer)
                .GetObjectiveDatas()
                .ElementAt(__instance.field_Public___c__DisplayClass8_0_0.chainIndex);
            if (data.Objective.Type != eWardenObjectiveType.RetrieveBigItems) 
                return;

            PickupHelper.AssociateDistributionWithLocation(
                LG_Factory.Current.m_currentBatch.Jobs.FromEnd().Cast<LG_Distribute_PickupItemsPerZone>(),
                data.Location_BigRetrieval_Instance(__instance.i + 1)
            );
        }
    }

    /// <summary>
    /// Prevents the exit scan from adding removed items on reload
    /// </summary>
    [ArchivePatch(typeof(WardenObjectiveManager), nameof(WardenObjectiveManager.OnStateChange))]
    public static class WardenObjectiveManager__OnStateChange__Patch
    {
        public static void Prefix(ref pWardenObjectiveState newState, bool isRecall)
        {
            if (!isRecall) return;
            for (int i = 0; i < newState.RequiredObjectiveItems.Length; i++)
                newState.RequiredObjectiveItems[i] = 255; // Sentinel value to ignore this field
        }
    }

}
