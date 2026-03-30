using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class GatherSmallItemsHandler : ArchipelagoFeature
{
    public override string Name => "Reactor Gather Small Pickups Handler";
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

    /*
     * TODO:
     *  Location: Detect when a pickup is grabbed, and auto-discover empty spots when entering a region
     *  Item: Add ability to receive item over network
     *  Region: Currently detected using OnSolve events
     */

    private class GatherSmallItemsLocation : Location
    {
        public GatherSmallItemsLocation(string name, RegionList regions, Item? item)
            : base(name, regions, item) { }

        private static RandomizationData s_randData = new()
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    private class GatherSmallItemsItem : Item
    {
        public GatherSmallItemsItem(string name, Objective.Data data)
            : base(name)
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }

        public override RandomizationData RandData => new()
        {
            Categories = new() { "All", "Objective Items", "Small Pickups", $"{ItemDataBlock.GetBlock(ObjectiveData.Objective.Gather_ItemId).publicName ?? "Null Item"}s" },
        };
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.GatherSmallItems;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        ItemDataBlock item = ItemDataBlock.GetBlock(data.Objective.Gather_ItemId);
        if (item == null)
            FeatureLogger.Error($"Failed to find gather item datablock for objective: {data.ObjectiveName(null)}");
        return $"Gather {data.Objective.Gather_RequiredCount}x \"{item?.publicName ?? "null!"}\"";
    }

    private static bool ThisIsCorrectObjective(Objective.Data data)
        => data.Objective.Type == ThisObjectiveType;

    private static void CheckThisIsCorrectObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            FeatureLogger.Error($"Wrong objective type! Expected {Enum.GetName(ThisObjectiveType)}, got {data.Objective.Type}");
    }

    private static string ThisObjectiveName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return data.ObjectiveName(ThisObjectiveSummary(data));
    }

    private static string ThisItemName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Spawn Spot Checked";
    }

    private static string ThisLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Spawn Spot #{count}";
    }

    private static string ThisRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Collected {count} Items";
    }

    // Compares two ZonePlacement datas to see if they refer to the same zone
    private class ZonePlacementEqualityComparer : IEqualityComparer<ZonePlacementData>
    {
        public bool Equals(ZonePlacementData? x, ZonePlacementData? y)
            => x?.LocalIndex == y?.LocalIndex && x?.DimensionIndex == y?.DimensionIndex;

        public int GetHashCode(ZonePlacementData obj)
            => Tuple.Create(obj.LocalIndex, obj.DimensionIndex).GetHashCode();
    }

    // Objective requiring picking up a certain number of small items
    [Objective.Callback]
    public void HandleGatherSmallItemsObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // First thing we need to know is how many items can be "missing"
        // For example, in R1B1, there are 18 total IDs, 7 spawn zones, and up to 3 per zone
        // Therefore, there are (7*3)-18 = 3 "missing" IDs (7*3=21 spawn spots, 3 of which will be empty)

        // A lot of test/debug levels use this objective type with no placement data. Seems GTFO just uses the first zone by default
        List<ZonePlacementData> placements;
        if (data.ObjectiveData.ZonePlacementDatas.Count == 0)
            placements = new(1) { new() };
        else
        {   // The way items are placed varies a lot by level. This seems to work for all cases
            placements = data.ObjectiveData.ZonePlacementDatas
                .SelectMany(ps => ps.Iter())
                .Distinct(new ZonePlacementEqualityComparer())
                .ToList();
        }

        // Calculating actual "missing" count
        if (data.Objective.Gather_MaxPerZone <= 0)
            throw new ArgumentException($"{ThisObjectiveName(data)}: Expected positive MaxPerZone, got {data.Objective.Gather_MaxPerZone}");
        int numSpawnSpots = placements.Count * data.Objective.Gather_MaxPerZone;
        int numMissing = numSpawnSpots - data.Objective.Gather_SpawnCount;
        if (numMissing < 0) numMissing = 0; // This occurs on R7C2 overload, for example. We could handle it... TODO
        int requiredCount = numMissing + data.Objective.Gather_RequiredCount;

        // Placing spawn spots as pickups in the world
        Item item = data.GetItem(new GatherSmallItemsItem(ThisItemName(data), data));
        int count = 0;
        foreach (var placement in placements)
        {
            int region = data.GetOrCreateRegion(data.FindZoneByPlacement(placement)!.ZoneName);
            for (int i = 0; i < data.Objective.Gather_MaxPerZone; i++)
            {
                ++count;
                data.GetLocation(new GatherSmallItemsLocation(
                    ThisLocationName(data, count),
                    region,
                    item
                ));
            }
        }

        // We track progression not by how many pickups can be found, but instead by how many spawn spots can be found
        // The first numMissing spawn spots are assumed empty (because that is worst case), and therefore trigger no events
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        int last = data.ObjectiveStartRegion;
        for (int i = 1 + numMissing; i <= numSpawnSpots; i++)
        {
            // Add the region and chain it to the previous ones
            string regionName = ThisRegionName(data, i - numMissing);
            int newRegion = data.GetOrCreateRegion(regionName);
            Path path = data.AddPath(last, newRegion);
            path.RequiredItem = item.Name;
            path.RequiredItemCount = (uint)(i == (1 + numMissing) ? i : 1);
            last = newRegion;

            // If this is the required count, put the objective completion in
            if (i == requiredCount)
                SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), newRegion);

            // Process events
            eventWrapper.Process(newRegion, regionName);
            if (i >= requiredCount && eventWrapper.IsDone) break;
        }
    }

}
