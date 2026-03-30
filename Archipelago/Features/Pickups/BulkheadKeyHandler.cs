
using Clonesoft.Json;
using GameData;
using LevelGeneration;
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

using Player;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
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
    private class BulkheadKeyLocation : Location
    {
        public BulkheadKeyLocation(Layer.Data data, int count, RegionList regions, Item? item)
            : base(MakeName(data, count), regions, item)
        { }

        public static string MakeName(Layer.Data data, int count)
            => $"{data.LayerName} Bulkhead Key Spawn #{count}";

        private static RandomizationData s_randData = new()
        {
            IsProgression = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    /// <summary>
    /// Item class representing a bulkhead key for a particular expedition
    /// </summary>
    private class BulkheadKeyItem : Item
    {
        public BulkheadKeyItem(Expedition.Data data)
            : base($"{data.ExpeditionName} Bulkhead Key")
        {
            ExpeditionData = data;
        }

        [JsonIgnore]
        public Expedition.Data ExpeditionData { get; set; }

        private static RandomizationData s_randData = new()
        {
            IsProgression = true,
            IsRandomLike = true,
            Categories = new() { "All", "Small Pickups", "Keys", "Bulkheads", "Bulkhead Keys" },
        };
        public override RandomizationData RandData => s_randData;

        // Not sure how to check this at runtime except maybe by name? Not worth it
        const uint BULKHEAD_KEY_ID = 146u;

        public override void OnItemObtained(StateTracker stateTracker, long sourceLocationId, PlayerAgent? player = null)
        {
            // If the bulkhead key is not randomized, we want to try and give it directly to the player who found it
            if (Expedition.Data.FromCurrentExpedition() == ExpeditionData)
            {
                if (!stateTracker.TestRandomization(this, true).IsRandomized && player != null)
                {
                    global::Item? item = ItemSpawnManager.SpawnItem(BULKHEAD_KEY_ID, ItemMode.Pickup, Vector3.zero, Quaternion.identity);
                    KeyItemPickup_Core? keyItem = item?.TryCast<KeyItemPickup_Core>();
                    if (keyItem == null)
                    {
                        FeatureLogger.Error("Failed to spawn key item while spawning bulkhead keycard! Item added to terminal.");
                    }
                    else
                    {
                        keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player.Owner);
                        return;
                    }
                }
                stateTracker.AddItemToTerminal(this);
            }
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (data == ExpeditionData)
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            global::Item? item = ItemSpawnManager.SpawnItem(BULKHEAD_KEY_ID, ItemMode.Pickup, Vector3.zero, Quaternion.identity);
            KeyItemPickup_Core? keyItem = item?.TryCast<KeyItemPickup_Core>();
            if (keyItem == null)
            {
                FeatureLogger.Error("Failed to spawn key item while spawning bulkhead keycard!");
                stateTracker.AddItemToTerminal(this);
                yield return () =>
                {
                    terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving colored key", 2f);
                    terminal.AddLine("<#F00>Failed to retrieve key! It has been re-added to terminal system.</color>");
                };
                yield break;
            }

            // Isolate these so the lambda can capture them
            var sync = keyItem.m_sync;
            var player = terminal.m_localInteractionSource.Owner;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving bulkhead key", 2f);
            };

            yield return () =>
            {
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player);
                terminal.AddLine($"Key \"{keyItem.PublicName}\" has been given to {player.NickName}");
            };
        }
    }

    /// <summary>
    /// Get a bulkhead key item for the provided expedition
    /// </summary>
    /// <param name="data">The expedition to get the key for</param>
    /// <returns>The shared bulkhead key item</returns>
    public static Item GetBulkheadKeyItem(Expedition.Data data)
        => data.GetItem(new BulkheadKeyItem(data));

    // Add bulkhead keys from layer data
    [Layer.Callback]
    public static void AddBulkheadKeys(Layer.Data data)
    {
        if (data.LayerDatas == null) return;

        Item item = GetBulkheadKeyItem(data);
        for (int i = 0; i < data.LayerDatas.BulkheadKeyPlacements.Count; i++)
        {
            // R7C3 Main and R5C1 secondary both have an empty placement (for some reason)
            if (!data.LayerDatas.BulkheadKeyPlacements[i].Any())
                continue;

            data.GetLocation(new BulkheadKeyLocation(
                data, i + 1,
                data.PlacementsToZoneRegions(data.LayerDatas.BulkheadKeyPlacements[i]).Select(info => info.Region).ToList(),
                item
            ));
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
                    PickupHelper.AssociateItem(
                        keyItem.keyPickupCore, 
                        layerData.LookupLocation(BulkheadKeyLocation.MakeName(layerData, i + 1)).ID
                    );
                    return;
                }
            }

            // Extra check, in case we've made some kind of error
            if (keyItem.PublicName.Contains("bulkhead", StringComparison.OrdinalIgnoreCase))
                FeatureLogger.Error("Failed to identify placement for bulkhead key!");
        }
    }

}
