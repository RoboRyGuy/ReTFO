using AIGraph;
using GameData;
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
using UnityEngine;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Diagnostics;

public static class GatherSmallItemsHandler_Tags
{ 
    extension (Game.Data data)
    {
        public TagResolver Tag_GatherItemsLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Small Objective Items Spawn Locations", "Locations checked by picking up small objective items (for example, PIDs)", gd.Tag_SmallPickupLocations));

        public TagResolver Tag_GatherItemsItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Small Objective Items", "Items granting progress toward a \"Gather Small Items\" objective", gd.Tag_SmallPickupItems));
    }

    extension (Objective.Data data)
    {
        public TagResolver Tag_GatherItemsItems_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Small Objective Items", "Items granting progress toward a particular objective", gd.Tag_GatherItemsItems));
    }
}


[EnableFeatureByDefault, AutomatedFeature, InjectToIl2Cpp]
public class GatherSmallItemsHandler : ArchipelagoFeature
{
    public override string Name => "Gather Small Pickups Handler";
    public override string Description
        => "Handles the GatherSmallItems objective type.\n"
        + "Example: R1B1";
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
            = eWardenObjectiveType.GatherSmallItems;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            ItemDataBlock item = ItemDataBlock.GetBlock(data.Objective.Gather_ItemId);
            if (item == null)
                FeatureLogger.Error($"Failed to find gather item datablock for objective: {data.ObjectiveName(null)}");
            return $"Gather {data.Objective.Gather_RequiredCount}x \"{item?.publicName ?? "null!"}\"";
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

    // Regions used by this objective type
    private static class ThisRegions
    {
        // Region entered when items are found
        public static string FoundItem(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Found {count} Items";
    }

    // Location where a small item can be found
    private static class GatherSmall_SpawnLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Spawn Spot #{count}", "A particular small objective pickup spawn location", gd.Tag_GatherItemsLocations));

        public static LocationData MakeRandData() => new LocationData() { };
    }

    // The actual small item
    private class GatherSmall_Item : Item
    {
        public GatherSmall_Item(Objective.Data data, bool isEmpty)
            : base(MakeTag(data, isEmpty), MakeRandData())
        {
            ObjectiveData = data;
            IsEmpty = isEmpty;
        }

        public static TagResolver MakeTag(Objective.Data data, bool isEmpty)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} {(isEmpty ? "Empty " : "Pickup")}", "A particular small objective pickup item", data.Tag_GatherItemsItems_PerObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, ObjectiveData.Tag_GatherItemsItems_PerObjective);

        public Objective.Data ObjectiveData { get; set; }

        public bool IsEmpty { get; set; }

        public override Expedition.Data? RequiredExpedition => ObjectiveData;

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (!IsEmpty && ObjectiveData.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (!IsEmpty && ObjectiveData.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            AsyncItemSpawnWrapper? wrapper = new();
            pItemData itemData = new()
            {
                itemID_gearCRC = ObjectiveData.Objective.Gather_ItemId,
                originCourseNode = new(),
                originLayer = ObjectiveData.LayerType,
            };
            itemData.originCourseNode.Set(ObjectiveData.GetLG_Layer()!.m_zones[0].m_courseNodes[0]);
            if (SNetwork.SNet.IsMaster)
            { 
                ItemReplicationManager.SpawnItem(
                    itemData,
                    new Action<ISyncedItem, PlayerAgent>(wrapper.OnSpawn),
                    ItemMode.Pickup,
                    Vector3.zero,
                    Quaternion.identity,
                    null,
                    null
                );
            }
            var player = terminal.m_syncedInteractionSource.Owner;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving your small pickup...", 2f);
            };

            yield return () =>
            {
                GenericSmallPickupItem_Core? keyItem = wrapper.Item?.TryCast<GenericSmallPickupItem_Core>();
                if (keyItem == null)
                {
                    if (SNetwork.SNet.IsMaster)
                    {
                        stateTracker.AddItemToTerminal(this);
                        FeatureLogger.Error("Failed to spawn small pickup!");
                        terminal.AddLine("<#F00>Failed to retrieve small pickup! It has been re-added to terminal system.</color>");
                        wrapper.QueueDespawn();
                    }
                    return;
                }

                keyItem.SetupFromLevelgen(0, true);
                keyItem._SpawnNode_k__BackingField = ObjectiveData.GetLG_Layer()!.m_zones[0].m_courseNodes[0];
                keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player);

                terminal.AddLine($"Pickup \"{keyItem.PublicName}\" has been given to {player.NickName}");
            };
        }
    }

    public static KeyedItem GetItem(Objective.Data data, bool isEmpty)
    {
        if (data.TryLookupItem(GatherSmall_Item.MakeTag(data, isEmpty), out var item))
            return item;

        Item newItem = new GatherSmall_Item(data, isEmpty);
        return new(data.AddItem(newItem), newItem);
    }

    // Compares two ZonePlacement datas to see if they refer to the same zone
    private class ZonePlacementEqualityComparer : IEqualityComparer<ZonePlacementData>
    {
        public bool Equals(ZonePlacementData? x, ZonePlacementData? y)
            => x?.LocalIndex == y?.LocalIndex && x?.DimensionIndex == y?.DimensionIndex;

        public int GetHashCode(ZonePlacementData obj)
            => Tuple.Create(obj.LocalIndex, obj.DimensionIndex).GetHashCode();
    }

    // Calculate how many empty spawn locations we'll have. Outputs the zone placement data for reference as well
    public static int CalcEmptySpots(Objective.Data data, out List<ZonePlacementData> placements)
    {
        if (data.ObjectiveData.ZonePlacementDatas.Count == 0)
            placements = new(1) { new() };
        else
        {   // The way items are placed varies a lot by level. This seems to work for all cases
            placements = data.ObjectiveData.ZonePlacementDatas
                .SelectMany(ps => ps.Iter())
                .Distinct(new ZonePlacementEqualityComparer())
                .ToList();
        }

        if (data.Objective.Gather_MaxPerZone <= 0)
            throw new ArgumentException($"{data.ObjectiveName()}: Expected positive MaxPerZone, got {data.Objective.Gather_MaxPerZone}");
        int numSpawnSpots = placements.Count * data.Objective.Gather_MaxPerZone;
        int numMissing = numSpawnSpots - data.Objective.Gather_SpawnCount;
        if (numMissing < 0) numMissing = 0; // This occurs on R7C2 overload, for example. We could handle it... TODO
        return numMissing;
    }

    // Objective requiring picking up a certain number of small items
    [Objective.Callback]
    public void HandleGatherSmallItemsObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // Placing spawn spots as pickups in the world
        int numMissing = CalcEmptySpots(data, out var placements);
        KeyedItem actualItem = GetItem(data, false);
        KeyedItem falseItem = numMissing > 0 ? GetItem(data, true) : default;
        int count = 0;
        foreach (var placement in placements)
        {
            // When creating associations, it's easier to assume the last numMissing spots are empty (as opposed to the first numMissing)
            RegionID spotRegion = data.LookupOrCreateRegion(data.FindZoneByPlacement(placement).ZoneName);
            for (int i = 0; i < data.Objective.Gather_MaxPerZone; i++)
            {
                data.AddLocation(
                    GatherSmall_SpawnLocation.MakeTag(data, ++count),
                    spotRegion,
                    GatherSmall_SpawnLocation.MakeRandData(),
                    (count <= numMissing ? falseItem : actualItem).ID
                );
            }
        }

        // On specifically R7C2, it requests 5 spawns but limits it to 4 per zone (and only spawns in 1 zone).
        // This is generalized logic for handling cases like that; it's probably not perfect :/
        if (count < data.Objective.Gather_SpawnCount)
        {
            RegionList placement = data.PlacementsToZoneRegions(placements).Select(info => info.Region).ToList();
            while (count < data.Objective.Gather_SpawnCount)
            {
                data.AddLocation(
                    GatherSmall_SpawnLocation.MakeTag(data, ++count),
                    placement,
                    GatherSmall_SpawnLocation.MakeRandData(),
                    (count <= numMissing ? falseItem : actualItem).ID
                );
            }
        }

        // "Found item #0" starts after finding the first numMissing spots, and exists to make the loop easier to write
        string regionName = ThisRegions.FoundItem(data, 0);
        RegionID region = data.LookupOrCreateRegion(regionName);
        Path firstPath = new()
        {
            StartingRegion = data.ObjectiveStartRegion,
            EndingRegion = region,
        };
        if (numMissing > 0)
        {
            firstPath.ReqItem = actualItem.Item.PathReqs;
            firstPath.ReqCount = (uint)numMissing;
        }
        data.AddPath(firstPath);

        // Now for each item, we can add the new region using the 0 region as a starting point
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        RegionID last = region;
        for (int i = 1; i <= data.Objective.Gather_SpawnCount; i++)
        {
            // Add the region and chain it to the previous ones
            regionName = ThisRegions.FoundItem(data, i);
            region = data.LookupOrCreateRegion(regionName);
            data.AddPath(new Path()
            {
                StartingRegion = last, 
                EndingRegion = region,
                ReqItem = actualItem.Item.PathReqs,
                ReqCount = 1u,
            });
            last = region;

            // If this is the required count, put the objective completion in
            if (i == data.Objective.Gather_RequiredCount)
                SharedObjectiveHandler.AddObjectiveCompleteItem(data, region);

            // Process events
            eventWrapper.Process(region, regionName, true);

            // This early exit prevents some regions from being added. I've decided I want those regions
            //if (i >= requiredCount && eventWrapper.IsDone) break;
        }
    }

    /// <summary>
    /// This comp is attached to LG_Zone gameobjects, and contains a list of locations
    ///  which will be discovered immediately when the zone is entered. This is used to
    ///  handle "missing" spawn spots
    /// </summary>
    [InjectToIl2Cpp]
    private class GatherSmall_FreeLcoationsComp : MonoBehaviour
    {
        public GatherSmall_FreeLcoationsComp(IntPtr ptr) : base(ptr) { }
        public List<LocationID> LocationIDs = new(0);
    }

    /// <summary>
    /// Once the objective is built, we can create associations for all spawned items and identify
    ///  locations which weren't spawned and inject checks for them.
    /// "Spawned Items" are newly queued LG_Distribute_PickupItemsPerZone jobs. We assume the last numSpawns jobs are related
    /// </summary>
    [ArchivePatch(typeof(LG_Distribute_WardenObjective), nameof(LG_Distribute_WardenObjective.BuildWardenObjective))]
    public static class LG_Distribute_WardenObjective__BuildWardenObjective__Patch
    {
        public static void Postfix(LG_Distribute_WardenObjective __instance, int chainIndex)
        {
            Objective.Data data = Layer.Data.FromLayer(__instance.m_layer).GetObjectiveDatas().ElementAt(chainIndex);
            if (This.IsCorrectObjective(data))
            {
                // Placement lookup for placements
                // Key: Zone.Pointer, Value: Tuple of (Zone placement index, count of placements actually spawned in that zone)
                Dictionary<IntPtr, Tuple<int, int>> placementCounts = new();
                int count = 0;
                int numMissing = CalcEmptySpots(data, out var placements);
                foreach (var placement in placements)
                {
                    LG_Zone zone = data.FindZoneByPlacement(placement).GetLG_Zone()!;
                    placementCounts.Add(zone.Pointer, Tuple.Create(count++, 0));
                }

                // Some useful numbers
                int maxPerZone = __instance.m_dataBlockData.Gather_MaxPerZone;
                int maxNormal = placements.Count * maxPerZone;
                int spawnCount = __instance.m_dataBlockData.Gather_SpawnCount;
                var jobQueue = LG_Factory.Current.m_currentBatch.Jobs;
                var ourHead = jobQueue._head + jobQueue._size - spawnCount;
                var jobs = Enumerable.Range(ourHead, spawnCount).Select(i => jobQueue._array[i]);

                foreach (var baseJob in jobs)
                {
                    // Update the spawn count
                    LG_Distribute_PickupItemsPerZone job = baseJob.Cast<LG_Distribute_PickupItemsPerZone>();
                    var counts = placementCounts[job.m_zone.Pointer];
                    counts = Tuple.Create(counts.Item1, counts.Item2 + 1);
                    placementCounts[job.m_zone.Pointer] = counts;

                    // Calulcate which location actually spawned here.
                    // If it's a normal spawn, we can cal its index normally; otherwise, we need to pull one of the overflow
                    if (counts.Item2 > maxPerZone)
                        count = ++maxNormal;
                    else
                        count = counts.Item1 * maxPerZone + counts.Item2;

                    if (!data.TryLookupLocation(GatherSmall_SpawnLocation.MakeTag(data, count), out var loc))
                    {
                        FeatureLogger.Error("Failed to lookup small gather item during association!");
                        continue;
                    }

                    // Associate!
                    PickupHelper.AssociateDistributionWithLocation(job, loc.ID);
                }

                // For any spots that didn't spawn, we need to attach our comp to help us check it later
                foreach (var pair in placementCounts)
                {
                    if (pair.Value.Item2 < maxPerZone)
                    {
                        LG_Zone zone = new(pair.Key);
                        count = pair.Value.Item2;
                        var comp = zone.gameObject.AddComponent<GatherSmall_FreeLcoationsComp>();
                        comp.LocationIDs = new(maxPerZone - count);
                        while (count++ < maxPerZone)
                        {
                            if (data.TryLookupLocation(GatherSmall_SpawnLocation.MakeTag(data, pair.Value.Item1 * maxPerZone + count), out var loc))
                                comp.LocationIDs.Add(loc.ID);
                            else
                                FeatureLogger.Error("Failed to find location(s) while generating free locations comp");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check if entering a new node and, if so, check the zone for free location checks
    /// </summary>
    [ArchivePatch(typeof(PlayerAgent), nameof(PlayerAgent.SetCourseNode))]
    public static class PlayerAgent__SetCourseNode__Patch
    {
        public static void Prefix(PlayerAgent __instance, AIG_CourseNode courseNode)
        {
            if ((__instance?.CourseNode?.m_zone?.Pointer ?? IntPtr.Zero) != (courseNode?.m_zone?.Pointer ?? IntPtr.Zero))
            {
                var comp = courseNode?.m_zone?.GetComponent<GatherSmall_FreeLcoationsComp>();
                if (comp != null)
                {
                    StateTracker.Get().NotifyFoundLocations(comp.LocationIDs, __instance);
                    GameObject.Destroy(comp);
                }
            }
        }
    }

}
