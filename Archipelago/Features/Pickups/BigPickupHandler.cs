using GameData;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.Pickups;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault, AutomatedFeature]
public class BigPickupHandler : ArchipelagoFeature
{
    public override string Name => "Big Pickups Handler";
    public override string Description 
        => "Provides utilites used by other features to manage big pickups";
    public override FeatureGroup Group => FeatureGroups.PickupHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// A big pickup item
    /// </summary>
    private class BigPickupItem : Item
    {
        public BigPickupItem(Expedition.Data data, ItemDataBlock? item)
            : base(MakeTag(data, item), MakeRandData(item))
        {
            Data = data;
            ItemDataBlock = item;
        }

        public static TagResolver MakeTag(Expedition.Data data, ItemDataBlock? item)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Item #{item?.persistentID ?? 0} ({item?.publicName ?? "null"})", "A particular big pickup item", gd.Tag_BigPickupItems));

        public static ItemData MakeRandData(ItemDataBlock? item) => new ItemData() { IsProgression = true, IsRandomLike = ((item?.persistentID ?? 0) == CellItemID) };

        /// <summary>
        /// The expedition this item was created for
        /// </summary>
        public Expedition.Data Data { get; set; }

        /// <summary>
        /// The item datablock this big pickup represents
        /// </summary>
        public ItemDataBlock? ItemDataBlock { get; set; }

        /// <summary>
        /// Immediately attempt to spawn the related big pickup.
        /// Spawning must be async because the host must approve it.
        /// </summary>
        /// <returns>A wrapper around the spawn attempt. This will later contain the item if it successfully spawns.</returns>
        private AsyncItemSpawnWrapper TrySpawnAsync()
        {
            var wrapper = new AsyncItemSpawnWrapper();
            if (ItemDataBlock != null)
                ItemReplicationManager.SpawnItem(
                    new pItemData() { itemID_gearCRC = ItemDataBlock.persistentID }, 
                    new Action<ISyncedItem, PlayerAgent>(wrapper.OnSpawn), 
                    ItemMode.Pickup, 
                    Vector3.zero, 
                    Quaternion.identity, 
                    null, 
                    null
                );
            return wrapper;
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (Data.IsCurrentlyInExpedition())
            {
                if (RandData.IsRandomLike && !stateTracker.TestRandomization(this, true).IsRandomized && player != null)
                {
                    // Give it directly to the player
                    void AttemptPickup(ISyncedItem item, PlayerAgent _)
                        => item.Cast<CarryItemPickup_Core>().m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player.Owner);
                    var wrapper = TrySpawnAsync();
                    wrapper.OnItemSpawned += AttemptPickup;
                    if (wrapper.Item != null) AttemptPickup(wrapper.Item, null!);
                }
            }
            stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (Data.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            // Isolating these for the lambda just in case
            string itemName = $"Big Pickup #{ItemDataBlock?.persistentID ?? 0} \"{ItemDataBlock?.publicName ?? "null"}\"";
            var wrapper = TrySpawnAsync();
            var node = terminal.SpawnNode;

            // We'll calculate a random raycast behind the player using their camera view
            // If we get a good hit, we'll place the item there. Otherwise, we'll place it at their feet
            System.Random rand = new(Guid.NewGuid().GetHashCode()); // Hopefully this is entropic enough

            // There's probably an easier way to calculate this. Whatever, this works!
            PlayerAgent player = terminal.m_syncedInteractionSource;
            Vector3 position = player.FPSCamera.Position;
            Vector3 right = player.FPSCamera.FlatRight;
            Vector3 backVector = Quaternion.AngleAxis(30f + rand.NextSingle() * 120f, Vector3.up) * right;
            Quaternion fullDown = Quaternion.FromToRotation(backVector, Vector3.down);
            Quaternion downAngle = Quaternion.Lerp(Quaternion.identity, fullDown, .4f + rand.NextSingle() * .4f);
            Vector3 testVector = downAngle * backVector;
            if (Physics.Raycast(position, testVector, out RaycastHit hit, 10f, 1))
                position = hit.point;
            Quaternion rotation = Quaternion.AngleAxis(360f * rand.NextSingle(), Vector3.up);

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Retrieving {itemName}", 2f);
            };

            yield return () =>
            {
                CarryItemPickup_Core? carryItem = wrapper.Item?.TryCast<CarryItemPickup_Core>();
                if (carryItem == null)
                {
                    // Effectively timed out. If it successfully spawns after now, we don't want it anymore
                    wrapper.QueueDespawn();
                    FeatureLogger.Error($"Failed to spawn {itemName}!");
                    stateTracker.AddItemToTerminal(this);
                    terminal.AddLine($"<#F00>Failed to retrieve {itemName}! It has been re-added to terminal system.</color>");
                    return;
                }

                carryItem.m_sync.AttemptPickupInteraction(
                    ePickupItemInteractionType.Place,
                    player.Owner, default, 
                    position, rotation,
                    node, true, true
                );
                carryItem.m_navMarkerPlacer.SetMarkerVisible(true);
                carryItem.m_terminalItem.PlayPing();
                terminal.AddLine($"{itemName} has been placed somewhere nearby");
            };
        }
    }

    // DataBlock ID for a cell
    public static uint CellItemID => 131u;
    //     => ItemDataBlock.GetAllBlocks().First(item => item.terminalItemShortName == "CELL").persistentID;

    // DataBlock ID for specifically the MWP which can be used to activate portal geos
    public static uint MatterWaveProjectorID => 166u;
    //     => ItemDataBlock.GetAllBlocks().First(item => item.name.Contains("PortalKey", StringComparison.OrdinalIgnoreCase)).persistentID;

    // Gets / creates a big pickup item for a particular item datablock
    public static KeyedItem GetBigPickupItem(Expedition.Data data, uint itemDataBlockId)
    {
        ItemDataBlock itemDataBlock = ItemDataBlock.GetBlock(itemDataBlockId);
        if (itemDataBlock == null)
            FeatureLogger.Error($"Failed to find big pickup #{itemDataBlockId}: {data.ExpeditionName}");

        if (data.TryLookupItem(BigPickupItem.MakeTag(data, itemDataBlock), out var item))
            return item;

        Item newItem = new BigPickupItem(data, itemDataBlock);
        return new KeyedItem(data.AddItem(newItem), newItem);
    }

}
