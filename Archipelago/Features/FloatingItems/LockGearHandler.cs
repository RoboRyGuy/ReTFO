using GameData;
using Gear;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.FloatingItems;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using ReTFO.Archipelago.Utilities;
using System.Linq;

public static class LockGearHandler_Tags
{ 
    extension (Game.Data data)
    {
        public TagResolver Tag_GearItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Gear Items", "All equippable gear items, including weapons, primaries, specials, tools, and the hacking tool.", gd.Tag_OptionalItems));

        /// <summary>
        /// Get a tag for a specific gear item by its item datablock persistent ID.
        /// In the offline player data, this is component '3', or 'eGearComponent.BaseItem`.
        /// </summary>
        /// <exception cref="ArgumentException">The provided ID does not correspond to a gear type</exception>
        public TagResolver Tag_GearItems_ByType(uint itemBaseID)
            => itemBaseID switch
            {
                53 => data.Tag_HackingTool,

                100 => data.Tag_MeleeSledgehammerItems,
                161 => data.Tag_MeleeKnifeItems,
                162 => data.Tag_MeleeSpearItems,
                163 => data.Tag_MeleeBatItems,

                108 => data.Tag_PrimaryGuns,
                156 => data.Tag_PrimaryShotgun,

                109 => data.Tag_SpecialGuns,
                110 => data.Tag_SpecialShotgun,

                28 => data.Tag_BiotrackerItems,
                37 => data.Tag_MineDeployerItems,
                73 => data.Tag_CFoamItems,
                97 => data.Tag_SentryItems,

                _ => throw new ArgumentException($"Item {ItemDataBlock.GetBlock(itemBaseID)?.publicName ?? "NULL"} is not a gear item type!")
            };

        public TagResolver Tag_HackingTool // Note: Randomizing this always throws an error, though the game does continue to work fine
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Hacking Tool", "Special gear category for the sentry gun", gd.Tag_Never));

        public TagResolver Tag_MeleeItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Melee Gear Items", "All equippable gear items in the melee slot", gd.Tag_GearItems));

        public TagResolver Tag_MeleeSledgehammerItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Melee Sledgehammer Items", "All melee gear considered to be a sledgehammer", gd.Tag_MeleeItems));

        public TagResolver Tag_MeleeKnifeItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Melee Knife Items", "All melee gear considered to be a knife", gd.Tag_MeleeItems));

        public TagResolver Tag_MeleeSpearItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Melee Spear Items", "All melee gear considered to be a spear", gd.Tag_MeleeItems));

        public TagResolver Tag_MeleeBatItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Melee Bat Items", "All melee gear considered to be a bat", gd.Tag_MeleeItems));

        public TagResolver Tag_PrimaryItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Primary Gear Items", "All equippable gear items in the primary slot", gd.Tag_GearItems));

        public TagResolver Tag_PrimaryGuns
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Primary Gun Items", "Equippable items in the primary slot which are considered guns", gd.Tag_PrimaryItems));

        public TagResolver Tag_PrimaryShotgun
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Primary Shotgun Items", "Equippable items in the primary slot which are considered shotguns", gd.Tag_PrimaryItems));

        public TagResolver Tag_SpecialItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Special Gear Items", "All equippable gear items in the special slot", gd.Tag_GearItems));

        public TagResolver Tag_SpecialGuns
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Special Gun Items", "Equippable items in the special slot which are considered guns", gd.Tag_SpecialItems));

        public TagResolver Tag_SpecialShotgun
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Special Shotgun Items", "Equippable items in the special slot which are considered shotguns", gd.Tag_SpecialItems));

        public TagResolver Tag_ToolItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Tool Gear Items", "All equippable gear items in the tool slot (also known as the class slot)", gd.Tag_GearItems));

        public TagResolver Tag_BiotrackerItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Biotracker Items", "All equippable gear items in the tool slot considered to be a biotracker", gd.Tag_ToolItems));

        public TagResolver Tag_MineDeployerItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Mine Deployer Items", "All equippable gear items in the tool slot considered to be a mine deployer", gd.Tag_ToolItems));

        public TagResolver Tag_CFoamItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("C-foam Items Items", "All equippable gear items in the tool slot considered to be a CFoam launcher", gd.Tag_ToolItems));

        public TagResolver Tag_SentryItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Sentry Items", "All equippable gear items in the tool slot considered to be a deployable sentry", gd.Tag_ToolItems));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class LockGearHandler : ArchipelagoFeature
{
    public override string Name => "Lock Gear Handler";
    public override string Description
        => "Disables player gear items and adds floating items which re-enables them";
    public override FeatureGroup Group => FeatureGroups.FloatingHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private class GearItemUnlock : Item
    {
        public GearItemUnlock(Game.Data data, PlayerOfflineGearDataBlock block)
            : base(MakeTag(data, block), MakeRandData())
        {
            Block = block;
        }

        public static TagResolver MakeTag(Game.Data data, PlayerOfflineGearDataBlock block)
        {
            GearIDRange idRange = new(block.GearJSON);
            string name = idRange.PublicGearName.ToString();

            GearCategoryDataBlock category = GearCategoryDataBlock.GetBlock(idRange.GetCompID(eGearComponent.Category));
            if (category != null)
            {
                name = category.PublicName.ToString();

                ArchetypeDataBlock archetype = ArchetypeDataBlock.GetBlock(
                    idRange.GetCompID(eGearComponent.FireMode) switch
                    {
                        (uint)eWeaponFireMode.Auto                 => category.AutoArchetype,
                        (uint)eWeaponFireMode.Burst                => category.BurstArchetype,
                        (uint)eWeaponFireMode.Semi                 => category.SemiArchetype,
                        (uint)eWeaponFireMode.SemiBurst            => category.SemiBurstArchetype,

                        // I believe these are statically set?
                        (uint)eWeaponFireMode.SentryGunAuto        => 6,  // Burst Sentry
                        (uint)eWeaponFireMode.SentryGunBurst       => 24, // HEL Auto Sentry
                        (uint)eWeaponFireMode.SentryGunSemi        => 23, // Sniper Sentry
                        (uint)eWeaponFireMode.SentryGunShotgunSemi => 25, // Shotgun Sentry

                        // 0 is never used
                        _ => 0u,
                    }
                );
                if (archetype != null)
                    name = archetype.PublicName.ToString();
            }

            name = $"{name} (Gear #{block.persistentID})";
            return new TagResolver(data, gd => gd.LookupOrCreateTag(name, "A specific gear item", gd.Tag_GearItems_ByType(idRange.GetCompID(eGearComponent.BaseItem))));
        }

        public static ItemData MakeRandData() => new ItemData() { IsUseful = true, CollectedByDefault = true };

        public PlayerOfflineGearDataBlock Block { get; set; }

        const eGearComponent INJECTED_COMP_VALUE = eGearComponent.None;

        private static void AddGear(StateTracker stateTracker, PlayerOfflineGearDataBlock block)
        {
            GearIDRange ids = new(block.GearJSON);
            ids.SetCompID(INJECTED_COMP_VALUE, block.persistentID);
            int slot = (int)ItemDataBlock.GetBlock(ids.GetCompID(eGearComponent.BaseItem)).inventorySlot;

            // Checking if it's a single existing entry and, if so, checking if that was granted to prevent softlocks
            // We'll store the result until after we add an item so it doesn't just get re-added
            GearIDRange? gearToRemove = null;
            if (GearManager.Current.m_gearPerSlot[slot].Count == 1)
            {   
                uint blockId = GearManager.Current.m_gearPerSlot[slot][0].GetCompID(INJECTED_COMP_VALUE);
                if (blockId != 0)
                {
                    var gearItem = GetGearItem(stateTracker.MidManager.GetProcessedGameData(), PlayerOfflineGearDataBlock.GetBlock(blockId));
                    int expectedCount = stateTracker.CollectedItemCounts.GetValueOrDefault(gearItem.ID, 0);
                    if (expectedCount == 0)
                        gearToRemove = GearManager.Current.m_gearPerSlot[slot][0];
                    else if (blockId == block.persistentID && expectedCount == 1)
                        return; // Funny edge case
                }
            }

            // Helper which finds the index of a datablock in the playerofflinegear list
            int findIndex(uint datablockId)
            {
                int count = 0;
                foreach (var item in PlayerOfflineGearDataBlock.GetAllBlocks())
                    if (item.persistentID == datablockId) return count;
                    else ++count;
                return count;
            }

            // Find the index of this item in the list and insert it
            int thisIndex = findIndex(block.persistentID);
            bool placed = false;
            for (int i = 0; i < GearManager.Current.m_gearPerSlot[slot].Count; i++)
            {
                if (thisIndex < findIndex(GearManager.Current.m_gearPerSlot[slot][i].GetCompID(INJECTED_COMP_VALUE)))
                {
                    GearManager.Current.m_gearPerSlot[slot].Insert(i, ids);
                    placed = true;
                    break;
                }
            }
            if (!placed)
                GearManager.Current.m_gearPerSlot[slot].Add(ids);

            // If we queued that item for removal earlier, we do so now
            if (gearToRemove != null) RemoveGear(gearToRemove);
        }

        private static void RemoveGear(GearIDRange ids)
        {
            InventorySlot slot = ItemDataBlock.GetBlock(ids.GetCompID(eGearComponent.BaseItem)).inventorySlot;
            var gearList = GearManager.Current.m_gearPerSlot[(int)slot];

            int i;
            for (i = 0; i < gearList.Count; i++)
                if (gearList[i].IsEqual(ids)) break;

            if (i != gearList.Count)
                gearList.RemoveAt(i);
            else
                FeatureLogger.Warning("Failed to remove gear item during OnItemLost");

            // If no gear is left in the list, find and add the first applicable gear item
            if (gearList.Count == 0)
            {
                FeatureLogger.Warning($"Removed all player gear from slot {Enum.GetName(slot)}; restoring first entry to prevent bugs...");
                GearIDRange? newGear = null;
                foreach (var offlineBlock in PlayerOfflineGearDataBlock.GetAllBlocks())
                {
                    if (offlineBlock.Type == eOfflineGearType.None) continue;
                    if (offlineBlock.Type == eOfflineGearType.SpawnedInLevel) continue;

                    newGear = new(offlineBlock.GearJSON);
                    if (ItemDataBlock.GetBlock(newGear.GetCompID(eGearComponent.BaseItem)).inventorySlot == slot)
                    {
                        newGear.SetCompID(INJECTED_COMP_VALUE, offlineBlock.persistentID);
                        break;
                    }
                    else
                        newGear = null;
                }

                if (newGear == null)
                {
                    FeatureLogger.Error($"Somehow did not find a replacment gear item!");
                    newGear = ids;
                }

                gearList.Add(newGear);
            }

            // Check for players with this item equipped and overwrite if if it is
            if (SNetwork.SNet.LobbyPlayers.Count == 0) // Not in a lobby? Most likely
            {
                if (PlayerBackpackManager.LocalBackpack.Slots[(int)slot].GearIDRange.IsEqual(ids))
                    PlayerBackpackManager.EquipLocalGear(gearList[0]);
            }
            else foreach (var player in SNetwork.SNet.LobbyPlayers)
            {
                if (!player.IsLocal) continue;
                PlayerBackpack backpack = PlayerBackpackManager.GetBackpack(player);
                if (backpack.Slots[(int)slot].GearIDRange.IsEqual(ids))
                    PlayerBackpackManager.Current.EquipSyncGear((InventorySlot)slot, gearList[0], player);
            }
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            AddGear(stateTracker, Block);
            stateTracker.AddItemToTerminal(this); // So players can equip it without leaving the level
        }

        public override void OnItemLost(StateTracker stateTracker)
        {
            GearIDRange ids = new(Block.GearJSON);
            RemoveGear(ids);
            ids.SetCompID(INJECTED_COMP_VALUE, Block.persistentID); // Now that it's added, we can safely inject this
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            var player = terminal.m_localInteractionSource.Owner;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving your gear...", 2f);
            };

            yield return () =>
            {
                if (!PlayerBackpackManager.TryGetBackpack(player, out PlayerBackpack pack))
                {
                    FeatureLogger.Error("Failed to get player backpack while giving gear!");
                    terminal.AddLine("<#F00>Failed to retrieve your gear! It has been re-added to terminal system.</color>");
                }

                GearIDRange ids = new(Block.GearJSON);
                pack.SpawnAndEquipGearAsync(
                    ItemDataBlock.GetBlock(ids.GetCompID(eGearComponent.BaseItem)).inventorySlot,
                    ids,
                    new Action<BackpackItem>((backpackItem) =>
                    {
                        ItemEquippable? weapon = backpackItem.Instance.TryCast<ItemEquippable>();
                        if (weapon == null) return; // This is probably safe
                        if (weapon.AmmoType != AmmoType.Standard
                            && weapon.AmmoType != AmmoType.Special
                            && weapon.AmmoType != AmmoType.Class
                        ) return;

                        float desiredAmmo = .5f * (weapon.AmmoType switch { AmmoType.Standard => 460f, AmmoType.Special => 230f, AmmoType.Class => 150f, _ => throw new ArgumentException() });
                        if (pack.AmmoStorage.GetAmmoInPack(weapon.AmmoType) < desiredAmmo)
                            pack.AmmoStorage.SetAmmo(weapon.AmmoType, desiredAmmo);

                        BulletWeapon? gun = weapon.TryCast<BulletWeapon>();
                        if (gun != null) gun.SetCurrentClipRel(1f);
                    })
                );
                terminal.AddLine($"Gear item \"{Block.name}\" has been given to {player.NickName}");
            };
        }
    }

    public static KeyedItem GetGearItem(Game.Data data, PlayerOfflineGearDataBlock block)
    {
        if (data.TryLookupItem(GearItemUnlock.MakeTag(data, block), out var item))
            return item;

        Item newItem = new GearItemUnlock(data, block);
        return new(data.AddItem(newItem), newItem);
    }

    [Game.Callback]
    public void AddGearItems(Game.Data data)
    {
        foreach (var block in PlayerOfflineGearDataBlock.GetAllBlocks())
        {
            if (block.Type == eOfflineGearType.None) continue;
            if (block.Type == eOfflineGearType.SpawnedInLevel) continue;
            if (!block.internalEnabled) continue;
            data.AddFloatingItem(GetGearItem(data, block).ID);
        }
    }

    /// <summary>
    /// Prevent bots from equipping invalid gear.
    /// Typically they will equip their last used gear, so this will let us prevent that.
    /// </summary>
    [ArchivePatch(typeof(PlayerBackpackManager), nameof(PlayerBackpackManager.EquipBotGear), [ typeof(SNetwork.SNet_Player), typeof(GearIDRange) ])]
    public static class PlayerBackpackManager__EquipBotGear__Patch
    {
        public static void Prefix(ref GearIDRange gearSetup)
        {
            GearIDRange localGear = gearSetup;
            ItemDataBlock itemData = ItemDataBlock.GetBlock(gearSetup.GetCompID(eGearComponent.BaseItem));
            if (!GearManager.Current.m_gearPerSlot[(int)itemData.inventorySlot].Any(g => g.IsEqual(localGear)))
                gearSetup = GearManager.Current.m_gearPerSlot[(int)itemData.inventorySlot].First();
        }
    }
}
