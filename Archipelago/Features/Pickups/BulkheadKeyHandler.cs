
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

using ReTFO.Archipelago.Features;
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
    /// Item class representing a bulkhead key for a particular expedition
    /// </summary>
    private class BulkheadKeyItem : Item
    {
        public BulkheadKeyItem(Expedition.Data data)
            : base($"{data.ExpeditionName} Bulkhead Key", eRandomizationType.Progression, new List<string>() { "All", "Small Pickups", "Keys", "Bulkheads", "Bulkhead Keys" })
        {
            ExpeditionData = data;
        }

        [JsonIgnore]
        public Expedition.Data ExpeditionData { get; set; }

        public override void OnItemObtained(StateTracker stateTracker)
        {
            if (Expedition.Data.FromCurrentExpedition() == ExpeditionData)
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (data == ExpeditionData)
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            const uint bulkheadKeyItemId = 146u; // Just a const, since I'm not sure how else to correctly identify the item block at runtime
            global::Item? item = ItemSpawnManager.SpawnItem(bulkheadKeyItemId, ItemMode.Pickup, Vector3.zero, Quaternion.identity);
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

    /// <summary>
    /// Get a bulkhead key location name given the layer and a 1-indexed count of which spawn it is
    /// </summary>
    /// <param name="data">The layer the key spawns in</param>
    /// <param name="count">Which key spawn this is</param>
    /// <returns>The name of the location</returns>
    private static string GetBulkheadKeyLocationName(Layer.Data data, int count)
        => $"{data.LayerName} Bulkhead Key Spawn #{count}";

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

            data.AddLocation(
                GetBulkheadKeyLocationName(data, i + 1),
                data.PlacementsToZoneRegions(data.LayerDatas.BulkheadKeyPlacements[i]).Select(info => info.Region).ToList(),
                eRandomizationType.Progression,
                false,
                item
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
                    PickupHelper.AssociateItem(keyItem.keyPickupCore, layerData.LookupLocation(GetBulkheadKeyLocationName(layerData, i + 1)).ID);
                    return;
                }
            }

            // Extra check, in case we've made some kind of error
            if (keyItem.PublicName.Contains("bulkhead", StringComparison.OrdinalIgnoreCase))
                FeatureLogger.Error("Failed to identify placement for bulkhead key!");
        }
    }

}
