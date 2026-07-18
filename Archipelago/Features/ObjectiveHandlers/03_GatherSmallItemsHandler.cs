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

public static class GatherSmallItemsHandler_Tags
{ 
    extension (Game.Data data)
    {
        public LocationID Location_GatherItems
            => LocationID.From(data, "Small Objective Items Spawn Locations", data => new("Locations checked by picking up small objective items (for example, PIDs)", data.Location_SmallPickups));

        public ItemID Item_GatherItems
            => ItemID.From(data, "Small Objective Items", data => new("Items granting progress toward a \"Gather Small Items\" objective", data.Item_SmallPickups));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.GatherSmallItems;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }


    extension(Objective.Data data)
    {
        public RegionID Region_GatheredItems(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Found {count} Items", data => new("Region enetered when a certain number of small objective items are found", data.Region_Objective));

        public LocationID Location_GatherItems_PerObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Small Objective Locations", data => new("Gather Items locations for a particular objective", data.Location_GatherItems));

        public ItemID Item_GatherItems_PerObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Small Objective Items", data => new("Items granting progress toward a particular objective", data.Item_GatherItems));


        public LocationID Location_GatherItems_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Small Objective Location #{count}", data => new("A particular Gather Items locations", data.Location_GatherItems_PerObjective));

        public ItemID Item_GatherItems_Instance(int count)
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Small Objective Item #{count}", 
                data => new("A particular Gather Items item", data.Item_GatherItems_PerObjective),
                new GatherSmallItemsHandler.GatherSmall_Item(data.Region_Objective, count)
            );
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

    // The actual small item
    public class GatherSmall_Item : TerminalItem
    {
        public GatherSmall_Item(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public int Count { get; private init; }

        public override RegionID TargetRegion => ObjectiveRegion;

        public override void OnEnteredExpedition(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
        {
            Objective.Data data = new(stateTracker.GameData, ObjectiveRegion);
            uint numEmpty = CalcEmptySpots(data, out _);
            if (Count > numEmpty) 
                base.OnEnteredExpedition(stateTracker, sourceLocationId, player, itemId);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            AsyncItemSpawnWrapper? wrapper = new();
            Objective.Data data = new(stateTracker.GameData, ObjectiveRegion);
            pItemData itemData = new()
            {
                itemID_gearCRC = data.Objective.Gather_ItemId,
                originCourseNode = new(),
                originLayer = data.LayerType,
            };
            itemData.originCourseNode.Set(data.GetLG_Layer()!.m_zones[0].m_courseNodes[0]);
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
                        stateTracker.AddItemToTerminal(itemId);
                        FeatureLogger.Error("Failed to spawn small pickup!");
                        terminal.AddLine("<#F00>Failed to retrieve small pickup! It has been re-added to terminal system.</color>");
                        wrapper.QueueDespawn();
                    }
                    return;
                }

                keyItem.SetupFromLevelgen(0, true);
                keyItem._SpawnNode_k__BackingField = data.GetLG_Layer()!.m_zones[0].m_courseNodes[0];
                keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player);

                terminal.AddLine($"Pickup \"{keyItem.PublicName}\" has been given to {player.NickName}");
            };
        }
    }

    // Compares two ZonePlacement datas to see if they refer to the same zone
    private class ZonePlacementEqualityComparer : IEqualityComparer<ZonePlacementData>
    {
        public bool Equals(ZonePlacementData? x, ZonePlacementData? y)
            => x?.LocalIndex == y?.LocalIndex && x?.DimensionIndex == y?.DimensionIndex;

        public int GetHashCode(ZonePlacementData obj)
            => (obj.LocalIndex, obj.DimensionIndex).GetHashCode();
    }

    // Calculate how many empty spawn locations we'll have. Outputs the calculated zone placement data for reference as well
    public static uint CalcEmptySpots(Objective.Data data, out List<ZonePlacementData> placements)
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
            throw new ArgumentException($"{data.ObjectiveName}: Expected positive MaxPerZone, got {data.Objective.Gather_MaxPerZone}");
        int numSpawnSpots = placements.Count * data.Objective.Gather_MaxPerZone;
        int numMissing = numSpawnSpots - data.Objective.Gather_SpawnCount;
        if (numMissing < 0) numMissing = 0; // This occurs on R7C2 overload, for example. We could handle it... TODO
        return (uint)numMissing;
    }

    // Objective requiring picking up a certain number of small items
    [Objective.Callback]
    public void HandleGatherSmallItemsObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.GatherSmallItems)
            return;

        // Placing spawn spots as pickups in the world
        uint numMissing = CalcEmptySpots(data, out var placements);
        int count = 0;
        foreach (var placement in placements)
        {
            // When creating associations, it's easier to assume the last numMissing spots are empty (as opposed to the first numMissing)
            RegionID spotRegion = data.FindZoneByPlacement(placement).Region_Zone;
            for (int i = 0; i < data.Objective.Gather_MaxPerZone; i++)
            {
                ++count;
                data.Locations.CreateValue(
                    data.Location_GatherItems_Instance(count),
                    spotRegion,
                    new LocationData(),
                    data.Item_GatherItems_Instance(count)
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
                ++count;
                data.Locations.CreateValue(
                    data.Location_GatherItems_Instance(count),
                    placement,
                    new LocationData(),
                    data.Item_GatherItems_Instance(count)
                );
            }
        }

        // "Found item #0" starts after finding the first numMissing spots, and exists to make the loop easier to write
        RegionID region = data.Region_GatheredItems(0);
        ItemID category = data.Item_GatherItems_PerObjective;
        data.AddPath(new()
        {
            StartingRegion = data.Region_Objective,
            EndingRegion = region,
            Reqs = numMissing == 0 ? new() : new(Path.eType.Category, category, numMissing),
        });

        // Now for each item, we can add the new region using the 0 region as a starting point
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        RegionID last = region;
        for (int i = 1; i <= data.Objective.Gather_SpawnCount; i++)
        {
            // Add the region and chain it to the previous ones
            region = data.Region_GatheredItems(i);
            data.AddPath(new Path()
            {
                StartingRegion = last, 
                EndingRegion = region,
                Reqs = new(Path.eType.Category, category, numMissing + (uint)i),
            });
            last = region;

            // If this is the required count, put the objective completion in
            if (i == data.Objective.Gather_RequiredCount)
                SharedObjectiveHandler.AddObjectiveCompleteItem(data, region);

            // Process events
            eventWrapper.Process(region, true);

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
            Objective.Data data = Layer.Data.GetFromLayer(__instance.m_layer).GetObjectiveDatas().ElementAt(chainIndex);
            if (data.Objective.Type != eWardenObjectiveType.GatherSmallItems)
                return;

            // Placement lookup for placements
            // Key: Zone.Pointer, Value: Tuple of (Zone placement index, count of placements actually spawned in that zone)
            Dictionary<IntPtr, (int, int)> placementCounts = new();
            int count = 0;
            CalcEmptySpots(data, out var placements); // We actually just need the placements data here...
            foreach (var placement in placements)
            {
                LG_Zone zone = data.FindZoneByPlacement(placement).GetLG_Zone()!;
                placementCounts.Add(zone.Pointer, (count++, 0));
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
                placementCounts[job.m_zone.Pointer] = counts = (counts.Item1, counts.Item2 + 1);

                // Calulcate which location actually spawned here.
                // If it's a normal spawn, we can calc its index normally; otherwise, we need to pull one of the overflow
                if (counts.Item2 > maxPerZone)
                    count = ++maxNormal;
                else
                    count = counts.Item1 * maxPerZone + counts.Item2;

                // Associate!
                PickupHelper.AssociateDistributionWithLocation(job, data.Location_GatherItems_Instance(count));
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
                        comp.LocationIDs.Add(data.Location_GatherItems_Instance(pair.Value.Item1 * maxPerZone + count));
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
