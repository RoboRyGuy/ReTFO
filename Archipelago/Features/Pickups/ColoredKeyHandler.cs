using GameData;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Members;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Core.Localization;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Pickups;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class ColoredKeyHandler_Tags
{
    extension (Game.Data data)
    {
        /// <summary>
        /// Parent tag of all colored key locations
        /// </summary>
        public LocationID Location_ColoredKeys
            => LocationID.From(data, "Colored Key Locations", data => new("Locations checked by picking up colored keys", data.Location_SmallPickups));

        /// <summary>
        /// Parent tag of all colored key items
        /// </summary>
        public ItemID Item_ColoredKeys
            => ItemID.From(data, "Colored Key Items", data => new("Parent of all colored key items", data.Item_SmallPickups));
    }

    extension (Zone.Data data)
    {
        /// <summary>
        /// The colored key spawn location for a particular zone
        /// </summary>
        public LocationID Location_ColoredKey_Instance
            => LocationID.From(
                data,
                $"{data.ZoneName} Colored Key Location",
                data => new("A particular zone's colored key's spawn location", data.Location_ColoredKeys)
            );

        /// <summary>
        /// The colored key item for a particular zone
        /// </summary>
        public ItemID Item_ColoredKey_Instance
            => ItemID.From(
                data,
                $"{data.ZoneName} Colored Key",
                data => new("A particular zone's colored key", data.Item_ColoredKeys),
                new ColoredKeyHandler.ColoredKeyItem(data.Region_Zone)
            );
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

    public class Settings
    {
        [Localized]
        public enum eRetrievalType
        {
            ToTerminal,
            ToHost,
            ToRandom,
            ToDoor,
        }

        [FSDisplayName("Retrieval Type")]
        [FSDescription(
            "Determines what happens when receiving a Colored Key from the Multiworld."
            + "\n\n<u>To Terminal</u>" + "\nPlace the key in the relevant terminal system, from which it can be retrieved at any time."
            + "\n\n<u>To Host</u>"     + "\nGive the key directly to the host, either immediately when receiving it or when the level starts."
            + "\n\n<u>To Random</u>"   + "\nGive the key directly to a randomly-chosen non-bot player."
            + "\n\n<u>To Door</u>"     + "\nImmediately unlock the associated door."
        )]
        public eRetrievalType RetrievalType { get; set; } = eRetrievalType.ToTerminal;
    }

    [FeatureConfig]
    public static Settings Config { get; set; } = null!;

    public class ColoredKeyItem : ExpeditionItem
    {
        public ColoredKeyItem(RegionID zone)
            : base(new ItemData() { IsProgression = true })
        {
            ZoneRegion = zone;
        }

        /// <summary>
        /// The zone this key unlocks
        /// </summary>
        public RegionID ZoneRegion { get; private init; }

        public override RegionID TargetRegion => ZoneRegion;

        /// <summary>
        /// Try to spawn the key now. Returns the async spawn wrapper, with which
        ///  you can queue events for when the key spawns
        /// </summary>
        public AsyncItemSpawnWrapper TrySpawnKey(Game.Data data)
        {
            AsyncItemSpawnWrapper? wrapper = new();
            Zone.Data zoneData = new(data, ZoneRegion);

            LG_Zone? zone = zoneData.GetLG_Zone();
            if (zone == null)
                FeatureLogger.Error("Failed to retrieve zone while spawning colored keycard!");
            else
            {
                LG_SecurityDoor? sourceDoor = zone.m_sourceGate?.SpawnedDoor.TryCast<LG_SecurityDoor>();
                if (SNetwork.SNet.IsMaster && sourceDoor != null)
                    ItemReplicationManager.SpawnItem(
                        new pItemData() { itemID_gearCRC = sourceDoor.m_keyItem.DataBlockID },
                        new Action<ISyncedItem, PlayerAgent>(wrapper.OnSpawn),
                        ItemMode.Pickup,
                        UnityEngine.Vector3.zero,
                        UnityEngine.Quaternion.identity,
                        null,
                        null
                    );
                else if (sourceDoor == null)
                    FeatureLogger.Error("Failed to identify sec door while spawning colored keycard!");
            }

            return wrapper;
        }

        public override void OnEnteredExpedition(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
        {
            switch (Config.RetrievalType)
            {
                case Settings.eRetrievalType.ToTerminal:
                    StateTracker.Get().AddItemToTerminal(itemId);
                    break;

                case Settings.eRetrievalType.ToHost:
                    TrySpawnKey(stateTracker.GameData).AddSpawnCallback((ISyncedItem item) => {
                        KeyItemPickup_Core? keyItem = item.TryCast<KeyItemPickup_Core>();
                        if (keyItem != null)
                            keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, SNetwork.SNet.Master);
                        else if (SNetwork.SNet.IsMaster)
                        {
                            FeatureLogger.Error("Failed to give keycard directly to host. Adding to terminal!");
                            StateTracker.Get().AddItemToTerminal(itemId);
                        }
                    });
                    break;

                case Settings.eRetrievalType.ToRandom:
                    TrySpawnKey(stateTracker.GameData).AddSpawnCallback((ISyncedItem item) => {
                        List<SNetwork.SNet_Player> players = SNetwork.SNet.LobbyPlayers.Where(p => !p.IsBot).ToList();
                        KeyItemPickup_Core? keyItem = item.TryCast<KeyItemPickup_Core>();
                        if (keyItem != null)
                            keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, players[Random.Shared.Next(0, players.Count)]);
                        else if (SNetwork.SNet.IsMaster)
                        {
                            FeatureLogger.Error("Failed to give keycard directly to random human. Adding to terminal!");
                            StateTracker.Get().AddItemToTerminal(itemId);
                        }
                    });
                    break;

                case Settings.eRetrievalType.ToDoor:
                    LG_Zone? zone = new Zone.Data(stateTracker.GameData, ZoneRegion).GetLG_Zone();
                    LG_SecurityDoor? sourceDoor = zone?.m_sourceGate?.SpawnedDoor.TryCast<LG_SecurityDoor>();
                    if (sourceDoor == null)
                        FeatureLogger.Error("Failed to retrieve zone door while unlocking colored keycard door!");
                    else if (SNetwork.SNet.IsMaster)
                        sourceDoor.AttemptOpenCloseInteraction(true);
                    break;
            }
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            AsyncItemSpawnWrapper wrapper = TrySpawnKey(stateTracker.GameData);
            var player = terminal.m_syncedInteractionSource.Owner;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving colored key", 2f);
            };

            yield return () =>
            {
                KeyItemPickup_Core? keyItem = wrapper.Item?.TryCast<KeyItemPickup_Core>();
                if (keyItem == null)
                {
                    if (SNetwork.SNet.IsMaster)
                    {
                        stateTracker.AddItemToTerminal(itemId);
                        FeatureLogger.Error("Failed to spawn key item while spawning colored keycard!");
                        terminal.AddLine("<#F00>Failed to retrieve key! It has been re-added to terminal system.</color>");
                        wrapper.QueueDespawn();
                    }
                    return;
                }

                keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player);
                terminal.AddLine($"Key \"{keyItem.PublicName}\" has been given to {player.NickName}");
            };
        }
    }

    /// <summary>
    /// Adds options for colored keys
    /// </summary>
    [Game.Callback]
    public void AddOptions(Game.Data data)
    {
        ItemID tag = data.Item_ColoredKeys;
        data.AddOption(new OptionItemTagOption(
            displayName: "Colored Keys Randomization",
            description: "Enables randomization of colored key cards." + OptionTagOption.DESC_SUFFIX,
            category: PickupHelper.PICKUPS_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, tag),
            condition: new(),
            defaultValue: 1,
            tag: tag
        ));
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

        data.Locations.CreateValue(
            data.Location_ColoredKey_Instance,
            data.PlacementsToZoneRegions(data.Zone.ProgressionPuzzleToEnter.ZonePlacementData).Select(i => i.Region).ToArray(),
            new LocationData(),
            data.Item_ColoredKey_Instance
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
            Zone.Data zone = Zone.Data.GetFromZone(__instance.Gate.m_linksTo.m_zone);
            PickupHelper.AssociateItem(__instance.m_keyItem.keyPickupCore, zone.Location_ColoredKey_Instance);
        }
    }
}
