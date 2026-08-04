using GameData;
using Gear;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using SNetwork;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.FloatingItems;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class LockGearHandler_Tags
{ 
    extension (Game.Data data)
    {
        public ItemID Item_Gear
            => ItemID.From(data, "Gear Items", data => new("All equippable gear items, including weapons, primaries, specials, tools, and the hacking tool.", data.Item_All));

        public ItemID Item_Gear_Instance(GearIDRange idRange)
            => ItemID.From(
                data,
                LockGearHandler.FormatName(idRange),
                data => new("A specific gear item", 
                data.Item_Gear_ByType(idRange.GetCompID(eGearComponent.BaseItem))), 
                new LockGearHandler.GearItemUnlock(idRange)
            );

        /// <summary>
        /// Get a tag for a specific gear item by its item datablock persistent ID.
        /// In the offline player data, this is component '3', or 'eGearComponent.BaseItem`.
        /// </summary>
        /// <exception cref="ArgumentException">The provided ID does not correspond to a gear type</exception>
        public ItemID Item_Gear_ByType(uint itemBaseID)
            => itemBaseID switch
            {
                53 => data.Item_HackingTool,

                100 => data.Item_MeleeSledgehammers,
                161 => data.Item_MeleeKnives,
                162 => data.Item_MeleeSpears,
                163 => data.Item_MeleeBats,

                108 => data.Item_PrimaryGuns,
                156 => data.Item_PrimaryShotguns,

                109 => data.Item_SpecialGuns,
                110 => data.Item_SpecialShotguns,

                28 => data.Item_Biotrackers,
                37 => data.Item_MineDeployers,
                73 => data.Item_CFoamers,
                97 => data.Item_Sentries,

                _ => throw new ArgumentException($"Item {ItemDataBlock.GetBlock(itemBaseID)?.publicName ?? "NULL"} is not a gear item type!")
            };

        /// <summary>
        /// Geta tag for a specific inventory slot, assuming such a tag is defined
        /// </summary>
        public ItemID Item_Gear_BySlot(InventorySlot slot)
            => slot switch
            {
                InventorySlot.HackingTool => data.Item_HackingTool,
                InventorySlot.GearMelee => data.Item_Melees,
                InventorySlot.GearStandard => data.Item_Primaries,
                InventorySlot.GearSpecial => data.Item_Specials,
                InventorySlot.GearClass => data.Item_Tools,
                _ => throw new ArgumentException($"{slot} is not a recognized invetory slot!")
            };

        public ItemID Item_HackingTool // Note: Randomizing this always throws an error, though the game does continue to work fine
            => ItemID.From(data, "Hacking Tool", data => new("Special gear category for the hacking tool", data.Item_Never));

        public ItemID Item_Melees
            => ItemID.From(data, "Melee Gear Items", data => new("All equippable gear items in the melee slot", data.Item_Gear));

        public ItemID Item_MeleeSledgehammers
            => ItemID.From(data, "Melee Sledgehammer Items", data => new("All melee gear considered to be a sledgehammer", data.Item_Melees));

        public ItemID Item_MeleeKnives
            => ItemID.From(data, "Melee Knife Items", data => new("All melee gear considered to be a knife", data.Item_Melees));

        public ItemID Item_MeleeSpears
            => ItemID.From(data, "Melee Spear Items", data => new("All melee gear considered to be a spear", data.Item_Melees));

        public ItemID Item_MeleeBats
            => ItemID.From(data, "Melee Bat Items", data => new("All melee gear considered to be a bat", data.Item_Melees));

        public ItemID Item_Primaries
            => ItemID.From(data, "Primary Gear Items", data => new("All equippable gear items in the primary slot", data.Item_Gear));

        public ItemID Item_PrimaryGuns
            => ItemID.From(data, "Primary Gun Items", data => new("Equippable items in the primary slot which are considered guns", data.Item_Primaries));

        public ItemID Item_PrimaryShotguns
            => ItemID.From(data, "Primary Shotgun Items", data => new("Equippable items in the primary slot which are considered shotguns", data.Item_PrimaryGuns));

        public ItemID Item_Specials
            => ItemID.From(data, "Special Gear Items", data => new("All equippable gear items in the special slot", data.Item_Gear));

        public ItemID Item_SpecialGuns
            => ItemID.From(data, "Special Gun Items", data => new("Equippable items in the special slot which are considered guns", data.Item_Specials));

        public ItemID Item_SpecialShotguns
            => ItemID.From(data, "Special Shotgun Items", data => new("Equippable items in the special slot which are considered shotguns", data.Item_Specials));

        public ItemID Item_Tools
            => ItemID.From(data, "Tool Gear Items", data => new("All equippable gear items in the tool slot (also known as the class slot)", data.Item_Gear));

        public ItemID Item_Biotrackers
            => ItemID.From(data, "Biotracker Items", data => new("All equippable gear items in the tool slot considered to be a biotracker", data.Item_Tools));

        public ItemID Item_MineDeployers
            => ItemID.From(data, "Mine Deployer Items",data => new( "All equippable gear items in the tool slot considered to be a mine deployer", data.Item_Tools));

        public ItemID Item_CFoamers
            => ItemID.From(data, "C-foam Items Items", data => new("All equippable gear items in the tool slot considered to be a CFoam launcher", data.Item_Tools));

        public ItemID Item_Sentries
            => ItemID.From(data, "Sentry Items", data => new("All equippable gear items in the tool slot considered to be a deployable sentry", data.Item_Tools));
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

    public const string GEAR_OPTION_CATEGORY = "Gear";

    public class GearItemUnlock : Item
    {
        public GearItemUnlock(GearIDRange idRange)
            : base(MakeRandData())
        {
            IDRange = idRange;
        }

        public static ItemData MakeRandData() => new ItemData() { IsUseful = true, IsCollectedByDefault = true };

        public GearIDRange IDRange { get; private init; }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
        {
            AddGear(stateTracker, IDRange, itemId);
            stateTracker.AddItemToTerminal(itemId); // So players can equip it without leaving the level
        }

        public override void OnItemLost(StateTracker stateTracker, ItemID itemId)
        {
            RemoveGear(IDRange);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            var player = terminal.m_syncedInteractionSource.Owner;

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

                SetPlayerGear(player, IDRange);
                terminal.AddLine($"Gear item \"{IDRange.PublicGearName}\" has been given to {player.NickName}");
            };
        }
    }

    /// <summary>
    /// Register gear items and options with gameData
    /// </summary>
    [Game.Callback]
    public void AddGearItems(Game.Data data)
    {
        SortedList<InventorySlot, OptionChoice> choiceOptions = new();

        foreach (var block in PlayerOfflineGearDataBlock.GetAllBlocks())
        {
            if (block.Type == eOfflineGearType.None) continue;
            if (block.Type == eOfflineGearType.SpawnedInLevel) continue;
            if (!block.internalEnabled) continue;

            ItemID gearItem = data.Item_Gear_Instance(new GearIDRange(block.GearJSON));
            data.AddFloatingItem(data.Region_Menu, gearItem);

            // Skip items in categories which cannot be randomized, so their options don't get generated
            if (data.Items.IsChild(gearItem, data.Item_Never))
                continue;

            GearIDRange idRange = new(block.GearJSON);
            ItemDataBlock baseItem = ItemDataBlock.GetBlock(idRange.GetCompID(eGearComponent.BaseItem));

            if (!choiceOptions.TryGetValue(baseItem.inventorySlot, out OptionChoice? choice))
            {
                choice = MakeOptionsForSlot(data, baseItem.inventorySlot);
                choiceOptions.Add(baseItem.inventorySlot, choice);
            }

            choice.ChoiceNames.Add(data.Items.LookUpName(gearItem));
            choice.ChoiceValues.Add(gearItem.ID);
        }
    }

    /// <summary>
    /// Create the set of options for a specific inventory slot
    /// </summary>
    /// <param name="data">Game data to generate for</param>
    /// <param name="slot">The slot to generate for</param>
    /// <returns>The choice option from the generated slots, so choices can be added.</returns>
    private static OptionChoice MakeOptionsForSlot(Game.Data data, InventorySlot slot)
    {
        OptionChoice result; // Will be set during creation

        string slotName = slot switch
        {
            InventorySlot.GearMelee => "Melee Gear",
            InventorySlot.GearStandard => "Primary Gear",
            InventorySlot.GearSpecial => "Special Gear",
            InventorySlot.GearClass => "Tool Gear",
            _ => $"{Enum.GetName(slot)} Gear"
        };
        ItemID slotTag = data.Item_Gear_BySlot(slot);
        uint[] slotSort = Option.MakeSortKey(data, slotTag);

        OptionID unlockRange = data.AddOption(new OptionRange(
            displayName: $"Number of Unlocked {slotName}",
            description:
                $"Randomly selects the chosen number of {slotName.ToLower()} and adds it to your starting inventory."
                + " You may also set this to -1 to start with all gear in this slot unlocked."
                + " GTFO does not support zero gear items in a slot; ensure at least one item is selected here or below.",
            category: GEAR_OPTION_CATEGORY,
            categorySort: slotSort,
            defaultValue: 1,
            condition: new(),
            min: -1,
            max: 99
        ));
        OptionID randomizationEnabled = data.AddOption(new OptionDoesNotEqualOperation(unlockRange, -1));

        data.AddOption(new OptionAddToSet(
            target: Option.eSetTarget.ItemWhitelist,
            tag: slotTag,
            condition: randomizationEnabled
        ));
        data.AddOption(new OptionAddToSet(
            target: Option.eSetTarget.ItemBlacklist,
            tag: slotTag,
            condition: data.AddOption(new OptionNotOperation(randomizationEnabled))
        ));

        data.AddOption(new OptionAddCount(
            target: Option.eDictTarget.StartVouchers,
            tag: slotTag,
            count: unlockRange,
            condition: randomizationEnabled
        ));

        OptionID choice = data.AddOption(result = new OptionChoice(
            displayName: $"Starting {slotName}",
            description:
                "The chosen gear item is guaranteed to be unlocked at start (in addition to the randomly-selected gear items)."
                + " You may also choose \"None\" to not add an item.",
            category: GEAR_OPTION_CATEGORY,
            categorySort: slotSort,
            defaultValue: new ItemID().ID,
            condition: randomizationEnabled,
            choiceNames: new() { "None" },
            choiceValues: new() { new ItemID().ID }
        ));

        data.AddOption(new OptionAddToSet(
            target: Option.eSetTarget.ItemBlacklist,
            tag: choice,
            condition: randomizationEnabled
        ));

        OptionID earlyRange = data.AddOption(new OptionRange(
            displayName: $"Early {slotName}",
            description:
                $"Randomly selects the chosen amount of {slotName.ToLower()} and adds it to the early items list," 
                + " ensuring it spawns somewhere it can be reached before any player picks up any item."
                + Option.EARLY_WARNING_SUFFIX,
            category: GEAR_OPTION_CATEGORY,
            categorySort: slotSort,
            defaultValue: 0,
            condition: randomizationEnabled,
            min: 0,
            max: 99
        ));

        data.AddOption(new OptionAddCount(
            target: Option.eDictTarget.EarlyItems,
            tag: slotTag,
            count: earlyRange,
            condition: randomizationEnabled
        ));

        return result;
    }

    /// <summary>
    /// Create the item name for a specific gear range
    /// </summary>
    public static string FormatName(GearIDRange idRange)
    {
        string name = idRange.PublicGearName.ToString();

        GearCategoryDataBlock category = GearCategoryDataBlock.GetBlock(idRange.GetCompID(eGearComponent.Category));
        if (category != null)
        {
            name = category.PublicName.ToString();

            ArchetypeDataBlock archetype = ArchetypeDataBlock.GetBlock(
                idRange.GetCompID(eGearComponent.FireMode) switch
                {
                    (uint)eWeaponFireMode.Auto => category.AutoArchetype,
                    (uint)eWeaponFireMode.Burst => category.BurstArchetype,
                    (uint)eWeaponFireMode.Semi => category.SemiArchetype,
                    (uint)eWeaponFireMode.SemiBurst => category.SemiBurstArchetype,

                    // I believe these are statically set?
                    (uint)eWeaponFireMode.SentryGunAuto => 57, // HEL Auto Sentry
                    (uint)eWeaponFireMode.SentryGunBurst => 55, // Burst Sentry 
                    (uint)eWeaponFireMode.SentryGunSemi => 54, // Sniper Sentry
                    (uint)eWeaponFireMode.SentryGunShotgunSemi => 58, // Shotgun Sentry

                    // 0 is never used, so it should return a null archetype
                    _ => 0u,
                }
            );
            if (archetype != null)
                name = archetype.PublicName.ToString();
        }

        return $"{name} ({idRange.PublicGearName})";
    }

    /// <summary>
    /// Add gear to GearManager. Checks if the existing gear should be there and removes it if necessary
    /// </summary>
    /// <param name="stateTracker">Current state tracker</param>
    /// <param name="idRange">ID range of the gear to add</param>
    /// <param name="itemId">ID of the gear range being added, if available</param>
    private static void AddGear(StateTracker stateTracker, GearIDRange idRange, ItemID itemId = new())
    {
        Game.Data data = stateTracker.GameData;

        if (itemId.IsNull)
            data.Items.TryLookUpID(FormatName(idRange), out itemId);

        InventorySlot slot = ItemDataBlock.GetBlock(idRange.GetCompID(eGearComponent.BaseItem)).inventorySlot;
        var slotList = GearManager.Current.m_gearPerSlot[(int)slot];

        // Checking if it's a single existing entry and, if so, checking if that was granted to prevent softlocks
        // We'll store the result until after we add an item so it doesn't just get re-added
        GearIDRange? gearToRemove = null;
        if (slotList.Count == 1)
        {
            if (data.Items.TryLookUpID(FormatName(slotList[0]), out ItemID testId))
            {
                int expectedCount = stateTracker.CollectedItemCounts.GetValueOrDefault(testId, 0);
                if (expectedCount == 0)
                    gearToRemove = slotList[0];
                else if (testId.Equals(itemId) && expectedCount == 1)
                    return; // Funny edge case
            }
        }

        // Find the index of this item in the list and insert it
        uint thisIndex = itemId.IsNull ? uint.MaxValue : itemId.ID;
        bool placed = false;
        for (int i = 0; i < slotList.Count; i++)
        {
            uint thatIndex;
            if (data.Items.TryLookUpID(FormatName(slotList[i]), out ItemID thatId))
                thatIndex = thatId.ID;
            else
                thatIndex = uint.MaxValue;

            if (thisIndex < thatIndex)
            {
                slotList.Insert(i, idRange);
                placed = true;
                break;
            }
        }
        if (!placed) slotList.Add(idRange);
            
        // If we queued that item for removal earlier, we do so now
        if (gearToRemove != null) RemoveGear(gearToRemove);
    }

    /// <summary>
    /// Remove one copy of gear from the GearManager. 
    /// Checks if another copy exists and, if not, checks if players have the gear equipped 
    ///  and de-equips it for them if needed
    /// </summary>
    /// <param name="idRange">ID range of the gear to remove</param>
    private static void RemoveGear(GearIDRange idRange)
    {
        InventorySlot slot = ItemDataBlock.GetBlock(idRange.GetCompID(eGearComponent.BaseItem)).inventorySlot;
        var slotList = GearManager.Current.m_gearPerSlot[(int)slot];

        int gearIndex;
        for (gearIndex = 0; gearIndex < slotList.Count; gearIndex++)
            if (slotList[gearIndex].IsEqual(idRange)) break;

        if (gearIndex != slotList.Count)
            slotList.RemoveAt(gearIndex);
        else
            FeatureLogger.Warning("Failed to remove gear item during OnItemLost");

        // If no gear is left in the list, find and add the first applicable gear item
        if (slotList.Count == 0)
        {
            FeatureLogger.Warning($"Removed all player gear from slot {Enum.GetName(slot)}; restoring first entry to prevent bugs...");
            GearIDRange? newGear = null;
            foreach (var offlineBlock in PlayerOfflineGearDataBlock.GetAllBlocks())
            {
                if (offlineBlock.Type == eOfflineGearType.None) continue;
                if (offlineBlock.Type == eOfflineGearType.SpawnedInLevel) continue;

                newGear = new(offlineBlock.GearJSON);
                if (ItemDataBlock.GetBlock(newGear.GetCompID(eGearComponent.BaseItem)).inventorySlot == slot)
                    break;
                else
                    newGear = null;
            }

            if (newGear == null)
            {
                FeatureLogger.Error($"Somehow did not find a replacment gear item!");
                newGear = idRange;
            }

            slotList.Add(newGear);
        }
        else
        {
            // Check if any duplicates of this gear item are still in the list
            for (gearIndex = 0; gearIndex < slotList.Count; ++gearIndex)
                if (slotList[gearIndex].IsEqual(idRange)) return;
        }

        // Check for players with this item equipped and overwrite it if it is
        if (SNet.LobbyPlayers.Count == 0) // Not in a lobby?
        {
            if (PlayerBackpackManager.LocalBackpack.Slots[(int)slot].GearIDRange.IsEqual(idRange))
                SetPlayerGear(SNet.LocalPlayer, slotList[0]);
        }
        else foreach (var player in SNet.Slots.SlottedPlayers) // This includes bots, unlike SNet.LobbyPlayers
        {
            if (!(player.IsLocal || (SNet.IsMaster && player.IsBot))) continue;
            PlayerBackpack backpack = PlayerBackpackManager.GetBackpack(player);
            if (backpack.Slots[(int)slot].GearIDRange.IsEqual(idRange))
                SetPlayerGear(player, slotList[0]);
        }
    }

    /// <summary>
    /// Set a player's equipped gear. The slot will be inferred from the gear
    /// </summary>
    /// <param name="player">The player to set the gear for</param>
    /// <param name="desiredGear">The desired gear item for the player</param>
    private static void SetPlayerGear(SNet_Player player, GearIDRange desiredGear)
    {
        FeatureLogger.Debug($"Attempting to give \"{player.NickName}\" gear item \"{desiredGear.PublicGearName}\"");
        if (!(player.IsLocal || (player.IsBot && SNet.IsMaster)))
        {
            FeatureLogger.Debug("Cancelled -> That player is not locally owned!");
            return;
        }

        ItemDataBlock gearItemBlock = ItemDataBlock.GetBlock(desiredGear.GetCompID(eGearComponent.BaseItem));
        InventorySlot slot = gearItemBlock.inventorySlot;
        PlayerBackpack pack = PlayerBackpackManager.GetBackpack(player);
        PlayerAgent? agent = player.PlayerAgent?.Cast<PlayerAgent>();

        if (pack.Slots[(int)slot].Status == eInventoryItemStatus.Deployed && pack.TryGetDeployedItem(slot, out global::Item deployedItem))
        {
            FeatureLogger.Debug("Detected that gear being replaced is deployed. Attempting to pick up as a sentry gun...");
            var sentryGun = deployedItem.TryCast<SentryGunInstance>();
            if (sentryGun == null)
                FeatureLogger.Debug("Failed to pick up sentry gun while changing gear!");
            else
                sentryGun.PickUp(agent);
        }

        if (player.IsBot)
            PlayerBackpackManager.EquipBotGear(player, desiredGear);
        else if (player.IsLocal)
            PlayerBackpackManager.EquipLocalGear(desiredGear);
        else
            PlayerBackpackManager.Current.EquipSyncGear(slot, desiredGear, player);
        global::Item item = pack.Slots[(int)slot].Instance;

        ItemEquippable? weapon = item.TryCast<ItemEquippable>();
        if (weapon == null)
        {
            FeatureLogger.Warning("Failed to identify newly spawned gear as equippable. Skipping relevant calls.");
            return;
        }

        // TODO: Fix UI here

        if (weapon.AmmoType != AmmoType.Standard
            && weapon.AmmoType != AmmoType.Special
            && weapon.AmmoType != AmmoType.Class
        ) return;

        float desiredAmmo = .5f * (weapon.AmmoType switch { AmmoType.Standard => 460f, AmmoType.Special => 230f, AmmoType.Class => 150f, _ => throw new ArgumentException() });
        if (pack.AmmoStorage.GetAmmoInPack(weapon.AmmoType) < desiredAmmo)
            pack.AmmoStorage.SetAmmo(weapon.AmmoType, desiredAmmo);

        // TODO: Check if sentry and give ammo to sentry instead

        BulletWeapon? gun = weapon.TryCast<BulletWeapon>();
        if (gun != null)
            gun.SetCurrentClipRel(1f);
    }

    /// <summary>
    /// Prevent bots from equipping invalid gear.
    /// Typically they will equip their last used gear, so this will let us prevent that.
    /// </summary>
    [ArchivePatch(typeof(PlayerBackpackManager), nameof(PlayerBackpackManager.EquipBotGear), [ typeof(SNet_Player), typeof(GearIDRange) ])]
    public static class PlayerBackpackManager__EquipBotGear__Patch
    {
        /// <summary>
        /// Last gear items successfully equipped by a bot
        /// </summary>
        private static List<List<GearIDRange?>> m_lastEquippedGear = new();

        public static void Prefix(SNet_Player bot, ref GearIDRange gearSetup)
        {
            GearIDRange localGear = gearSetup;
            ItemDataBlock itemData = ItemDataBlock.GetBlock(gearSetup.GetCompID(eGearComponent.BaseItem));
            int botIndex = bot.CharacterSlot.index;
            int slot = (int)itemData.inventorySlot;

            while (m_lastEquippedGear.Count <= botIndex)
                m_lastEquippedGear.Add(new List<GearIDRange?>());
            List<GearIDRange?> botGear = m_lastEquippedGear[botIndex];
            while (botGear.Count <= slot) botGear.Add(null);

            if (!GearManager.Current.m_gearPerSlot[(int)itemData.inventorySlot].Any(g => g.IsEqual(localGear)))
                gearSetup = botGear[slot] ?? GearManager.Current.m_gearPerSlot[(int)itemData.inventorySlot].First();
            botGear[slot] = gearSetup;
        }
    }

    ///// <summary>
    ///// From some reason, the gear limits don't sync right when joining a lobby as a proxy client.
    ///// This will check for that and correct it.
    ///// </summary>
    //[ArchivePatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.OnJoinedLobby))]
    //public static class SNet_SessionHub__OnJoinedLobby__Patch
    //{
    //    public static void Postfix(SNet_Player? player)
    //    {
    //        var func = () =>
    //        {
    //            //if (!(player?.IsLocal ?? false)) return;
    //            //for (int i = 0; i < PlayerBackpackManager.LocalBackpack.Slots.Count; i++)
    //            //{
    //            //    var currentRange = PlayerBackpackManager.LocalBackpack.Slots[i]?.GearIDRange;
    //            //    if (currentRange != null && !GearManager.Current.m_gearPerSlot[i].Any(r => r.Equals(currentRange)))
    //            //        SetPlayerGear(SNet.LocalPlayer, GearManager.Current.m_gearPerSlot[i][0]);
    //            //}
    //        };
    //        func();
    //    }
    //}
}
