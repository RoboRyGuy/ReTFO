using Clonesoft.Json;
using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.Pickups;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class BigPickupHelper : ArchipelagoFeature
{
    public override string Name => "Big Pickups Helper";
    public override string Description 
        => "Provides utilites used by other features to manage big pickups";
    public override FeatureGroup Group => FeatureGroups.PickupHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private class BigPickupItem : Item
    {
        public BigPickupItem(Expedition.Data data, ItemDataBlock? item)
            : base($"{data.ExpeditionName} Item #{item?.persistentID ?? 0} ({item?.publicName ?? "null"})")
        {
            Data = data;
            ItemDataBlock = item;
            m_randData = new()
            {
                IsProgression = true,
                IsRandomLike = item?.terminalItemShortName == "CELL",
                Categories = new() { $"{item?.publicName}s", "Big Pickups", "Pickups", "Items", "All" }
            };
        }

        /// <summary>
        /// The expedition this item was created for
        /// </summary>
        [JsonIgnore]
        public Expedition.Data Data { get; set; }

        /// <summary>
        /// The item datablock this big pickup represents
        /// </summary>
        [JsonIgnore]
        public ItemDataBlock? ItemDataBlock { get; set; }

        private RandomizationData m_randData;
        public override RandomizationData RandData => m_randData;

        /// <summary>
        /// Immediately attempt to spawn the related big pickup.
        /// </summary>
        /// <returns>The spawned item, or null if it failed</returns>
        private CarryItemPickup_Core? TrySpawnItemNow()
        {
            global::Item? item =
                ItemDataBlock == null
                ? null
                : ItemSpawnManager.SpawnItem(ItemDataBlock.persistentID, ItemMode.Pickup, Vector3.zero, Quaternion.identity);
            CarryItemPickup_Core? carryItem = item?.TryCast<CarryItemPickup_Core>();
            if (carryItem == null) return null;

            // Inject a despawn packet into all captured buffers to trigger this item to despawn if a reload occurs
            var packet = ItemReplicationManager.Current.m_SNetReplicationManager_Item.m_despawnPacket;
            SNetwork.pReplicationData data = new()
            {
                isRecall = 0,
                PrefabID = 0,
                ReplicatorKey = carryItem.m_sync.GetReplicator().Key
            };
            Il2CppStructArray<byte> bytes = new(SNetwork.SNet_Packet<SNetwork.pReplicationData>.s_marshaller.SizeWithIDs);
            SNetwork.SNet_Packet<SNetwork.pReplicationData>.s_marshaller.MarshalToBytes(data, bytes);
            SNetwork.SNet_Packet.InjectIDPacketIndex(packet, bytes, carryItem.m_sync.GetReplicator().KeyBytes);
            foreach (var buffer in SNetwork.SNet.Capture.m_buffers)
                buffer.GetPass(SNetwork.eCapturePass.SessionPass).Add(bytes);

            return carryItem;
        }

        public override void OnItemObtained(StateTracker stateTracker, long sourceLocationId, PlayerAgent? player)
        {
            if (Expedition.Data.FromCurrentExpedition() == Data)
            {
                if (RandData.IsRandomLike && !stateTracker.TestRandomization(this, true).IsRandomized && player != null)
                {
                    // Give it directly to the player
                    CarryItemPickup_Core? carryItem = TrySpawnItemNow();
                    if (carryItem == null)
                    {
                        string itemName = $"Big Pickup #{ItemDataBlock?.persistentID ?? 0} \"{ItemDataBlock?.publicName ?? "null"}\"";
                        FeatureLogger.Error($"Failed to spawn {itemName}! Item added to terminal.");
                        stateTracker.AddItemToTerminal(this);
                    }
                    else
                    {
                        carryItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player.Owner);
                        return;
                    }
                }
            }
            stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (data == Data)
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            string itemName = $"Big Pickup #{ItemDataBlock?.persistentID ?? 0} \"{ItemDataBlock?.publicName ?? "null"}\"";
            var carryItem = TrySpawnItemNow();
            if (carryItem == null)
            {
                FeatureLogger.Error($"Failed to spawn {itemName}!");
                stateTracker.AddItemToTerminal(this);
                yield return () =>
                {
                    terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Retrieving {itemName}", 2f);
                    terminal.AddLine($"<#F00>Failed to retrieve {itemName}! It has been re-added to terminal system.</color>");
                };
                yield break;
            }

            // Isolating these for the lambda just in case
            Vector3 position = terminal.m_localInteractionSource.FPSCamera.Position;
            Vector3 right = terminal.m_localInteractionSource.FPSCamera.FlatRight;
            var node = terminal.SpawnNode;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Retrieving {itemName}", 2f);
            };

            yield return () =>
            {
                // We'll calculate a random raycast behind the player using their camera view
                // If we get a good hit, we'll place the cell there. Otherwise, we'll place it at their feet
                System.Random rand = new();

                // There's probably an easier way to calculate this. Whatever, this works!
                Vector3 backVector = Quaternion.AngleAxis(30f + rand.NextSingle() * 120f, Vector3.up) * right;
                Quaternion fullDown = Quaternion.FromToRotation(backVector, Vector3.down);
                Quaternion downAngle = Quaternion.Lerp(Quaternion.identity, fullDown, .4f + rand.NextSingle() * .4f);
                Vector3 testVector = downAngle * backVector;

                if (Physics.Raycast(position, testVector, out RaycastHit hit))
                    position = hit.point;

                carryItem.m_sync.AttemptPickupInteraction(
                    ePickupItemInteractionType.Place,
                    null, default, position,
                    Quaternion.AngleAxis(360f * rand.NextSingle(), Vector3.up),
                    node, true, true
                );
                carryItem.m_navMarkerPlacer.SetMarkerVisible(true);
                carryItem.m_terminalItem.PlayPing();
                terminal.AddLine($"{itemName} has been placed somewhere nearby");
            };
        }
    }

    // DataBlock ID for a cell
    public static uint CellItemID
        => ItemDataBlock.GetAllBlocks().First(item => item.terminalItemShortName == "CELL").persistentID;

    // DataBlock ID for specifically the MWP which can be used to activate portal geos
    public static uint MatterWaveProjectorID
        => ItemDataBlock.GetAllBlocks().First(item => item.name.Contains("PortalKey", StringComparison.OrdinalIgnoreCase)).persistentID;

    // Gets / creates a big pickup item for a particular item datablock
    public static Item GetBigPickupItem(Expedition.Data data, uint itemDataBlockId)
    {
        ItemDataBlock itemDataBlock = ItemDataBlock.GetBlock(itemDataBlockId);
        if (itemDataBlock == null)
            FeatureLogger.Error($"Failed to find big pickup #{itemDataBlockId}: {data.ExpeditionName}");

        return data.GetItem(new BigPickupItem(data, itemDataBlock));
    }

    /// <summary>
    /// Associate a big pickup distribution with a location ID so it's checked when the pickup is grabbed.
    /// </summary>
    /// <param name="distribution">The distribution to associate</param>
    /// <param name="locationId">ID of the location</param>
    /// <exception cref="ArgumentException">If given a non-big-pickup distribution, or if the distribution already has an association</exception>
    public static void AssociateDistributionWithLocation(LG_Distribute_PickupItemsPerZone distribution, long locationId)
    {
        if (!ArchipelagoFeatureHelper.GetFeature<BigPickupHelper>().Enabled)
            return;

        string locationName() => StateTracker.Get().MidManager.GetProcessedGameData().LookupLocation(locationId).Name;
        if (distribution.m_pickupType != ePickupItemType.BigGenericPickup)
            throw new ArgumentException($"Cannot use AssociateDistributionWithLocation on non-big-pickup item; attempted with location: {locationName()}");
        if (distribution.m_consumableDistributionData != null)
            throw new ArgumentException($"Existing association on big pickup; cannot replace! Location: {locationName()}");
        distribution.m_consumableDistributionData = new(); // Dud block used to associate info
        var bytes = BitConverter.GetBytes(locationId);
        distribution.m_consumableDistributionData.SpawnsPerZone = BitConverter.ToInt32(bytes, 0);
        distribution.m_consumableDistributionData.persistentID = BitConverter.ToUInt32(bytes, 4);
    }

    /// <summary>
    /// Associate a big pickup distribution with a location ID so it's checked when the pickup is grabbed.
    /// </summary>
    /// <param name="distribution">The distribution to associate</param>
    /// <param name="locationId">ID of the location</param>
    /// <exception cref="ArgumentException">If given a non-big-pickup distribution, or if the distribution already has an association</exception>
    public static void AssociateDistributionWithLocation(LG_DistributePickUpItem distribution, long locationId)
    {
        if (!ArchipelagoFeatureHelper.GetFeature<BigPickupHelper>().Enabled)
            return;

        string locationName() => StateTracker.Get().MidManager.GetProcessedGameData().LookupLocation(locationId).Name;
        if (distribution.m_type != ePickupItemType.BigGenericPickup || distribution.m_function != ExpeditionFunction.BigPickupItem)
            throw new ArgumentException($"Cannot use AssociateDistributionWithLocation on non-big-pickup item; attempted with location: {locationName()}");
        if (distribution.m_consumableData != null)
            throw new ArgumentException($"Existing association on big pickup; cannot replace! Location: {locationName()}");
        distribution.m_consumableData = new(); // Dud block used to associate info
        var bytes = BitConverter.GetBytes(locationId);
        distribution.m_consumableData.SpawnsPerZone = BitConverter.ToInt32(bytes, 0);
        distribution.m_consumableData.persistentID = BitConverter.ToUInt32(bytes, 4);
    }

    // After the big pickup is spawned, retrieve its count and associate it by name
    [ArchivePatch(typeof(LG_PickupItemBuilder), nameof(LG_PickupItemBuilder.SetupFunctionGO))]
    public static class LG_PickupItemBuilder__SetupFunctionGO__Patch
    {
        public static void Postfix(LG_PickupItemBuilder __instance, LG_LayerType layer, GameObject GO)
        {
            if (__instance.m_type != ePickupItemType.BigGenericPickup) return;
            if (__instance.m_function != ExpeditionFunction.BigPickupItem) return;
            if (__instance.m_consumableData == null) return;

            CarryItemPickup_Core? spawnedItem = GO.GetComponentInChildren<CarryItemPickup_Core>();
            if (spawnedItem == null) throw new NullReferenceException();

            byte[] bytes = new byte[8];
            BitConverter.GetBytes(__instance.m_consumableData.SpawnsPerZone).CopyTo(bytes, 0);
            BitConverter.GetBytes(__instance.m_consumableData.persistentID).CopyTo(bytes, 4);
            long id = BitConverter.ToInt64(bytes);
            PickupHelper.AssociateItem(spawnedItem, id);
        }
    }

}
