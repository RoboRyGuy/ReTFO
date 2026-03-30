
using Clonesoft.Json;
using GameData;
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

    private class ColoredKeyLocation : Location
    {
        public ColoredKeyLocation(string name, RegionList regions, Item? item)
            : base(name, regions, item) { }

        private static RandomizationData s_randData = new()
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    private class ColoredKeyItem : Item
    {
        public ColoredKeyItem(Zone.Data data)
            : base($"{data.ZoneName} Colored Key")
        {
            ZoneData = data;
        }

        [JsonIgnore]
        public Zone.Data ZoneData { get; set; }

        private static RandomizationData s_randData = new()
        {
            IsProgression = true,
            Categories = { "All", "Small Pickups", "Keys", "Colored Keys" },
        };
        public override RandomizationData RandData => s_randData;

        public override void OnItemObtained(StateTracker stateTracker, long sourceLocationId, PlayerAgent? player)
        {
            if (Expedition.Data.FromCurrentExpedition() == ZoneData.ExpeditionData)
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (data == ZoneData.ExpeditionData)
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            LG_Zone? zone = ZoneData.GetLG_Zone();
            if (zone == null)
                FeatureLogger.Error("Failed to retrieve zone while spawning colored keycard!");

            LG_SecurityDoor? sourceDoor = zone?.m_sourceGate?.SpawnedDoor.TryCast<LG_SecurityDoor>();
            global::Item? item = null;
            if (sourceDoor == null)
                FeatureLogger.Error("Failed to identify sec door while spawning colored keycard!");
            else
                item = ItemSpawnManager.SpawnItem(sourceDoor.m_keyItem.DataBlockID, ItemMode.Pickup, Vector3.zero, Quaternion.identity);

            KeyItemPickup_Core? keyItem = item?.TryCast<KeyItemPickup_Core>();
            if (keyItem == null)
            {
                FeatureLogger.Error("Failed to spawn key item while spawning colored keycard!");
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
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving colored key", 2f);
            };

            yield return () =>
            {
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player);
                terminal.AddLine($"Key \"{keyItem.PublicName}\" has been given to {player.NickName}");
            };
        }
    }

    /// <summary>
    /// Gets a colored key item for the supplied zone
    /// </summary>
    /// <param name="data">The zone the key will unlock</param>
    /// <returns>The colored key item</returns>
    public static Item GetColoredKeyItem(Zone.Data data)
        => data.GetItem(new ColoredKeyItem(data));

    private static string GetColoredKeyLocationName(Zone.Data data)
        => $"{data.ZoneName} Colored Key Spawn Location";

    /// <summary>
    /// Adds colored keys to the world while processing zone data
    /// </summary>
    /// <param name="data">The zone check for colored keys</param>
    [Zone.Callback]
    public static void AddColoredKeys(Zone.Data data)
    {
        if (data.Zone == null) return;
        if (data.Zone.ProgressionPuzzleToEnter.PuzzleType != eProgressionPuzzleType.Keycard_SecurityBox) return;

        data.GetLocation(new ColoredKeyLocation(
            GetColoredKeyLocationName(data),
            data.PlacementsToZoneRegions(data.Zone.ProgressionPuzzleToEnter.ZonePlacementData).Select(info => info.Region).ToList(),
            GetColoredKeyItem(data)
        ));
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
            PickupHelper.AssociateItem(__instance.m_keyItem.keyPickupCore, zone.LookupLocation(GetColoredKeyLocationName(zone)).ID);
        }
    }

}
