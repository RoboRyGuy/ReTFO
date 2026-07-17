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

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class CentralGenClusterHandler_Tags
{
    extension (Game.Data data)
    {
        public LocationID Location_CentralGenCells
            => LocationID.From(data, "Central Gen Cell Locations", data => new("Locations checked by picking up cells spawned for central gen cluster objectives", data.Location_BigPickups));

        public LocationID Location_CentralGenClusters
            => LocationID.From(data, "Central Gen Cluster Locations", data => new("Locations checked by finding central gen clusters", data.Location_Never));

        public ItemID Item_CentralGenClusters
            => ItemID.From(data, "Central Gen Cluster Items", data => new("Items representing central gen clusters", data.Item_Never));

        public LocationID Location_CentralGenScans
            => LocationID.From(data, "Central Gen Scan Locations", data => new("Locations checked by fully powering a central gen cluster", data.Location_All));

        public ItemID Item_CentralGenScans
            => ItemID.From(data, "Central Gen Scan Items", data => new("Items which start the final gen cluster scan (which normally occurs when it's fully powered)", data.Item_Scans));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.CentralGeneratorCluster;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension (Objective.Data data)
    {
        public RegionID Region_FoundGenCluster
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Found Gen Cluster", data => new("Region entered when a central gen cluster is found", data.Region_Objective));

        public RegionID Region_PoweredCentralGenerator(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Powered Generator #{count}", data => new("Region entered when a central generator is powered", data.Region_Objective));

        public RegionID Region_CompletedCentralGenScan
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Completed Scan", data => new("Region entered when a central gen cluster's scan is completed", data.Region_Objective));


        public LocationID Location_CentralGenCells_ByObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Central Gen Cell Locations", data => new("Locations for cells for a particular objective", data.Location_CentralGenCells));


        public LocationID Location_CentralGenCell_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Central Gen Cell Location #{count}", data => new("Location of a particular central gen cell", data.Location_CentralGenCells_ByObjective));

        public LocationID Location_CentralGenCluster_Instance
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Central Gen Cluster Location", data => new("The location of a particular central gen cluster", data.Location_CentralGenClusters));

        public ItemID Item_CentralGenCluster_Instance
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Central Gen Cluster", 
                data => new("A particular central gen cluster", data.Item_CentralGenClusters),
                new CentralGenClusterHandler.GenCluster_ClusterItem(data.Region_Objective)
            );

        public LocationID Location_CentralGenScan_Instance
            => LocationID.From(data, $"{data.ObjectiveName} Central Gen Scan Location", data => new("A particular gen cluster scan's location", data.Location_CentralGenScans));

        public ItemID Item_CentralGenScan_Instance
            => ItemID.From(
                data, 
                $"{data.ObjectiveName} Central Gen Scan Items", 
                data => new("A particular gen cluster scan", data.Item_CentralGenScans),
                new CentralGenClusterHandler.GenCluster_ScanItem(data.Region_Objective)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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

    /// <summary>
    /// A gen cluster item - ie, the actual gen cluster itself
    /// </summary>
    public class GenCluster_ClusterItem : Item
    {
        public GenCluster_ClusterItem(RegionID objective)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
        }
        
        public RegionID ObjectiveRegion { get; private init; }
    }

    /// <summary>
    /// Item which represents / triggers the gen cluster scan
    /// </summary>
    public class GenCluster_ScanItem : TerminalItem
    {
        public GenCluster_ScanItem(RegionID objective)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public override RegionID TargetRegion => ObjectiveRegion;

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            Objective.Data data = new(stateTracker.GameData, ObjectiveRegion);

            var item = WardenObjectiveManager.GetObjectiveItemCollection(data.LayerType, data.ObjectiveIndex);
            LG_PowerGeneratorCluster? cluster = item[0].TryCast<LG_PowerGeneratorCluster>();

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Initiating Gen Cluster Scan", 2f);
            };

            yield return () =>
            {
                if (cluster != null)
                {
                    terminal.AddLine($"Scan will start in 3 seconds. Enjoy :)");
                    cluster.StartCoroutine(cluster.ObjectiveEndSequence());
                    cluster.SetFogIndex(cluster.m_currentFogStepIndex + 1);
                }
                else
                {
                    terminal.AddLine($"<#F00>Failed to find generator cluster! Item returned to terminal.</color>");
                    stateTracker.AddItemToTerminal(itemId);
                }
            };
        }
    }

    // Objective requiring one or more cells be found in the map and used to power a central generator cluster
    [Objective.Callback]
    public void HandleCentralGenGlusterObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.CentralGeneratorCluster)
            return;

        // a) Placing cells in the map
        ItemID cellItem = data.Item_BigPickup_Cell;
        List<List<RegionID>> regionSets = data.PlacementsToZoneRegions(data.ObjectiveData.ZonePlacementDatas)
            .Select(ps => ps.Select(i => i.Region).ToList())
            .TakeLooped(data.Objective.CentralPowerGenClustser_NumberOfPowerCells)
            .ToList();

        for (int i = 1; i <= data.Objective.CentralPowerGenClustser_NumberOfPowerCells; i++)
        {
            data.Locations.CreateValue(
                data.Location_CentralGenCell_Instance(i),
                regionSets[i - 1],
                new LocationData(),
                cellItem
            );
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
            FeatureLogger.Warning($"Failed to find gen cluster for objective: {data.ObjectiveName}");
            clusterZone = data.FirstZone;
        }

        ItemID clusterItem = data.Item_CentralGenCluster_Instance;
        data.Locations.CreateValue(
            data.Location_CentralGenCluster_Instance, 
            clusterZone.Region_Zone,
            new LocationData() { IsAutoDiscovered = true },
            clusterItem
        );

        // This region represents having found the gen cluster
        RegionID foundGenClusterRegion = data.Region_FoundGenCluster;
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Objective,
            EndingRegion = foundGenClusterRegion,
            ReqItem = new(Path.PathReq.eType.Item, clusterItem),
            ReqCount = 1u,
        });

        // c) Powering gens with found cells
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        RegionID last = foundGenClusterRegion;
        for (int i = 1; i <= data.Objective.CentralPowerGenClustser_NumberOfGenerators; i++)
        {
            RegionID newRegion = data.Region_PoweredCentralGenerator(i);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = newRegion,
                ReqItem = new(Path.PathReq.eType.ItemConsumed, cellItem),
                ReqCount = 1u,
            });
            last = newRegion;
            eventWrapper.Process(newRegion, true);
        }

        // d) Scan at the end of the objective
        ItemID scanItem = data.Item_CentralGenScan_Instance;
        data.Locations.CreateValue(
            data.Location_CentralGenScan_Instance,
            last,
            new LocationData(),
            scanItem
        );

        RegionID scanRegion = data.Region_CompletedCentralGenScan;
        data.AddPath(new Path()
        {
            StartingRegion = last,
            EndingRegion = scanRegion,
            ReqItem = new(Path.PathReq.eType.Item, scanItem),
            ReqCount = 1u,
        });

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, scanRegion);
    }

    /// <summary>
    /// Notify that the sequence is ended and cancel the ending if necessary
    /// </summary>
    [ArchivePatch(typeof(LG_PowerGeneratorCluster), nameof(LG_PowerGeneratorCluster._Setup_b__15_1))]
    public static class LG_PowerGeneratorCluster___Setup_b__15_1__Patch
    {
        public static bool Prefix(LG_PowerGeneratorCluster __instance)
        {
            Objective.Data data = Layer.Data.GetFromLayer(__instance.SpawnNode.m_zone.Layer)
                .GetObjectiveDatas().ElementAt(__instance.WardenObjectiveChainIndex);

            if (__instance.m_currentFogStepIndex == (data.Objective.CentralPowerGenClustser_NumberOfGenerators - 2))
            {
                Location loc = StateTracker.Get().NotifyFoundLocation(data.Location_CentralGenScan_Instance, null);
                if (loc.RandData.IsTreatedAsRandom)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Brief explanation:
    /// Objective items (including our cells) are spawned by the LG_Distribute_WardenObjective job.
    /// This job calls BuildWardenObjective, which then calls DistributePickupItems.
    /// DistributePickupItems calls TryGetValidPlacementZonesFromPlacementData to first get placements, then 
    ///  SelectZoneFromPlacementAndKeepTrackOnCount once per distribution item to actually use those placements.
    /// Passed to SelectZoneFromPlacementAndKeepTrackOnCount is a lambda to actually use the placement; this is the lamdba we are targetting below.
    /// The lambda uses the selected zone to create and queue a new LG_Distribute_PickupItemsPerZone job, which then later places
    ///  the objective item in the relevant FunctionMarker builder job and yada yada.
    /// For our use case, when this function is done, a brand new LG_Distribute_PickupItemsPerZone job is sitting on the queue, so we'll go claim it :)
    /// </summary>
    [ArchivePatch(typeof(LG_Distribute_WardenObjective.__c__DisplayClass8_1), nameof(LG_Distribute_WardenObjective.__c__DisplayClass8_1._DistributePickupItems_b__0))]
    public static class LG_Distribute_WardenObjective____c__DisplayClass8_1___DistributePickupItems_b__0__Patch
    {
        public static void Postfix(LG_Distribute_WardenObjective.__c__DisplayClass8_1 __instance, LG_Zone zone)
        {
            Objective.Data data = Layer.Data.GetFromLayerFlattened(zone.Layer)
                .GetObjectiveDatas().ElementAt(__instance.field_Public___c__DisplayClass8_0_0.chainIndex);
            if (data.Objective.Type != eWardenObjectiveType.CentralGeneratorCluster) return;

            PickupHelper.AssociateDistributionWithLocation(
                LG_Factory.Current.m_currentBatch.Jobs.FromEnd().Cast<LG_Distribute_PickupItemsPerZone>(),
                data.Location_CentralGenCell_Instance(__instance.i + 1)
            );
        }
    }

}
