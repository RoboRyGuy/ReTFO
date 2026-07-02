using GameData;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.Pickups;

using FluffyUnderware.DevTools.Extensions;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using SNetwork;
using System.Linq;
using System.Runtime.InteropServices;
using TheArchive.Utilities;

public static class BigPickupHandler_Tags
{
    extension (Expedition.Data data)
    {
        /// <summary>
        /// Get a big pickup item by datablock id. This does not check to ensure it's a big pickup item
        /// </summary>
        public ItemID Item_BigPickup_Instance(uint itemDatablockId)
            => data.Item_BigPickup_Instance(ItemDataBlock.GetBlock(itemDatablockId));

        /// <summary>
        /// Get a big pickup item by datablock. This does not check to ensure it's a big pickup item
        /// </summary>
        public ItemID Item_BigPickup_Instance(ItemDataBlock item)
            => ItemID.From(
                data,
                $"{data.ExpeditionName} Item #{item.persistentID} ({item.publicName})",
                data => new("A particualr big pickup item", data.Item_BigPickups),
                new BigPickupHandler.BigPickupItem(data.Region_Expedition, item)
            );

        /// <summary>
        /// Shortcut to get specifically cell pickups
        /// </summary>
        public ItemID Item_BigPickup_Cell
            => data.Item_BigPickup_Instance(BigPickupHandler.CellItemID);

        /// <summary>
        /// Shortcut to get specifically the MWP big pickup.
        /// This is specifically the MWP required to warp via dimension portal.
        /// </summary>
        public ItemID Item_BigPickup_MWP
            => data.Item_BigPickup_Instance(BigPickupHandler.MatterWaveProjectorID);
    }
}

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
    public class BigPickupItem : TerminalItem
    {
        public BigPickupItem(RegionID expedition, ItemDataBlock item)
            : base(MakeRandData(item))
        {
            ExpeditionRegion = expedition;
            ItemDataBlock = item;
        }

        public static ItemData MakeRandData(ItemDataBlock? item) => new ItemData() { IsProgression = true, IsRandomLike = ((item?.persistentID ?? 0) == CellItemID) };

        /// <summary>
        /// The expedition this item was created for
        /// </summary>
        public RegionID ExpeditionRegion { get; private init; }

        /// <summary>
        /// The item datablock this big pickup represents
        /// </summary>
        public ItemDataBlock ItemDataBlock { get; private init; }

        public override RegionID TargetRegion => ExpeditionRegion;

        /// <summary>
        /// Immediately attempt to spawn the related big pickup.
        /// Spawning must be async because the host must approve it.
        /// </summary>
        /// <returns>A wrapper around the spawn attempt. This will later contain the item if it successfully spawns.</returns>
        private AsyncItemSpawnWrapper TrySpawnAsync()
        {
            var wrapper = new AsyncItemSpawnWrapper();

            AIGraph.AIG_CourseNode originNode = AIGraph.AIG_CourseNode.s_allNodes[0];
            AIGraph.pCourseNode pOriginNode = new();
            pOriginNode.Set(originNode);
            
            pItemData data = new pItemData()
            {
                custom = new(),
                itemID_gearCRC = ItemDataBlock.persistentID,
                originCourseNode = pOriginNode,
                originLayer = originNode.LayerType,
                replicatorRef = new(),
                slot = ItemDataBlock.inventorySlot,
            };
            
            ItemReplicationManager.SpawnItem(
                data,
                new Action<ISyncedItem, PlayerAgent>(wrapper.OnSpawn), 
                ItemMode.Pickup, 
                Vector3.zero, 
                Quaternion.identity,
                originNode, 
                null
            );

            return wrapper;
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
        {
            if (CheckExpedition(stateTracker))
            {
                if (RandData.IsRandomLike && player != null)
                {
                    // Give it directly to the player
                    void AttemptPickup(ISyncedItem item)
                        => item.Cast<CarryItemPickup_Core>().m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player.Owner);
                    var wrapper = TrySpawnAsync();
                    wrapper.AddSpawnCallback(AttemptPickup);
                }
                else
                    stateTracker.AddItemToTerminal(itemId);
            }
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            // Isolating these for the lambda just in case
            var wrapper = TrySpawnAsync();
            string itemName = $"Big Pickup #{ItemDataBlock?.persistentID ?? 0} \"{ItemDataBlock?.publicName ?? "null"}\"";
            var node = terminal.SpawnNode;
            var player = terminal.m_syncedInteractionSource;
            var trans = CalcBigObjectPointNearTerminal(terminal);

            yield return () =>
            {
                StateTracker.Get().AddItemToTerminal(itemId);
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
                    stateTracker.AddItemToTerminal(itemId);
                    terminal.AddLine($"<#F00>Failed to retrieve {itemName}! It has been re-added to terminal system.</color>");
                    return;
                }

                carryItem.ItemCuller.OnFactoryCullingSetupDone(node.m_cullNode);
                carryItem.m_sync.AttemptPickupInteraction(
                    ePickupItemInteractionType.Place,
                    player.Owner, default, 
                    trans.Item1, trans.Item2,
                    node, true, true
                );
                carryItem.m_navMarkerPlacer.SetMarkerVisible(true);
                carryItem.m_terminalItem.PlayPing();
                terminal.AddLine($"{itemName} has been placed somewhere nearby");
            };
        }
    }

    /// <summary>
    /// Get a point near the point the player stands for the terminal, and spawn an item there.
    /// If that is determined unsafe, spawn the item where the player is standing (ignoring physics).
    /// If there is no player interacting, spawn it inside the terminal's interaction hitbox.
    /// </summary>
    /// <param name="terminal">The terminal to spawn near</param>
    /// <param name="randSeed">The seed to use for randomization, or null to generate a fairly unique new seed</param>
    /// <returns></returns>
    public static (Vector3, Quaternion) CalcBigObjectPointNearTerminal(LG_ComputerTerminal terminal, int? randSeed = null)
    {
        System.Random rand;
        if (randSeed.HasValue)
            rand = new(randSeed.Value);
        else
            rand = new(new Guid().GetHashCode());

        Quaternion quat = Quaternion.AngleAxis(rand.NextSingle() * 2 * MathF.PI, Vector3.up);
        Vector3 pos;

        // Find a point on the ground near where players should be standing
        Vector3 direction = terminal.m_CameraAlign.TransformDirection(Vector3.down);
        if (Physics.Raycast(new Ray(terminal.m_CameraAlign.position, direction), out RaycastHit hit, 5f))
        {
            float r = rand.NextSingle() * .625f;
            float a = rand.NextSingle() * 2 * MathF.PI;
            Vector3 offset = Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward * r;
            pos = hit.m_Point + offset;
        }
        else if (terminal.m_syncedInteractionSource != null)
        {
            pos = terminal.m_syncedInteractionSource.Position;
        }
        else
        {
            pos = terminal.GetComponentInChildren<Interact_ComputerTerminal>()?.transform.position
                ?? terminal.m_position;
        }

        return (pos, quat);

        /*
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
         */
    }

    // DataBlock ID for a cell
    public static uint CellItemID => 131u;
    //     => ItemDataBlock.GetAllBlocks().First(item => item.terminalItemShortName == "CELL").persistentID;

    // DataBlock ID for specifically the MWP which can be used to activate portal geos
    public static uint MatterWaveProjectorID => 166u;
    //     => ItemDataBlock.GetAllBlocks().First(item => item.name.Contains("PortalKey", StringComparison.OrdinalIgnoreCase)).persistentID;
}
