using CellMenu;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using SNetwork;
using System;
using System.Collections.Generic;
using TMPro;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.FloatingItems;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class LockLobbySlotsHandler_Tags
{
    extension(Game.Data data)
    {
        public TagResolver Tag_LobbySlotUnlocks
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Unlock Lobby Slot Items", "Items which unlock more lobby slots", gd.Tag_OptionalItems));
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

    private class LobbySlotUnlockItem : Item
    {
        public LobbySlotUnlockItem(Game.Data data, int index)
            : base(MakeTag(data, index), MakeRandData())
        {
            Index = index;
        }

        public static TagResolver MakeTag(Game.Data data, int index)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"Lobby Slot #{index} Unlock", "Item which unlocks a particular lobby slot", gd.Tag_LobbySlotUnlocks));

        public static ItemData MakeRandData() => new ItemData() { IsUseful = true, IsCollectedByDefault = true };

        public int Index { get; set; }

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

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player = null)
        {
            // Make the permissions button work again
            UnlockButtonNow();
        }

        public override void OnItemLost(StateTracker stateTracker)
        {
            // Try and kick the current player in the slot
            SNet.Slots.SetSlotPermission(Index, SNet_PlayerSlotManager.SlotPermission.Forbidden);
            SNet_Player player = SNet.Slots.CharacterSlots[Index].player;
            if (player != null) SNet.Slots.RemoveFromPlayersInGame(player);

            // Lock out the permissions button
            LockButtonNow();
        }
    }

    public static KeyedItem GetSlotUnlockItem(Game.Data data, int index)
    {
        if (data.TryLookupItem(LobbySlotUnlockItem.MakeTag(data, index), out var item))
            return item;

        Item newItem = new LobbySlotUnlockItem(data, index);
        return new(data.AddItem(newItem), newItem);
    }

    [Game.Callback]
    public void AddSlotUnlockItems(Game.Data data)
    {
        for (int i = 1; i < SNet.Slots.CharacterSlots.Count; i++)
        {
            KeyedItem slotUnlock = GetSlotUnlockItem(data, i);
            data.AddFloatingItem(slotUnlock.ID);
        }
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

            if (!data.TryLookupItem(LobbySlotUnlockItem.MakeTag(data, playerIndex), out KeyedItem item))
            {
                FeatureLogger.Error("Failed to lookup slot unlock item; allowing slot modifications!");
                return true;
            }

            if (stateTracker.CollectedItemCounts.GetValueOrDefault(item.ID, 0) > 0) return true;
            if (!item.Item.RandMode.IsTreatedAsRandom) return true;

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

            if (!data.TryLookupItem(LobbySlotUnlockItem.MakeTag(data, pillarIndex), out KeyedItem item))
            {
                FeatureLogger.Error("Failed to lookup slot unlock item; not locking slot button!");
                return;
            }

            if (stateTracker.CollectedItemCounts.GetValueOrDefault(item.ID, 0) > 0) return;
            if (!item.Item.RandMode.IsTreatedAsRandom) return;

            item.OnItemLost(stateTracker);
        }
    }
}
