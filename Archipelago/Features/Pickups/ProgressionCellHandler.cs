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
        public TagResolver Tag_ProgressionCellSpawnLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Progression Cell Locations", "Locations checked by picking up cells specifically spawned by the game's progression puzzle system.", gd.Tag_BigPickupLocations));
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

    private static class ProgressionCellLocation
    {
        public static TagResolver MakeTag(Zone.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ZoneName} Progression Cell #{count} Spawn", "Progression cell spawn location", gd.Tag_ProgressionCellSpawnLocations));

        public static LocationData MakeRandData() => new LocationData();
    }

    [Zone.Callback]
    public void AddProgressionCells(Zone.Data data)
    {
        if ((data.Zone?.ProgressionPuzzleToEnter.PuzzleType ?? eProgressionPuzzleType.None) != eProgressionPuzzleType.PowerGenerator_And_PowerCell)
            return;

        var cellItem = BigPickupHandler.GetBigPickupItem(data, BigPickupHandler.CellItemID);
        var placement = data.PlacementsToZoneRegions(data.Zone!.ProgressionPuzzleToEnter.ZonePlacementData).Select(i => i.Region).Distinct().ToArray();
        for (int count = 1; count <= data.Zone.ProgressionPuzzleToEnter.PlacementCount; count++)
        {
            data.AddLocation(
                ProgressionCellLocation.MakeTag(data, count),
                placement,
                ProgressionCellLocation.MakeRandData(),
                cellItem.ID
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
            Layer.Data layerData = Layer.Data.FromLayer(__instance.m_layer);

            // Not sure why, but dimensions with no layout can "inherit" zone datas - this then causes problems
            if (layerData.LayoutID == 0)
                return;

            var queuedCells = __instance.m_layer.m_buildData.m_zoneBuildDatas
                .Where(z => z.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.PowerGenerator_And_PowerCell)
                .Select(z => layerData.FindZoneByIndex(z.LocalIndex))
                .SelectMany(z => Enumerable.Range(1, z.Zone!.ProgressionPuzzleToEnter.PlacementCount).Select(i => Tuple.Create(z, i)))
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
                    if (!layerData.TryLookupLocation(ProgressionCellLocation.MakeTag(queuedCells.Current.Item1, queuedCells.Current.Item2), out var loc))
                    {
                        FeatureLogger.Error($"Failed to create association for progression cell #{queuedCells.Current.Item2} in zone: {queuedCells.Current.Item1.ZoneName}");
                        continue;
                    }
                    PickupHelper.AssociateDistributionWithLocation(dist, loc.ID);
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
