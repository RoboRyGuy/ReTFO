using Clonesoft.Json;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using ReTFO.Archipelago.Utilities;

[EnableFeatureByDefault]
public class CentralGenClusterHandler : ArchipelagoFeature
{
    public override string Name => "Central Generator Cluster Handler";
    public override string Description
        => "Handles the CentralGenCluster objective type.\n"
        + "Example: R2D1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /* TODO:
     *  Location: Detect finding gen cluster? Auto-discover on zone entry works here, too
     *            Cell discovery not handled here
     *  Item: Add ability to receive gen over network - dynamically replace marker?
     *        Cell item not handled here
     *  Region: FoundGen not currently detected
     *          PoweredGen currently detected using OnSolve events
     */

    private class GenClusterLocation : Location
    {
        public GenClusterLocation(string name, RegionList regions, Item? item = null) 
            : base(name, regions, item) { }

        private static RandomizationData s_randData = new()
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    private class GenClusterItem : Item
    {
        public GenClusterItem(string name, int size, Objective.Data data)
            : base(name)
        {
            ObjectiveData = data;
            this.size = size;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }

        [JsonIgnore]
        public int size { get; set; }

        private static RandomizationData s_randData = new()
        {
            Categories = new() { "All", "Objective Items", "Function Markers", "Central Generator Clusters" },
        };
        public override RandomizationData RandData => s_randData;
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.CentralGeneratorCluster;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{data.Objective.CentralPowerGenClustser_NumberOfGenerators}x Central Gen Cluster";
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

    private static string ThisGenItemName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Gen Cluster";
    }

    private static string ThisGenLocationName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Gen Cluster Location";
    }

    private static string ThisCellLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Cell Spawn #{count}";
    }

    private static string ThisFoundGenRegionName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Sample Collected";
    }

    private static string ThisPoweredGenRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Powered Gen #{count}";
    }

    // Objective requiring one or more cells be found in the map and used to power a central generator cluster
    [Objective.Callback]
    public void HandleCentralGenGlusterObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // Central gen requires a) us to place cells in the map, b) us to find the central gen, and c) events when each cell is inserted
        // a) Placing cells in the map
        List<List<int>> regionSets = data.PlacementsToZoneRegions(data.ObjectiveData.ZonePlacementDatas)
            .Select(ps => ps.Select(i => i.Region).ToList())
            .TakeLooped(data.Objective.CentralPowerGenClustser_NumberOfPowerCells)
            .ToList();
        for (int i = 1; i <= data.Objective.CentralPowerGenClustser_NumberOfPowerCells; i++)
        {
            data.GetLocation(new GenClusterLocation(
                ThisCellLocationName(data, i),
                regionSets[i - 1],
                BigPickupHelper.GetBigPickupItem(data, BigPickupHelper.CellItemID)
            ));
        }

        // b) Finding the central gen cluster
        Zone.Data? clusterZone = null;
        foreach (var zone in data.AllZones)
        {
            if ((zone.Zone?.GeneratorClustersInZone ?? 0) > 0)
            {
                clusterZone = zone;
                break;
            }
        }
        if (clusterZone == null)
        {
            FeatureLogger.Warning($"Failed to find gen cluster for objective: {ThisObjectiveName(data)}");
            clusterZone = data.FirstZone;
        }

        Item genItem = data.GetItem(new GenClusterItem(ThisGenItemName(data), data.Objective.CentralPowerGenClustser_NumberOfGenerators, data));
        data.GetLocation(new GenClusterLocation(
            ThisGenLocationName(data),
            data.GetOrCreateRegion(clusterZone.ZoneName),
            genItem
        ));

        // This region represents having found the gen cluster
        int foundGenClusterRegion = data.GetOrCreateRegion(ThisFoundGenRegionName(data));
        Path path = data.AddPath(data.ObjectiveStartRegion, foundGenClusterRegion);
        path.RequiredItem = genItem.Name;
        path.RequiredItemCount = 1u;

        // c) Regions and events based on available cell counts
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        int last = foundGenClusterRegion;
        for (int i = 1; i <= data.Objective.CentralPowerGenClustser_NumberOfGenerators; i++)
        {
            string newRegionName = ThisPoweredGenRegionName(data, i);
            int newRegion = data.GetOrCreateRegion(newRegionName);
            path = data.AddPath(last, newRegion);
            path.RequiredItem = BigPickupHelper.GetBigPickupItem(data, BigPickupHelper.CellItemID).Name;
            path.RequiredItemCount = 1u;
            last = newRegion;

            eventWrapper.Process(newRegion, newRegionName);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), last);
    }

}
