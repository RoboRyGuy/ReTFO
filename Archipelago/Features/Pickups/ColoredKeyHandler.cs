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

        public override Expedition.Data? RequiredExpedition => ZoneData;

        /// <summary>
        /// Try to spawn the key now. Returns the async spawn wrapper, with which
        ///  you can queue events for when the key spawns
        /// </summary>
        public AsyncItemSpawnWrapper TrySpawnKey()
        {
            LG_Zone? zone = ZoneData.GetLG_Zone();
            if (zone == null)
                FeatureLogger.Error("Failed to retrieve zone while spawning colored keycard!");

            LG_SecurityDoor? sourceDoor = zone?.m_sourceGate?.SpawnedDoor.TryCast<LG_SecurityDoor>();
            AsyncItemSpawnWrapper? wrapper = new();
            if (sourceDoor == null)
                FeatureLogger.Error("Failed to identify sec door while spawning colored keycard!");
            else if (SNetwork.SNet.IsMaster)
            {
                ItemReplicationManager.SpawnItem(
                    new pItemData() { itemID_gearCRC = sourceDoor.m_keyItem.DataBlockID },
                    new Action<ISyncedItem, PlayerAgent>(wrapper.OnSpawn),
                    ItemMode.Pickup,
                    UnityEngine.Vector3.zero,
                    UnityEngine.Quaternion.identity,
                    null,
                    null
                );
            }

            return wrapper;
        }

        /// <summary>
        /// Immediately retrieves the key item, placing it into the terminal,
        /// unlocking the relevant door, or giving it to a player.
        /// Assumes you are in the correct expedition.
        /// </summary>
        public void RetrieveKey()
        {
            switch (Config.RetrievalType)
            {
                case Settings.eRetrievalType.ToTerminal:
                    StateTracker.Get().AddItemToTerminal(this);
                    break;

                case Settings.eRetrievalType.ToHost:
                    TrySpawnKey().AddSpawnCallback((ISyncedItem item) => {
                        KeyItemPickup_Core? keyItem = item.TryCast<KeyItemPickup_Core>();
                        if (keyItem != null)
                            keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, SNetwork.SNet.Master);
                        else if (SNetwork.SNet.IsMaster)
                        {
                            FeatureLogger.Error("Failed to give keycard directly to host. Adding to terminal!");
                            StateTracker.Get().AddItemToTerminal(this);
                        }
                    });
                    break;

                case Settings.eRetrievalType.ToRandom:
                    TrySpawnKey().AddSpawnCallback((ISyncedItem item) => {
                        List<SNetwork.SNet_Player> players = SNetwork.SNet.LobbyPlayers.Where(p => !p.IsBot).ToList();
                        KeyItemPickup_Core? keyItem = item.TryCast<KeyItemPickup_Core>();
                        if (keyItem != null)
                            keyItem.m_sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, players[Random.Shared.Next(0, players.Count)]);
                        else if(SNetwork.SNet.IsMaster)
                        {
                            FeatureLogger.Error("Failed to give keycard directly to random human. Adding to terminal!");
                            StateTracker.Get().AddItemToTerminal(this);
                        }
                    });
                    break;

                case Settings.eRetrievalType.ToDoor:
                    LG_Zone? zone = ZoneData.GetLG_Zone();
                    LG_SecurityDoor? sourceDoor = zone?.m_sourceGate?.SpawnedDoor.TryCast<LG_SecurityDoor>();
                    if (sourceDoor == null)
                        FeatureLogger.Error("Failed to retrieve zone door while unlocking colored keycard door!");
                    else if (SNetwork.SNet.IsMaster)
                        sourceDoor.AttemptOpenCloseInteraction(true);
                    break;
            }
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (ZoneData.IsCurrentlyInExpedition())
                RetrieveKey();
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ZoneData.IsSameExpedition(data))
                RetrieveKey();
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            AsyncItemSpawnWrapper wrapper = TrySpawnKey();
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
                        stateTracker.AddItemToTerminal(this);
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
    /// Adds options for colored keys
    /// </summary>
    [Game.Callback]
    public void AddOptions(Game.Data data)
    {
        OptionID toggle = data.AddOption(new OptionToggle()
        {
            DisplayName = "Randomize Colored Keys",
            Description = "Enables randomization of colored key cards",
            Category = PickupHelper.PICKUPS_OPTION_CATEGORY,
            Condition = new(),
            DefaultValue = 1,
        });

        data.AddOption(new OptionWhiteOrBlacklist()
        {
            Toggle = toggle,
            Tag = data.Tag_ColoredKeyItems,
            Condition = new(),
        });
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
