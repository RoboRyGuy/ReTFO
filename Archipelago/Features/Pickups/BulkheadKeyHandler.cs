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
        /// <summary>
        /// Parent of all bulkhead key locations
        /// </summary>
        public LocationID Location_BulkheadKeys
            => LocationID.From(gameData, "Bulkhead Key Locations", data => new("Locations checked by picking up bulkhead keys", data.Location_SmallPickups));

        /// <summary>
        /// Parent of all bulkhead key items
        /// </summary>
        public ItemID Item_BulkheadKeys
            => ItemID.From(gameData, "Bulkhead Key Items", data => new("The bulkhead key item itself", data.Item_SmallPickups));
    }

    extension (Expedition.Data data)
    {
        /// <summary>
        /// Bulkhead key for a particular expedition
        /// </summary>
        public ItemID Item_BulkheadKey_Instance
            => ItemID.From(
                data,
                $"{data.ExpeditionName} Bulkhead Key",
                data => new("The bulkhead key for a particular expedition", data.Item_BulkheadKeys),
                new BulkheadKeyHandler.BulkheadKeyItem(data.Region_Expedition)
            );
    }

    extension (Layer.Data data)
    {
        /// <summary>
        /// Parent tag of bulkhead key locations in a layer
        /// </summary>
        public LocationID Location_BulkheadKey_ByLayer
            => LocationID.From(data, $"{data.LayerName} Bulkhead Key Locations", data => new("Bulkhead key locations in a particular layer of a particular expedition", data.Location_BulkheadKeys));

        /// <summary>
        /// A particular bulkhead key spawn location
        /// </summary>
        /// <param name="count">1-indexed count of which spawn this is for the layer</param>
        public LocationID Location_BulkheadKey_Instance(int count)
            => LocationID.From(
                data, 
                $"{data.LayerName} Bulkhead Key Location #{count}", 
                data => new("A particular bulkhead key spawn location", data.Location_BulkheadKey_ByLayer)
            );
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
    /// Item class representing a bulkhead key for a particular expedition
    /// </summary>
    public class BulkheadKeyItem : TerminalItem
    {
        public BulkheadKeyItem(RegionID expedition)
            : base(new ItemData() { IsProgression = true })
        {
            ExpeditionRegion = expedition;
        }

        public RegionID ExpeditionRegion { get; private init; }

        public override RegionID TargetRegion => ExpeditionRegion;

        /// <summary>
        /// Attempt to spawn the bulkhead key item
        /// </summary>
        /// <returns></returns>
        private AsyncItemSpawnWrapper SpawnItemAsync()
        {
            AsyncItemSpawnWrapper wrapper = new();
            if (SNetwork.SNet.IsMaster)
            {
                ItemReplicationManager.SpawnItem(
                    new pItemData() { itemID_gearCRC = BULKHEAD_KEY_ID },
                    new Action<ISyncedItem, PlayerAgent>(wrapper.OnSpawn),
                    ItemMode.Pickup,
                    Vector3.zero,
                    Quaternion.identity,
                    null,
                    null
                );
            }
            return wrapper;
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player = null, ItemID itemId = new())
        {
            // If possible, give the key directly to who found it
            if (CheckExpedition(stateTracker))
            {
                if (player != null)
                {
                    var wrapper = SpawnItemAsync();
                    wrapper.AddSpawnCallback(item => 
                    {
                        KeyItemPickup_Core keyItem = item.Cast<KeyItemPickup_Core>();
                        keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player.Owner);
                    });
                    return;
                }
                stateTracker.AddItemToTerminal(itemId);
            }
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId = new())
        {
            // Isolate these so the lambda can capture them
            var wrapper = SpawnItemAsync();
            var player = terminal.m_syncedInteractionSource.Owner;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving bulkhead key", 2f);
            };

            yield return () =>
            {
                KeyItemPickup_Core? keyItem = wrapper.Item?.TryCast<KeyItemPickup_Core>();
                if (keyItem == null)
                {
                    if (SNetwork.SNet.IsMaster)
                    {
                        stateTracker.AddItemToTerminal(itemId);
                        wrapper.QueueDespawn();
                        FeatureLogger.Error("Failed to spawn key item while spawning bulkhead keycard!");
                        terminal.AddLine("<#F00>Failed to retrieve key! It has been re-added to terminal system.</color>");
                    }
                    return;
                }

                keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player);
                terminal.AddLine($"Key \"{keyItem.PublicName}\" has been given to {player.NickName}");
            };
        }
    }

    /// <summary>
    /// ID of a bulkhead key in vanilla
    /// </summary>
    const uint BULKHEAD_KEY_ID = 146u;

    // Add bulkhead keys from layer data
    [Layer.Callback]
    public void AddBulkheadKeys(Layer.Data data)
    {
        if (data.LayerDatas == null) return;

        ItemID item = data.Item_BulkheadKey_Instance;
        for (int i = 0; i < data.LayerDatas.BulkheadKeyPlacements.Count; i++)
        {
            // R7C3 Main and R5C1 secondary both have an empty placement (for some reason)
            // Skipping them should prevent bad locations from being generated
            if (data.LayerDatas.BulkheadKeyPlacements[i].Any())
            {
                data.Locations.CreateValue(
                    data.Location_BulkheadKey_Instance(i + 1),
                    data.PlacementsToZoneRegions(data.LayerDatas.BulkheadKeyPlacements[i]).Select(i => i.Region).ToArray(),
                    new LocationData(),
                    item
                );
            }
        }
    }

    /// <summary>
    /// Adds options for bulkhead keys
    /// </summary>
    [Game.Callback]
    public void AddOptions(Game.Data data)
    {
        ItemID tag = data.Item_BulkheadKeys;
        data.AddOption(new OptionItemTagOption(
            displayName: "Bulkhead Key Randomization",
            description: "Enables randomization of bulkhead key cards." + OptionTagOption.DESC_SUFFIX,
            category: PickupHelper.PICKUPS_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, tag),
            condition: new(),
            defaultValue: 1,
            tag: tag
        ));
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
            Layer.Data layerData = Layer.Data.GetFromLayer(__instance.m_layer);
            for (int i = 0; i < __instance.m_layer.m_buildData.m_layerGameData.BulkheadKeyPlacements.Count; i++)
            {
                if (__instance.m_layer.m_buildData.m_layerGameData.BulkheadKeyPlacements[i].Any(p => p.Pointer == placementData.Pointer))
                {
                    LocationID loc = layerData.Location_BulkheadKey_Instance(i + 1);
                    PickupHelper.AssociateItem(keyItem.keyPickupCore, loc);
                    return;
                }
            }

            // Extra check, in case we've made some kind of error
            if (keyItem.PublicName.Contains("bulkhead", StringComparison.OrdinalIgnoreCase))
                FeatureLogger.Error("Failed to identify placement for bulkhead key!");
        }
    }

}
