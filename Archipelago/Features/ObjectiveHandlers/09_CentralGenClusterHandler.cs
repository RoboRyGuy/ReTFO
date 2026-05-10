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
        public TagResolver Tag_CentralGenCellLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Central Gen Cell Locations", "Locations checked by picking up cells spawned for central gen cluster objectives", gd.Tag_BigPickupLocations));

        public TagResolver Tag_CentralGenClusterLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Central Gen Cluster Locations", "Locations checked by finding central gen clusters", gd.Tag_Never));

        public TagResolver Tag_CentralGenClusterItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Central Gen Cluster Items", "Items representing central gen clusters", gd.Tag_Never));

        public TagResolver Tag_CentralGenScanLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Central Gen Scan Locations", "Locations checked by fully powering a central gen cluster", gd.Tag_AllLocations));

        public TagResolver Tag_CentralGenScanItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Central Gen Scan Items", "Items which start the final gen cluster scan (which normally occurs when it's fully powered)", gd.Tag_ScanItems));
    }

    extension (Objective.Data data)
    {
        public TagResolver Tag_CentralGenCellLocations_ByObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Central Gen Cell Locations", "Locations for cells for a particular objective", gd.Tag_CentralGenCellLocations));
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
    /// A location containing a cell spawned for a central gen cluster
    /// </summary>
    private static class GenCluster_CellLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Central Gen Cell Location #{count}", "A particular cell spawn location", data.Tag_CentralGenCellLocations_ByObjective));

        public static LocationData MakeRandData() => new LocationData() { };
    }

    /// <summary>
    /// A location containing a gen cluster
    /// </summary>
    private static class GenCluster_ClusterLocation
    {
        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Central Gen Cluster Location", "A particular gen cluster spawn location", gd.Tag_CentralGenClusterLocations));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    /// <summary>
    /// A gen cluster item - ie, the actual gen cluster itself
    /// </summary>
    private class GenCluster_ClusterItem : Item
    {
        public GenCluster_ClusterItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Central Gen Cluster Item", "A particular gen cluster", gd.Tag_CentralGenClusterItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }
    }

    /// <summary>
    /// Location containing the scan that occurs when all cells are inserted
    /// </summary>
    private static class GenCluster_ScanLocation
    {
        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Gen Cluster Scan Location", "A particular scan location", gd.Tag_CentralGenScanLocations));

        public static LocationData MakeRandData() => new LocationData();
    }

    /// <summary>
    /// Item which represents / triggers the gen cluster scan
    /// </summary>
    private class GenCluster_ScanItem : Item
    {
        public GenCluster_ScanItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Gen Cluster Scan Item", "A particular scan item", gd.Tag_CentralGenScanItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData;

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (ObjectiveData.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ObjectiveData.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            var item = WardenObjectiveManager.GetObjectiveItemCollection(ObjectiveData.LayerType, ObjectiveData.ObjectiveIndex);
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
                    stateTracker.AddItemToTerminal(this);
                }
            };
        }
    }

    public static KeyedItem GetClusterItem(Objective.Data data)
    {
        if (data.TryLookupItem(GenCluster_ClusterItem.MakeTag(data), out var item))
            return item;

        Item newItem = new GenCluster_ClusterItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    public static KeyedItem GetScanItem(Objective.Data data)
    {
        if (data.TryLookupItem(GenCluster_ScanItem.MakeTag(data), out var item))
            return item;

        Item newItem = new GenCluster_ScanItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.CentralGeneratorCluster;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return $"{data.Objective.CentralPowerGenClustser_NumberOfGenerators}x Central Gen Cluster";
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

    // Region names for this objective
    private static class ThisRegions
    {
        // Region reached after finding the gen cluster
        public static string FoundGenCluster(Objective.Data data)
            => $"{This.ObjectiveName(data)} Found Gen Cluster";

        // Region reached after powering a generator
        public static string PoweredGenerator(Objective.Data data, int count)
            => $"{This.ObjectiveName(data)} Powered Generator #{count}";

        // Region reached after completing the final scan
        public static string CompletedScan(Objective.Data data)
            => $"{This.ObjectiveName(data)} Completed Scan";
    }

    // Objective requiring one or more cells be found in the map and used to power a central generator cluster
    [Objective.Callback]
    public void HandleCentralGenGlusterObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // a) Placing cells in the map
        KeyedItem cellItem = BigPickupHandler.GetBigPickupItem(data, BigPickupHandler.CellItemID);
        List<List<RegionID>> regionSets = data.PlacementsToZoneRegions(data.ObjectiveData.ZonePlacementDatas)
            .Select(ps => ps.Select(i => i.Region).ToList())
            .TakeLooped(data.Objective.CentralPowerGenClustser_NumberOfPowerCells)
            .ToList();
        for (int i = 1; i <= data.Objective.CentralPowerGenClustser_NumberOfPowerCells; i++)
        {
            data.AddLocation(
                GenCluster_CellLocation.MakeTag(data, i),
                regionSets[i - 1],
                GenCluster_CellLocation.MakeRandData(),
                cellItem.ID
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
            FeatureLogger.Warning($"Failed to find gen cluster for objective: {This.ObjectiveName(data)}");
            clusterZone = data.FirstZone;
        }

        KeyedItem clusterItem = GetClusterItem(data);
        data.AddLocation(
            GenCluster_ClusterLocation.MakeTag(data),
            data.LookupOrCreateRegion(clusterZone.ZoneName),
            GenCluster_ClusterLocation.MakeRandData(),
            clusterItem.ID
        );

        // This region represents having found the gen cluster
        RegionID foundGenClusterRegion = data.LookupOrCreateRegion(ThisRegions.FoundGenCluster(data));
        data.AddPath(new Path()
        {
            StartingRegion = data.ObjectiveStartRegion,
            EndingRegion = foundGenClusterRegion,
            ReqItem = clusterItem.PathReqs,
            ReqCount = 1u,
        });

        // c) Powering gens with found cells
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        RegionID last = foundGenClusterRegion;
        for (int i = 1; i <= data.Objective.CentralPowerGenClustser_NumberOfGenerators; i++)
        {
            string newRegionName = ThisRegions.PoweredGenerator(data, i);
            RegionID newRegion = data.LookupOrCreateRegion(newRegionName);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = newRegion,
                ReqItem = cellItem.PathReqs,
                ReqCount = 1u,
            });
            last = newRegion;
            eventWrapper.Process(newRegion, newRegionName, true);
        }

        // d) Scan at the end of the objective
        KeyedItem scanItem = GetScanItem(data);
        data.AddLocation(
            GenCluster_ScanLocation.MakeTag(data),
            last,
            GenCluster_ScanLocation.MakeRandData(),
            scanItem.ID
        );

        string scanRegionName = ThisRegions.CompletedScan(data);
        RegionID scanRegion = data.LookupOrCreateRegion(scanRegionName);
        data.AddPath(new Path()
        {
            StartingRegion = last,
            EndingRegion = scanRegion,
            ReqItem = scanItem.PathReqs,
            ReqCount = 1u,
        });

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, scanRegion);
    }

    /// <summary>
    /// The m_endSequenceTriggered flag isn't used in vanilla, so we implement it here.
    /// We also notify that the scan location was found, regardless.
    /// </summary>
    [ArchivePatch(typeof(LG_PowerGeneratorCluster), nameof(LG_PowerGeneratorCluster._Setup_b__15_1))]
    public static class LG_PowerGeneratorCluster___Setup_b__15_1__Patch
    {
        public static bool Prefix(LG_PowerGeneratorCluster __instance)
        {
            if (__instance.m_currentFogStepIndex == (__instance.m_fogDataSteps.Count - 2))
            {
                Objective.Data data = Layer.Data.FromLayer(__instance.SpawnNode.m_zone.Layer)
                    .GetObjectiveDatas().ElementAt(__instance.WardenObjectiveChainIndex);

                if (data.TryLookupLocation(GenCluster_ScanLocation.MakeTag(data), out var loc))
                {
                    if (StateTracker.Get().NotifyFoundLocation(loc.ID, null).RandMode.IsTreatedAsRandom)
                        return false;
                }
                else
                    FeatureLogger.Error("Failed to notify finding of gen cluster scan location!");
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
    /// The lambda uses the selected zone to create and queue a new LG_Distribute_PickupItemsPerZone job, which then laters places
    ///  the objective item in the relevant FunctionMarker builder job and yada yada.
    /// For our use case, when this function is done, a brand new LG_Distribute_PickupItemsPerZone job is sitting on the queue, so we'll go claim it :)
    /// </summary>
    [ArchivePatch(typeof(LG_Distribute_WardenObjective.__c__DisplayClass8_1), nameof(LG_Distribute_WardenObjective.__c__DisplayClass8_1._DistributePickupItems_b__0))]
    public static class LG_Distribute_WardenObjective____c__DisplayClass8_1___DistributePickupItems_b__0__Patch
    {
        public static void Postfix(LG_Distribute_WardenObjective.__c__DisplayClass8_1 __instance, LG_Zone zone)
        {
            Objective.Data data = Layer.Data.FromLayerFlattened(zone.Layer).GetObjectiveDatas().ElementAt(__instance.field_Public___c__DisplayClass8_0_0.chainIndex);
            if (data.Objective.Type != This.ObjectiveType) return;

            if (data.TryLookupLocation(GenCluster_CellLocation.MakeTag(data, __instance.i + 1), out var loc))
            {
                PickupHelper.AssociateDistributionWithLocation(
                    LG_Factory.Current.m_currentBatch.Jobs.FromEnd().Cast<LG_Distribute_PickupItemsPerZone>(),
                    loc.ID
                );
            }
            else
                FeatureLogger.Error("Failed to create association for Central Gen Cluster spawned cell!");
        }
    }

}
