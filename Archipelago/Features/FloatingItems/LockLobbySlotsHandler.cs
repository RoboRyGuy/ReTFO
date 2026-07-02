using CellMenu;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using SNetwork;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using TMPro;
using UnityEngine;

namespace ReTFO.Archipelago.Features.FloatingItems;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using XInputDotNetPure;

public static class LockLobbySlotsHandler_Tags
{
    extension(Game.Data data)
    {
        /// <summary>
        /// Parent tag of all items which unlock lobby slots
        /// </summary>
        public ItemID Item_LobbySlotUnlocks
            => ItemID.From(data, "Unlock Lobby Slot Items", data => new("Items which unlock more lobby slots", data.Item_All));

        /// <summary>
        /// A lobby slot unlock item for a particular slot index
        /// </summary>
        /// <param name="index">0-indexed position which will be unlocked (0 is master (woods), 1 is dauda, etc)</param>
        public ItemID Item_LobbySlotUnlock_Instance(int index)
            => ItemID.From(
                data, 
                $"Lobby Slot #{index} Unlock", 
                data => new("Item which unlocks a particular lobby slot", data.Item_LobbySlotUnlocks),
                new LockLobbySlotsHandler.LobbySlotUnlockItem(index)
            );
    }
}

// Handles the lock lobby slots item, which prevents players from using some or all lobby slots to bring in allies
[EnableFeatureByDefault, AutomatedFeature]
public class LockLobbySlotsHandler : ArchipelagoFeature
{
    public override string Name => "Lock Lobby Spots Handler";
    public override string Description
        => "Locks lobby slots and forces players to find specific items to unlock them";
    public override FeatureGroup Group => FeatureGroups.FloatingHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public const string LOBBY_SLOT_OPTION_CATEGORY = "Lobby Slots";

    public class LobbySlotUnlockItem : Item
    {
        public LobbySlotUnlockItem(int index)
            : base(MakeRandData())
        {
            Index = index;
        }

        public static ItemData MakeRandData() => new ItemData() { IsUseful = true, IsCollectedByDefault = true };

        public int Index { get; private init; }

        public void LockButtonNow()
        {
            var lobbyBar = MainMenuGuiLayer.Current.PageLoadout.m_playerLobbyBars[Index];

            // Setting the text and icon to grey
            TextMeshPro text = lobbyBar.m_permissionButton.GetComponent<TextMeshPro>();
            Color color = Color.grey;
            color.a = .4f;
            text.color = color;
            if ((lobbyBar.m_permissionButton.m_textColorOrg?.Count ?? 0) > 0)
                lobbyBar.m_permissionButton.m_textColorOrg![0] = new Color(color.r, color.g, color.b, lobbyBar.m_permissionButton.m_textColorOrg[0].a);
            if ((lobbyBar.m_permissionButton.m_textColorOut?.Count ?? 0) > 0)
                lobbyBar.m_permissionButton.m_textColorOut![0] = new Color(color.r, color.g, color.b, lobbyBar.m_permissionButton.m_textColorOut[0].a);
            if ((lobbyBar.m_permissionButton.m_textColorOver?.Count ?? 0) > 0)
                lobbyBar.m_permissionButton.m_textColorOver![0] = new Color(color.r, color.g, color.b, lobbyBar.m_permissionButton.m_textColorOver[0].a);
            foreach (SpriteRenderer r in lobbyBar.m_permissionButton.GetComponentsInChildren<SpriteRenderer>())
                r.color = color;

            // Removing the dropdown
            lobbyBar.OnPermissionButtonPressed = null;
        }

        public void UnlockButtonNow()
        {
            var lobbyBar = MainMenuGuiLayer.Current.PageLoadout.m_playerLobbyBars[Index];

            // Setting the text and icon to red
            Color color = Color.red;
            color.a = .4f;
            TextMeshPro text = lobbyBar.m_permissionButton.GetComponent<TextMeshPro>();
            text.color = color;
            if ((lobbyBar.m_permissionButton.m_textColorOrg?.Count ?? 0) > 0)
                lobbyBar.m_permissionButton.m_textColorOrg![0] = new Color(color.r, color.g, color.b, lobbyBar.m_permissionButton.m_textColorOrg[0].a);
            if ((lobbyBar.m_permissionButton.m_textColorOut?.Count ?? 0) > 0)
                lobbyBar.m_permissionButton.m_textColorOut![0] = new Color(color.r, color.g, color.b, lobbyBar.m_permissionButton.m_textColorOut[0].a);
            if ((lobbyBar.m_permissionButton.m_textColorOver?.Count ?? 0) > 0)
                lobbyBar.m_permissionButton.m_textColorOver![0] = new Color(color.r, color.g, color.b, lobbyBar.m_permissionButton.m_textColorOver[0].a);
            foreach (SpriteRenderer r in lobbyBar.m_permissionButton.GetComponentsInChildren<SpriteRenderer>())
                r.color = color;

            // Restoring the dropdown - For the record, I just used the debugger to figure out what is registered by default
            var closure = new CM_PageLoadout.__c__DisplayClass29_0()
            {
                __4__this = MainMenuGuiLayer.Current.PageLoadout,
                bar = lobbyBar
            };
            IntPtr methodPtr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                closure.ObjectClass,
                false,
                "<Setup>b__7", // Can't use `nameof` here because the carots get replaced with underscores
                typeof(void).FullName!,
                new string[] { typeof(int).FullName! }
            );
            lobbyBar.OnPermissionButtonPressed = new Il2CppSystem.Action<int>(closure, methodPtr);
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player = null, ItemID itemId = new())
        {
            // Make the permissions button work again
            UnlockButtonNow();
        }

        public override void OnItemLost(StateTracker stateTracker, ItemID itemId = new())
        {
            // Try and kick the current player in the slot
            SNet.Slots.SetSlotPermission(Index, SNet_PlayerSlotManager.SlotPermission.Forbidden);
            SNet_Player player = SNet.Slots.CharacterSlots[Index].player;
            if (player != null) SNet.Slots.RemoveFromPlayersInGame(player);

            // Lock out the permissions button
            LockButtonNow();
        }
    }

    [Game.Callback]
    public void AddSlotUnlockItems(Game.Data data)
    {
        // Define the items
        for (int i = 1; i < SNet.Slots.CharacterSlots.Count; i++)
            data.AddFloatingItem(data.Region_Always, data.Item_LobbySlotUnlock_Instance(i));

        // Add the options
        ItemID tag = data.Item_LobbySlotUnlocks;
        uint[] sort = Option.MakeSortKey(data, tag);

        OptionID unlockRange = data.AddOption(new OptionRange(
            displayName: $"Number of Unlocked Lobby Slots",
            description:
                "The number of lobby slots which will be unlocked when the game starts."
                + " This does not include the host slot, which will always be unlocked."
                + " This can be 0. To unlock all lobby slots, use -1.",
            category: LOBBY_SLOT_OPTION_CATEGORY,
            categorySort: sort,
            defaultValue: 1,
            condition: new(),
            min: -1,
            max: SNet.Slots.CharacterSlots.Count - 1
        ));
        OptionID randomizationEnabled = data.AddOption(new OptionDoesNotEqualOperation(unlockRange, -1));

        data.AddOption(new OptionAddToSet(
            target: Option.eSetTarget.ItemWhitelist,
            tag: tag,
            condition: randomizationEnabled
        ));
        data.AddOption(new OptionAddToSet(
            target: Option.eSetTarget.ItemWhitelist,
            tag: tag,
            condition: data.AddOption(new OptionNotOperation(randomizationEnabled))
        ));

        data.AddOption(new OptionAddCount(
            target: Option.eDictTarget.StartVouchers,
            tag: tag,
            count: unlockRange,
            condition: randomizationEnabled
        ));

        OptionID earlyRange = data.AddOption(new OptionRange(
            displayName: "Early Lobby Slots",
            description:
                "The number of lobby slots which are guaranteed to randomize into locations which can be collected"
                + " before any player collects any items. You may specify -1 to enable this for all randomized slots."
                + Option.EARLY_WARNING_SUFFIX,
            category: LOBBY_SLOT_OPTION_CATEGORY,
            categorySort: sort,
            defaultValue: 0,
            condition: randomizationEnabled,
            min: -1,
            max: SNet.Slots.CharacterSlots.Count - 1
        ));

        data.AddOption(new OptionAddCount(
            target: Option.eDictTarget.EarlyItems,
            tag: tag,
            count: earlyRange,
            condition: randomizationEnabled
        ));
    }

    /// <summary>
    /// Prevent changes to the lobby slot if it hasn't been unlocked
    /// </summary>
    [ArchivePatch(typeof(SNet_PlayerSlotManager), nameof(SNet_PlayerSlotManager.SetSlotPermission))]
    public static class SNet_PlayerSlotManager__GetSlotPermission__Patch
    {
        public static bool Prefix(SNet_PlayerSlotManager __instance, int playerIndex)
        {
            if (playerIndex == 0) return true;

            StateTracker stateTracker = StateTracker.Get();
            Game.Data data = stateTracker.MidManager.GetProcessedGameData();

            ItemID item = data.Item_LobbySlotUnlock_Instance(playerIndex);
            if (stateTracker.CollectedItemCounts.GetValueOrDefault(item, 0) > 0) return true;
            if (!data.Items.LookUpValueChecked(item).RandData.CanBeRandomized) return true;

            __instance.m_playerSlotPermissions[playerIndex] = SNet_PlayerSlotManager.SlotPermission.Forbidden;
            return false;
        }
    }

    /// <summary>
    /// Grey out the lobby slot after setting up the lobby page
    /// </summary>
    [ArchivePatch(typeof(CM_PlayerLobbyBar), nameof(CM_PlayerLobbyBar.SetupFromPage))]
    public static class CM_PlayerLobbyBar__SetupSlot__Patch
    {
        public static void Postfix(CM_PlayerLobbyBar __instance, int pillarIndex)
        {
            if (pillarIndex == 0) return;

            StateTracker stateTracker = StateTracker.Get();
            Game.Data data = stateTracker.MidManager.GetProcessedGameData();

            ItemID item = data.Item_LobbySlotUnlock_Instance(pillarIndex);
            Item instance = data.Items.LookUpValueChecked(item);
            if (stateTracker.CollectedItemCounts.GetValueOrDefault(item, 0) > 0) return;
            if (!instance.RandData.CanBeRandomized) return;

            instance.OnItemLost(stateTracker, item);
        }
    }
}
