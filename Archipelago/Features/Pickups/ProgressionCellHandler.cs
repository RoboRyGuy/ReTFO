using GameData;
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Pickups;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class ProgressionCellHandler_Tags
{
    extension (Game.Data gameData)
    {
        /// <summary>
        /// Parent tag of all progression cell locations
        /// </summary>
        public LocationID Location_ProgressionCellSpawns
            => LocationID.From(gameData, "Progression Cell Locations", data => new("Locations checked by picking up cells specifically spawned by the game's progression puzzle system", data.Location_BigPickups));
    }

    extension (Zone.Data data)
    {
        /// <summary>
        /// Parent tag for progression cell locations for a particular zone
        /// </summary>
        public LocationID Location_ProgressionCellSpawns_ByZone
            => LocationID.From(data, $"{data.ZoneName} Progression Cell Locations", data => new("Progresion cell locations spawned for a specific zone's progression puzzle", data.Location_ProgressionCellSpawns));

        /// <summary>
        /// A specific progression cell location
        /// </summary>
        /// <param name="count">1-indexed count of the location within the list of spawns in for the zone</param>
        public LocationID Location_ProgressionCellSpawn_Instance(int count)
            => LocationID.From(data, $"{data.ZoneName} Progression Cell Location #{count}", data => new("A particular progression cell spawn location", data.Location_ProgressionCellSpawns_ByZone));
    }
}

// Small handler specifically for identifying cells spawned for progression puzzles
[EnableFeatureByDefault, AutomatedFeature]
public class ProgressionCellHandler : ArchipelagoFeature
{
    public override string Name => "Progression Cell Handler";
    public override string Description 
        => "Handles specifically cells spawned as part of progression puzzles\n"
         + "Progression puzzles are blockers for entering a zone. In this case, any zone that "
         + "requires a specific generator to be powered to enter";
    public override FeatureGroup Group => FeatureGroups.PickupHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    [Zone.Callback]
    public void AddProgressionCells(Zone.Data data)
    {
        if ((data.Zone?.ProgressionPuzzleToEnter.PuzzleType ?? eProgressionPuzzleType.None) != eProgressionPuzzleType.PowerGenerator_And_PowerCell)
            return;

        ItemID cellItem = data.Item_BigPickup_Cell;
        var placement = data.PlacementsToZoneRegions(data.Zone!.ProgressionPuzzleToEnter.ZonePlacementData).Select(i => i.Region).Distinct().ToArray();
        for (int count = 1; count <= data.Zone.ProgressionPuzzleToEnter.PlacementCount; count++)
        {
            data.Locations.CreateValue(
                data.Location_ProgressionCellSpawn_Instance(count),
                placement,
                new LocationData(),
                cellItem
            );
        }
    }

    // Catch progression cells after they're placed and associate them with their location ID
    [ArchivePatch(typeof(LG_Distribute_ProgressionPuzzles), nameof(LG_Distribute_ProgressionPuzzles.Build))]
    public static class LG_Distribute_ProgressionPuzzles__Build__Patch
    {
        public static void Postfix(LG_Distribute_ProgressionPuzzles __instance)
        {
            var queuedJobs = LG_Factory.Current.m_batches[(int)LG_Factory.BatchName.Distribution].Jobs;
            Layer.Data layerData = Layer.Data.GetFromLayer(__instance.m_layer);

            // Not sure why, but dimensions with no layout can "inherit" zone datas - this then causes problems
            if (layerData.LayoutID == 0)
                return;

            var queuedCells = __instance.m_layer.m_buildData.m_zoneBuildDatas
                .Where(z => z.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.PowerGenerator_And_PowerCell)
                .Select(z => layerData.FindZoneByIndex(z.LocalIndex))
                .SelectMany(z => Enumerable.Range(1, z.Zone!.ProgressionPuzzleToEnter.PlacementCount).Select(i => (Zone: z, Count: i)))
                .GetEnumerator();

            foreach (var job in queuedJobs)
            {
                LG_Distribute_PickupItemsPerZone? dist = job.TryCast<LG_Distribute_PickupItemsPerZone>();
                if (dist == null) continue;
                if (dist.m_zone.m_layer.Pointer != __instance.m_layer.Pointer) continue; // Wrong layer!
                if (dist.m_pickupType != ePickupItemType.BigGenericPickup) continue;
                if (dist.m_bigPickupDistributionData != null) continue; // Placed by big pickup distirbutions during static zone creation

                // We expect this distribution to be a progression cell
                if (dist.m_genericItemId != BigPickupHandler.CellItemID)
                    FeatureLogger.Error("Expected distribution to be a cell, but it wasn't!");
                else if (queuedCells.MoveNext())
                {
                    var pair = queuedCells.Current;
                    PickupHelper.AssociateDistributionWithLocation(dist, pair.Zone.Location_ProgressionCellSpawn_Instance(pair.Count));
                }
                else
                    FeatureLogger.Error("Had more cells than progression puzzle cells!");
            }

            // Some extra error checking
            if (queuedCells.MoveNext())
                FeatureLogger.Error($"Not all cell locations associated during layer build: {layerData.LayerName}");
        }
    }

}
