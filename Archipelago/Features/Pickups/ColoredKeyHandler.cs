using GameData;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
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

public static class ColoredKeyHandler_Tags
{
    extension (Game.Data gameData)
    {
        public TagResolver Tag_ColoredKeyLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Colored Key Locations", "Locations checked by picking up Colored keys", gd.Tag_SmallPickupLocations));

        public TagResolver Tag_ColoredKeyItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Colored Key Items", "The Colored key item itself", gd.Tag_SmallPickupItems));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class ColoredKeyHandler : ArchipelagoFeature
{
    public override string Name => "Colored Key Handler";
    public override string Description => "Handles processing, despawning, and spawning colored keys as part of randomization";
    public override FeatureGroup Group => FeatureGroups.PickupHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private static class ColoredKeyLocation
    {
        public static TagResolver MakeTag(Zone.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ZoneName} Colored Key Location", "A colored key spawn location", gd.Tag_ColoredKeyLocations));

        public static LocationData MakeRandData() => new LocationData();
    }

    private class ColoredKeyItem : Item
    {
        public ColoredKeyItem(Zone.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ZoneData = data;
        }

        public static TagResolver MakeTag(Zone.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ZoneName} Colored Key Item", "A colored key", gd.Tag_ColoredKeyItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Zone.Data ZoneData { get; set; }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (ZoneData.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ZoneData.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            LG_Zone? zone = ZoneData.GetLG_Zone();
            if (zone == null)
                FeatureLogger.Error("Failed to retrieve zone while spawning colored keycard!");

            LG_SecurityDoor? sourceDoor = zone?.m_sourceGate?.SpawnedDoor.TryCast<LG_SecurityDoor>();
            AsyncItemSpawnWrapper? wrapper = new();
            if (sourceDoor == null)
                FeatureLogger.Error("Failed to identify sec door while spawning colored keycard!");
            else
            {
                ItemReplicationManager.SpawnItem(
                    new pItemData() { itemID_gearCRC = sourceDoor.m_keyItem.DataBlockID },
                    new Action<ISyncedItem, PlayerAgent>(wrapper.OnSpawn),
                    ItemMode.Pickup,
                    Vector3.zero,
                    Quaternion.identity,
                    null,
                    null
                );
            }

            var player = terminal.m_localInteractionSource.Owner;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving colored key", 2f);
            };

            yield return () =>
            {
                KeyItemPickup_Core? keyItem = wrapper.Item?.TryCast<KeyItemPickup_Core>();
                if (keyItem == null)
                {
                    stateTracker.AddItemToTerminal(this);
                    FeatureLogger.Error("Failed to spawn key item while spawning colored keycard!");
                    terminal.AddLine("<#F00>Failed to retrieve key! It has been re-added to terminal system.</color>");
                    wrapper.QueueDespawn();
                    return;
                }

                keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player);
                terminal.AddLine($"Key \"{keyItem.PublicName}\" has been given to {player.NickName}");
            };
        }
    }

    /// <summary>
    /// Gets a colored key item for the supplied zone
    /// </summary>
    /// <param name="data">The zone the key will unlock</param>
    /// <returns>The colored key item</returns>
    public static KeyedItem GetColoredKeyItem(Zone.Data data)
    {
        if (data.TryLookupItem(ColoredKeyItem.MakeTag(data), out var item))
            return item;

        Item newItem = new ColoredKeyItem(data);
        return new KeyedItem(data.AddItem(newItem), newItem);
    }

    /// <summary>
    /// Adds colored keys to the world while processing zone data
    /// </summary>
    /// <param name="data">The zone check for colored keys</param>
    [Zone.Callback]
    public void AddColoredKeys(Zone.Data data)
    {
        if (data.Zone == null) return;
        if (data.Zone.ProgressionPuzzleToEnter.PuzzleType != eProgressionPuzzleType.Keycard_SecurityBox) return;

        RegionList regions = data.PlacementsToZoneRegions(data.Zone.ProgressionPuzzleToEnter.ZonePlacementData)
            .Select(i => i.Region)
            .Distinct()
            .ToArray();

        data.AddLocation(
            ColoredKeyLocation.MakeTag(data),
            regions,
            ColoredKeyLocation.MakeRandData(),
            GetColoredKeyItem(data).ID
        );
    }

    /// <summary>
    /// Identifies colored keys after they're spawned by checking sec doors after they're set up
    /// </summary>
    [ArchivePatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.SetupKeyItemLock))]
    public static class LG_SecurityDoor__SetupKeyItemLock__Patch
    {
        public static void Postfix(LG_SecurityDoor __instance, GateKeyItem keyItem)
        {
            Zone.Data zone = Zone.Data.FromZone(__instance.Gate.m_linksTo.m_zone);
            if (zone.TryLookupLocation(ColoredKeyLocation.MakeTag(zone), out var loc))
                PickupHelper.AssociateItem(__instance.m_keyItem.keyPickupCore, loc.ID);
            else
                FeatureLogger.Error($"Failed to create association for colored key for zone: {zone.ZoneName}");
        }
    }

}
