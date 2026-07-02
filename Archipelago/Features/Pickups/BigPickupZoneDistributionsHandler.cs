using GameData;
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Pickups;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class BigPickupZoneDistributionsHandler_Tags
{
    extension (Game.Data gameData)
    {
        /// <summary>
        /// Parent tag for all big pickup zone distributions
        /// </summary>
        public LocationID Location_BigPickupZoneDistributions
            => LocationID.From(gameData, "Big Pickup Zone Distribution Locations", data => new("Locations created by misc big pickup items spawned inside a specific zone", data.Location_BigPickups));
    }

    extension (Zone.Data data)
    {
        /// <summary>
        /// A particular zone pickup distribution
        /// </summary>
        /// <param name="count">1-indexed position of this spawn in the "list" of spawns</param>
        public LocationID Location_BigPickupZoneDistribution_Instance(int count)
            => LocationID.From(
                data, 
                $"{data.ZoneName} Big Pickup #{count}", 
                data => new("A big pickup location spawned randomly in a zone", data.Location_BigPickupZoneDistributions)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class BigPickupZoneDistributionsHandler : ArchipelagoFeature
{
    public override string Name => "Big Pickups Zone Distributions";
    public override string Description
        => "Handles items spawned via Big Pickup Zone Distributions.\n"
        + "This is the system which spawns most big pickups. The other two major big pickup spawning "
        + "systems are Warden Objectives (which can spawn big pickups based on the objective type) "
        + "and Cells spawned as part of Zone Progression Puzzles";
    public override FeatureGroup Group => FeatureGroups.PickupHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    //private static class BigPickupSpecificDistributionLocation
    //{
    //    public static TagResolver MakeTag(Zone.Data data, int count)
    //        => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ZoneName} Specific Big Pickup #{count}", "A big pickup location spawned in on a specific align", gd.Tag_BigPickupZoneDistributionLocation));
    //
    //    public static RandomizationData MakeRandData() => new RandomizationData() { IsProgression = true };
    //}

    // Add big pickups in zones which spawn them via big pickup distributions
    [Zone.Callback]
    public void AddBigPickups(Zone.Data data)
    {
        int count = 0;
        uint id = data.Zone?.BigPickupDistributionInZone ?? data.DimensionData?.StaticBigPickupDistributionInZone ?? 0u;
        BigPickupDistributionDataBlock pickups = BigPickupDistributionDataBlock.GetBlock(id);
        if (pickups != null)
        {
            // Big pickup distributions are handled weirdly. Below is a guess as to how it's handled (including for non-1 weights)
            float usedWeight = 0f;
            int index = 0;
            while ((usedWeight + pickups.SpawnData[index].Weight) <= pickups.SpawnsPerZone)
            {
                ++count;
                data.Locations.CreateValue(
                    data.Location_BigPickupZoneDistribution_Instance(count),
                    data.Region_Zone,
                    new LocationData(),
                    data.Item_BigPickup_Instance(pickups.SpawnData[index].ItemID)
                );
                usedWeight += pickups.SpawnData[index].Weight;
                index = (index + 1) % pickups.SpawnData.Count;
            }
        }

        // Specific big pickup spawns. Technically, these could (probably) include small pickups.. Problem for future me :)
        // Of note, the only vanilla example I found for this was in R7D1, where it's used to spawn the neonate granted after finishing the timed sequence
        var specificPickups = data.Zone?.SpecificPickupSpawningDatas;
        if (specificPickups == null) return;
        foreach (var item in specificPickups)
        {
            if (item.WorldEventObjectFilter == null) continue; // R8C1 has some null data for some reason
            else if (item.PickupToSpawn == 0u) continue; // R8C1 has some null data for some reason

            ++count;
            data.Locations.CreateValue(
                data.Location_BigPickupZoneDistribution_Instance(count),
                data.Region_Zone,
                new LocationData(),
                data.Item_BigPickup_Instance(item.PickupToSpawn)
            );
        }
    }

    // Prior to building markers for big pickups, find all pickups associated with zone build data and create associations
    [ArchivePatch(typeof(LG_PopulateFunctionMarkersInZoneJob), nameof(LG_PopulateFunctionMarkersInZoneJob.BuildPickupItems))]
    public static class LG_PopulateFunctionMarkersInZoneJob__BuildPickupItems__Patch
    {
        public static void Prefix(LG_PopulateFunctionMarkersInZoneJob __instance)
        {
            // This build function will be run once for every item in the pickups queue, removing one item at a time
            // We only want to update the big pickup id when all big pickups are in the queue; therefore, only if no items have been dequeued
            var queue = __instance.m_distributionData.PickupItems.m_itemQueue;
            if (queue._head != 0 || queue.Count == 0)
                return;

            Zone.Data zone = Zone.Data.GetFromZone(__instance.m_zone);
            int count = 0;
            foreach (var item in __instance.m_distributionData.PickupItems.m_itemQueue.Iter())
            {
                if (item.m_type != ePickupItemType.BigGenericPickup) continue;
                if (item.m_function != ExpeditionFunction.BigPickupItem) continue;
                if (item.m_bigPickupData == null) continue;

                LocationID id = zone.Location_BigPickupZoneDistribution_Instance(++count);
                PickupHelper.AssociateDistributionWithLocation(item, id);
            }
        }
    }

}
