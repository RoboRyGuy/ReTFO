using GameData;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.Pickups;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class BulkheadKeyHandler_Tags
{
    extension (Game.Data gameData)
    {
        public TagResolver Tag_BulkheadKeyLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Bulkhead Key Locations", "Locations checked by picking up bulkhead keys", gd.Tag_SmallPickupLocations));

        public TagResolver Tag_BulkheadKeyItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Bulkhead Key Items", "The bulkhead key item itself", gd.Tag_SmallPickupItems));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class BulkheadKeyHandler : ArchipelagoFeature
{
    public override string Name => "Bulkhead Key Handler";
    public override string Description => "Handles processing, despawning, and spawning bulkhead keys as part of randomization";
    public override FeatureGroup Group => FeatureGroups.PickupHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// Location class representing a bulkhead key spawn
    /// </summary>
    private static class BulkheadKeyLocation
    {
        public static TagResolver MakeTag(Layer.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.LayerName} Bulkhead Key Spawn #{count}", "A bulkhead key spawn location", gd.Tag_BulkheadKeyLocations));

        public static LocationData MakeRandData() => new LocationData();
    }

    /// <summary>
    /// Item class representing a bulkhead key for a particular expedition
    /// </summary>
    private class BulkheadKeyItem : Item
    {
        public BulkheadKeyItem(Expedition.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ExpeditionData = data;
        }

        public static TagResolver MakeTag(Expedition.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Bulkhead Key Item", "A bulkhead key for a particular expedition", gd.Tag_BulkheadKeyItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true, IsRandomLike = true };

        public Expedition.Data ExpeditionData { get; set; }

        // Not sure how to check this at runtime except maybe by name? Not worth it
        const uint BULKHEAD_KEY_ID = 146u;

        private AsyncItemSpawnWrapper SpawnItemAsync()
        {
            AsyncItemSpawnWrapper wrapper = new();
            ItemReplicationManager.SpawnItem(
                new pItemData() { itemID_gearCRC = BULKHEAD_KEY_ID },
                new Action<ISyncedItem, PlayerAgent>(wrapper.OnSpawn),
                ItemMode.Pickup,
                Vector3.zero,
                Quaternion.identity,
                null,
                null
            );
            return wrapper;
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player = null)
        {
            // If the bulkhead key is not randomized, we want to try and give it directly to the player who found it
            if (ExpeditionData.IsCurrentlyInExpedition())
            {
                if (!stateTracker.TestRandomization(this, true).IsRandomized && player != null)
                {
                    var wrapper = SpawnItemAsync();
                    wrapper.OnItemSpawned += (item, _) => 
                    {
                        KeyItemPickup_Core keyItem = item.Cast<KeyItemPickup_Core>();
                        keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player.Owner);
                    };
                    return;
                }
                stateTracker.AddItemToTerminal(this);
            }
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ExpeditionData.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            // Isolate these so the lambda can capture them
            var wrapper = SpawnItemAsync();
            var player = terminal.m_localInteractionSource.Owner;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving bulkhead key", 2f);
            };

            yield return () =>
            {
                KeyItemPickup_Core? keyItem = wrapper.Item?.TryCast<KeyItemPickup_Core>();
                if (keyItem == null)
                {
                    stateTracker.AddItemToTerminal(this);
                    wrapper.QueueDespawn();
                    FeatureLogger.Error("Failed to spawn key item while spawning bulkhead keycard!");
                    terminal.AddLine("<#F00>Failed to retrieve key! It has been re-added to terminal system.</color>");
                    return;
                }

                keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player);
                terminal.AddLine($"Key \"{keyItem.PublicName}\" has been given to {player.NickName}");
            };
        }
    }

    /// <summary>
    /// Get a bulkhead key item for the provided expedition
    /// </summary>
    /// <param name="data">The expedition to get the key for</param>
    /// <returns>The shared bulkhead key item</returns>
    public static KeyedItem GetBulkheadKeyItem(Expedition.Data data)
    {
        if (data.TryLookupItem(BulkheadKeyItem.MakeTag(data), out var item))
            return item;

        Item newItem = new BulkheadKeyItem(data);
        return new KeyedItem(data.AddItem(newItem), newItem);
    }

    // Add bulkhead keys from layer data
    [Layer.Callback]
    public void AddBulkheadKeys(Layer.Data data)
    {
        if (data.LayerDatas == null) return;

        KeyedItem item = GetBulkheadKeyItem(data);
        for (int i = 0; i < data.LayerDatas.BulkheadKeyPlacements.Count; i++)
        {
            // R7C3 Main and R5C1 secondary both have an empty placement (for some reason)
            if (!data.LayerDatas.BulkheadKeyPlacements[i].Any())
                continue;

            data.AddLocation(
                BulkheadKeyLocation.MakeTag(data, i + 1),
                data.PlacementsToZoneRegions(data.LayerDatas.BulkheadKeyPlacements[i]).Select(info => info.Region).Distinct().ToArray(),
                BulkheadKeyLocation.MakeRandData(),
                item.ID
            );
        }
    }

    /// <summary>
    /// Identifies and associates bulkhead keys post-spawn
    /// </summary>
    [ArchivePatch(typeof(LG_Distribute_ProgressionPuzzles), nameof(LG_Distribute_ProgressionPuzzles.CreateKeyItemDistribution))]
    public static class LG_Distribute_ProgressionPuzzles__CreateKeyItemDistribution__Patch
    {
        public static void Postfix(LG_Distribute_ProgressionPuzzles __instance, GateKeyItem keyItem, ZonePlacementData placementData)
        {
            // I kinda have no good option other than to brute force this...
            // Basically: If the placemnet for this item is the same (by reference) as one in the bulkhead keys placement list, it's a bulkhead key
            if (__instance.m_layer.m_buildData.m_layerGameData == null) return;
            Layer.Data layerData = Layer.Data.FromLayer(__instance.m_layer);
            for (int i = 0; i < __instance.m_layer.m_buildData.m_layerGameData.BulkheadKeyPlacements.Count; i++)
            {
                if (__instance.m_layer.m_buildData.m_layerGameData.BulkheadKeyPlacements[i].Any(p => p.Pointer == placementData.Pointer))
                {
                    if (layerData.TryLookupLocation(BulkheadKeyLocation.MakeTag(layerData, i + 1), out var loc))
                        PickupHelper.AssociateItem(keyItem.keyPickupCore, loc.ID);
                    else
                        FeatureLogger.Error($"Failed to create association for bulkhead key location in layer: {layerData.LayerName}");
                    return;
                }
            }

            // Extra check, in case we've made some kind of error
            if (keyItem.PublicName.Contains("bulkhead", StringComparison.OrdinalIgnoreCase))
                FeatureLogger.Error("Failed to identify placement for bulkhead key!");
        }
    }

}
