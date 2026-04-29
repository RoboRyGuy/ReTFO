using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class RetrieveBigItemsHandler_Tags
{ 
    extension (Game.Data data)
    {
        public TagResolver Tag_BigRetrievalLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Big Retrieval Locations", "Locations checked by picking up big retrieval objective items, IE in R2A1", gd.Tag_BigPickupLocations));

        public TagResolver Tag_BigRetrievalItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Big Retrieval Items", "Big pickup items marked as retrieval objective items, IE the cargos in R2A1", gd.Tag_BigPickupItems));
    }

    extension (Objective.Data data)
    {
        public TagResolver Tag_BigRetrievalLocations_ByObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Big Retrieval Locations", "Locations checked by picking up big retrieval items for a particular objective", gd.Tag_BigRetrievalLocations));

        public TagResolver Tag_BigRetrievalItems_ByObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Big Retrieval Items", "Big pickup items marked as big retrieval items for a particular objective", gd.Tag_BigRetrievalItems));
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

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.RetrieveBigItems;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return $"Retrieve {data.Objective.Retrieve_Items.Count}x Big Items";
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

        // Helper to get the full name for This objective
        public static string ObjectiveName(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return data.ObjectiveName(ObjectiveSummary(data));
        }
    }

    // Names of regions for this objective
    private static class ThisRegions
    {
        // Region entered when retrieving the big item(s)
        public static string RetrievedItem(Objective.Data data, int count)
            => $"{This.ObjectiveName(data)} {count} Big Items Retrieved";
    }

    // Location where a big retrieval item can be found
    private static class BigRetrieval_Location
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Retrieval Location #{count}", "A location containing a particular retrieval target", data.Tag_BigRetrievalLocations_ByObjective));

        public static LocationData MakeRandData() => new LocationData();
    }

    // Big retrieval item itself - note that despite this being usable as a normal item, we disallow it by changing its name
    private class BigRetrieval_Item : Item
    {
        public BigRetrieval_Item(Objective.Data data, int count)
            : base(MakeTag(data, count), MakeRandData())
        {
            Data = data;
            ItemIndex = count - 1;
        }

        public static RandomizationTag MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Retrieval Item #{count}", "A particular big retrieval item", data.Tag_BigRetrievalItems_ByObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        /// <summary>
        /// The expedition this item was created for
        /// </summary>
        public Objective.Data Data { get; set; }

        /// <summary>
        /// Which item in the obejctive this refers to, 0-indexed
        /// </summary>
        public int ItemIndex { get; set; }

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, Data.Tag_BigRetrievalItems_ByObjective);

        /// <summary>
        /// Immediately attempt to spawn the related big pickup.
        /// Spawning must be async because the host must approve it.
        /// </summary>
        /// <returns>A wrapper around the spawn attempt. This will later contain the item if it successfully spawns.</returns>
        private AsyncItemSpawnWrapper TrySpawnAsync()
        {
            var wrapper = new AsyncItemSpawnWrapper();
            ItemDataBlock itemDataBlock = ItemDataBlock.GetBlock(Data.Objective.Retrieve_Items[ItemIndex]);
            if (itemDataBlock != null)
                ItemReplicationManager.SpawnItem(
                    new pItemData() { itemID_gearCRC = itemDataBlock.persistentID },
                    new Action<ISyncedItem, PlayerAgent>(wrapper.OnSpawn),
                    ItemMode.Pickup,
                    Vector3.zero,
                    Quaternion.identity,
                    null,
                    null
                );
            return wrapper;
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (Data.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (Data.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            // Isolating these for the lambda just in case
            ItemDataBlock itemDataBlock = ItemDataBlock.GetBlock(Data.Objective.Retrieve_Items[ItemIndex]);
            string itemName = $"Big Pickup #{itemDataBlock?.persistentID ?? 0} \"{itemDataBlock?.publicName ?? "null"}\"";
            var wrapper = TrySpawnAsync();
            var node = terminal.SpawnNode;

            // We'll calculate a random raycast behind the player using their camera view
            // If we get a good hit, we'll place the item there. Otherwise, we'll place it at their feet
            System.Random rand = new(Guid.NewGuid().GetHashCode()); // Hopefully this is entropic enough

            // There's probably an easier way to calculate this. Whatever, this works!
            PlayerAgent player = terminal.m_syncedInteractionSource;
            Vector3 position = player.FPSCamera.Position;
            Vector3 right = player.FPSCamera.FlatRight;
            Vector3 backVector = Quaternion.AngleAxis(30f + rand.NextSingle() * 120f, Vector3.up) * right;
            Quaternion fullDown = Quaternion.FromToRotation(backVector, Vector3.down);
            Quaternion downAngle = Quaternion.Lerp(Quaternion.identity, fullDown, .4f + rand.NextSingle() * .4f);
            Vector3 testVector = downAngle * backVector;
            if (Physics.Raycast(position, testVector, out RaycastHit hit, 10f, 1))
                position = hit.point;
            Quaternion rotation = Quaternion.AngleAxis(360f * rand.NextSingle(), Vector3.up);

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Retrieving {itemName}", 2f);
            };

            yield return () =>
            {
                CarryItemPickup_Core? carryItem = wrapper.Item?.TryCast<CarryItemPickup_Core>();
                if (carryItem == null)
                {
                    // Effectively timed out. If it successfully spawns after now, we don't want it anymore
                    wrapper.QueueDespawn();
                    FeatureLogger.Error($"Failed to spawn {itemName}!");
                    stateTracker.AddItemToTerminal(this);
                    terminal.AddLine($"<#F00>Failed to retrieve {itemName}! It has been re-added to terminal system.</color>");
                    return;
                }

                carryItem.SpawnNode = Data.GetLG_Layer()!.m_zones[0].m_courseNodes[0];
                carryItem.Set_pItemData(new pItemData()
                {
                    itemID_gearCRC = carryItem.pItemData.itemID_gearCRC,
                    originLayer = Data.LayerType
                });
                carryItem.m_isWardenObjective = true;

                // Overwriting the originally-spawned item
                var items = WardenObjectiveManager.GetObjectiveItemCollection(Data.LayerType, Data.ObjectiveIndex);
                ChainedPuzzles.CP_Bioscan_Core? core;
                if (WardenObjectiveManager.m_customGeoExitWinConditionItem != null)
                    core = WardenObjectiveManager.m_customGeoExitWinConditionItem.Cast<LG_LevelExitGeo>().m_puzzle.m_chainedPuzzleCores[0].TryCast<ChainedPuzzles.CP_Bioscan_Core>();
                else
                    core = WardenObjectiveManager.m_elevatorExitWinConditionItem.Cast<ElevatorShaftLanding>().m_puzzle.m_chainedPuzzleCores[0].TryCast<ChainedPuzzles.CP_Bioscan_Core>();

                // If the original item is registered as required for the exit scan (or if we can't check)
                if (core == null || core.m_reqItems.Any(i => i.Pointer == items[ItemIndex].Pointer))
                {   // Try to swap in our new item instead
                    Il2CppReferenceArray<iWardenObjectiveItem> originalItem = new(1);
                    originalItem[0] = items[ItemIndex];

                    Il2CppReferenceArray<iWardenObjectiveItem> newItem = new(1);
                    newItem[0] = carryItem.Cast<iWardenObjectiveItem>();

                    WardenObjectiveManager.RemoveObjectiveItemAsRequiredForExitScan(originalItem);
                    WardenObjectiveManager.AddObjectiveItemAsRequiredForExitScan(true, newItem);
                }

                // Swap our new item into the objective item array
                items[ItemIndex] = carryItem.Cast<iWardenObjectiveItem>();

                carryItem.m_sync.AttemptPickupInteraction(
                    ePickupItemInteractionType.Place,
                    player.Owner, default,
                    position, rotation,
                    node, true, true
                );
                carryItem.m_navMarkerPlacer.SetMarkerVisible(true);
                carryItem.m_terminalItem.PlayPing();
                terminal.AddLine($"{itemName} has been placed somewhere nearby");
            };
        }
    }

    public static KeyedItem GetItem(Objective.Data data, int count)
    {
        if (data.TryLookupItem(BigRetrieval_Item.MakeTag(data, count), out var item))
            return item;

        Item newItem = new BigRetrieval_Item(data, count);
        return new(data.AddItem(newItem), newItem);
    }

    // Objective requiring the retrieval of one or more big pickups, which may be of multiple (varying) item types
    [Objective.Callback]
    public void HandleRetrieveBigItemsObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        /* Similar to small items, we create one region per item we need to pick up
         * Each region will contain the events relevant to picking up that number of pickups
         * Placements are looped, so if we only have one list of zones it's reused; if two, it alternates; etc
         */
        List<List<RegionID>> regionSets = data.ObjectiveToZoneRegionSets(data.Objective.Retrieve_Items.Count).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();

        RegionID last = data.ObjectiveStartRegion;
        for (int i = 1; i <= data.Objective.Retrieve_Items.Count; i++)
        {
            // Note that retrieval targets cannot be used as normal items, and so cannot currently be added the same way
            KeyedItem item = GetItem(data, i);
            data.AddLocation(
                BigRetrieval_Location.MakeTag(data, i),
                regionSets[i - 1],
                BigRetrieval_Location.MakeRandData(),
                item.ID
            );

            string regionName = ThisRegions.RetrievedItem(data, i);
            RegionID newRegion = data.LookupOrCreateRegion(regionName);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = newRegion,
                ReqItem = item.PathReqs,
                ReqCount = 1u
            });
            last = newRegion;

            eventWrapper.Process(newRegion, regionName);
        }

        SharedObjectiveHandler.AddObjectiveCompleteItem(data, last);
    }

    /// <summary>
    /// See the similar explanation in 09_CentralGenClusterHandler
    /// </summary>
    [ArchivePatch(typeof(LG_Distribute_WardenObjective.__c__DisplayClass8_1), nameof(LG_Distribute_WardenObjective.__c__DisplayClass8_1._DistributePickupItems_b__0))]
    public static class LG_Distribute_WardenObjective____c__DisplayClass8_1___DistributePickupItems_b__0__Patch
    {
        public static void Postfix(LG_Distribute_WardenObjective.__c__DisplayClass8_1 __instance, LG_Zone zone)
        {
            Objective.Data data = Layer.Data.FromLayerFlattened(zone.Layer).GetObjectiveDatas().ElementAt(__instance.field_Public___c__DisplayClass8_0_0.chainIndex);
            if (data.Objective.Type != This.ObjectiveType) return;

            if (data.TryLookupLocation(BigRetrieval_Location.MakeTag(data, __instance.i + 1), out var loc))
            {
                PickupHelper.AssociateDistributionWithLocation(
                    LG_Factory.Current.m_currentBatch.Jobs.FromEnd().Cast<LG_Distribute_PickupItemsPerZone>(),
                    loc.ID
                );
            }
            else
                FeatureLogger.Error("Failed to lookup big retrieval target location during association");
        }

    }

}
