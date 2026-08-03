using GameData;
using Il2CppInterop.Runtime.Injection;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Features.Terminals;
using ReTFO.Archipelago.Utilities;
using SNetwork;
using System;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.Pickups;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;


public static class PickupHelper_Tags
{
    extension (Game.Data gameData)
    {
        public LocationID Location_Pickups
            => LocationID.From(gameData, "Pickup Locations", data => new("Locations checked when items are picked up (keys, cells, artifacts, etc)", data.Location_All));

        public LocationID Location_SmallPickups
            => LocationID.From(gameData, "Small Pickup Locations", data => new("Location checked when small items are picked up (keys, IDs, GLPS, etc)", data.Location_Pickups));

        public LocationID Location_BigPickups
            => LocationID.From(gameData, "Big Pickup Locations", data => new("Location checked when big items are picked up (cells, fog turbines, babies, etc)", data.Location_Pickups));

        public LocationID Location_ResourcePickups
            => LocationID.From(gameData, "Resource Pickup Locations", data => new("Location checked when resources are picked up (ammo, med, tool, disinfect)", data.Location_Pickups));

        public LocationID Location_ConsumablePickups
            => LocationID.From(gameData, "Consumable Pickup Locations", data => new("Location checked when consumables are picked up (glowsticks, flashlights, c-foam grenades, etc)", data.Location_Pickups));

        public LocationID Location_ArtifactPickups
            => LocationID.From(gameData, "Artifact Pickup Locations", data => new("Location checked when artifacts are picked up (muted, bold, aggressive)", data.Location_Pickups));


        public ItemID Item_Pickups
            => ItemID.From(gameData, "Pickup Items", data => new("All items which are picked up (keys, cells, artifacts, etc)", data.Item_All));

        public ItemID Item_SmallPickups
            => ItemID.From(gameData, "Small Pickup Items", data => new("All small pickup items which end up in the left-side menu (keys, IDs, GLPS, etc)", data.Item_Pickups));

        public ItemID Item_BigPickups
            => ItemID.From(gameData, "Big Pickup Items", data => new("All big pickup items (cells, fog turbines, babies, etc)", data.Item_Pickups));

        public ItemID Item_ResourcePickups
            => ItemID.From(gameData, "Resource Pickup Items", data => new("All resource pickups (ammo, med, tool, disinfect)", data.Item_Pickups));

        public ItemID Item_ConsumablePickups
            => ItemID.From(gameData, "Consumable Pickup Items", data => new("All consumable pickups (glowsticks, flashlights, c-foam grenades, etc)", data.Item_Pickups));

        public ItemID Item_ArtifactPickups
            => ItemID.From(gameData, "Artifact Pickup Items", data => new("All artifact picksup (muted, bold, aggressive)", data.Item_Pickups));
    }
}

/// <summary>
/// Utility for associating pickups with locations so that those locations get checked when the pickup is grabbed.
/// </summary>
[InjectToIl2Cpp, EnableFeatureByDefault]
public class PickupHelper : ArchipelagoFeature
{
    public override string Name => "Pickups Helper";
    public override string Description 
        => "Provides utilites used by other features to manage pickups (including big pickups)";
    public override FeatureGroup Group => FeatureGroups.PickupHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public const string PICKUPS_OPTION_CATEGORY = "Pickups";

    [Game.Callback]
    public void AddEventScanOptions(Game.Data data)
    {
        ItemID tag;

        tag = data.Item_Pickups;
        data.AddOption(new OptionItemTagOption(
            displayName: "Randomize All Pickups",
            description: "Randomize all supported pickups." + OptionTagOption.DESC_SUFFIX,
            category: PICKUPS_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, tag),
            condition: new(),
            defaultValue: 0,
            tag: tag
        ));

        tag = data.Item_SmallPickups;
        data.AddOption(new OptionItemTagOption(
            displayName: "Randomize Small Pickups",
            description: 
                "Randomize all supported small pickups. This includes colored keys, bulkhead keys, and"
                + " objective pickups (ie IDs)." 
                + OptionTagOption.DESC_SUFFIX,
            category: PICKUPS_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, tag),
            condition: new(),
            defaultValue: 1,
            tag: tag
        ));

        tag = data.Item_BigPickups;
        data.AddOption(new OptionItemTagOption(
            displayName: "Randomize Big Pickups",
            description: 
                "Randomize all supported big pickups. This includes normal spawns and objective spawns,"
                + " but not in-level spawns (if there are any)." 
                + OptionTagOption.DESC_SUFFIX,
            category: PICKUPS_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, tag),
            condition: new(),
            defaultValue: 1,
            tag: tag
        ));
    }

    /// <summary>
    /// Component placed on pickups to mark them as being associated with a location.
    /// </summary>
    [InjectToIl2Cpp]
    private class ContainsLocationPickupComp : MonoBehaviour
    {
        /// <summary>
        /// Required constructor based on IntPtr
        /// </summary>
        public ContainsLocationPickupComp(IntPtr ptr) : base(ptr) { }

        /// <summary>
        /// ID of the location being stored
        /// </summary>
        public LocationID StoredLocation = new();
    }

    /// <summary>
    /// Data injected into a distribution to associate it with a location.
    /// This can later be retrieved when the distributions are built as function markers.
    /// </summary>
    [InjectToIl2Cpp]
    private class This_ConsumablesData : ConsumableDistributionDataBlock
    {
        /// <summary>
        /// Required constructor based on IntPtr
        /// </summary>
        public This_ConsumablesData(IntPtr ptr) : base(ptr) { }

        /// <summary>
        /// Custom constructor which copies original and also stores the new id
        /// </summary>
        public This_ConsumablesData(LocationID id, ConsumableDistributionDataBlock? original) 
            : base(ClassInjector.DerivedConstructorPointer<This_ConsumablesData>())
        {
            ClassInjector.DerivedConstructorBody(this);

            // Grab a random block, since we can't fake being null
            original ??= ConsumableDistributionDataBlock.GetAllBlocks().First();

            StoredLocation = id;
            ChanceToSpawnInResourceContainer = original.ChanceToSpawnInResourceContainer;
            SpawnData = original.SpawnData;
            SpawnsPerZone = original.SpawnsPerZone;
            Index = original.Index;
            internalEnabled = original.internalEnabled;
            name = original.name;
            persistentID = 0; // You can use this to identify injected distribution datas
        }

        /// <summary>
        /// ID of the location associated with the placement
        /// </summary>
        public LocationID StoredLocation = new();
    }

    /// <summary>
    /// Associate a location with a pickup item
    /// </summary>
    /// <param name="item">The item to associate with the location</param>
    /// <param name="locationId">The location to associate</param>
    /// <param name="despawnIfFound">If true, will despawn the pickup if the location has already been checked</param>
    /// <remarks>
    /// This method works by placing a component on the item which can be checked at pickup time.
    /// If the item does not have a pickup sync comp, then this will never be checked.
    /// </remarks>
    public static void AssociateItem(ItemInLevel item, LocationID locationId, bool despawnIfFound=true)
    {
        StateTracker stateTracker = StateTracker.Get();
        Game.Data data = stateTracker.GameData;

        ContainsLocationPickupComp? comp = item.PickupInteraction.GetComponent<ContainsLocationPickupComp>();
        if (comp == null)
            comp = item.PickupInteraction.gameObject.AddComponent<ContainsLocationPickupComp>();
        else if (!comp.StoredLocation.IsNull)
        {
            int locLength = Math.Max(comp.StoredLocation.ID.ToString().Length, locationId.ID.ToString().Length);
            string formatString = new string('0', locLength);
            FeatureLogger.Error(
                $"Overwriting location on pickup!\n"
                + $"  Old Location: [{comp.StoredLocation.ID.ToString(formatString)}] {data.Locations.LookUpName(comp.StoredLocation)}"
                + $"  New Location: [{locationId.ID.ToString(formatString)}] {data.Locations.LookUpName(locationId)}"
            );
        }
        comp.StoredLocation = locationId;

        Location loc = data.Locations.LookUpValueChecked(locationId);
        if (despawnIfFound && loc.RandData.IsTreatedAsRandom && stateTracker.HasLocation(locationId))
        {
            // Try to despawn the item
            if (item.ReplicationWrapper != null)
            {   // We can despawn the item using its dynamic replicator, which is synced
                item.ReplicationWrapper?.Replicator.Despawn();
            }
            else
            {   // The assumption is that this code is invoked by a synced command - ie during the level build
                // This destroys the object for the local client only
                item.internalSync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, null);
                if (item.CanWarp) Dimension.RemoveWarpable(item.Cast<IWarpableObject>());
            }
        }
        else if (loc.RandData.IsRandomized)
        {
            // Set the name on the item to match the name Archipelago gave it
            Interact_Pickup_PickupItem? pickup = item.PickupInteraction.TryCast<Interact_Pickup_PickupItem>();
            if (pickup != null)
            {
                string backupName = data.Items.LookUpName(loc.ItemID);
                pickup.SetName(new Il2CppFunc_string(() => loc.ScoutedItemName ?? backupName));
                pickup.add_OnInteractionSelected(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<PlayerAgent, bool>>(new Action<PlayerAgent, bool>(
                    (_, _) => StateTracker.Get().ScoutLocation(locationId)
                )));
            }
        }
    }

    /// <summary>
    /// Associate a pickup distribution with a location ID so it's checked when the pickup is grabbed.
    /// </summary>
    /// <param name="distribution">The distribution to associate</param>
    /// <param name="locationId">ID of the location</param>
    public static void AssociateDistributionWithLocation(LG_Distribute_PickupItemsPerZone distribution, LocationID locationId)
    {
        if (!ArchipelagoFeatureHelper.GetFeature<PickupHelper>().Enabled)
            return;

        This_ConsumablesData? data = distribution.m_consumableDistributionData?.TryCast<This_ConsumablesData>();
        if (data != null)
        {
            FeatureLogger.Error($"Distribution already has location associated with it! Old ID: {data.StoredLocation}, New ID: {locationId}");
            return;
        }

        distribution.m_consumableDistributionData = new This_ConsumablesData(locationId, distribution.m_consumableDistributionData);
    }

    /// <summary>
    /// Associate a pickup distribution with a location ID so it's checked when the pickup is grabbed.
    /// </summary>
    /// <param name="distribution">The distribution to associate</param>
    /// <param name="locationId">ID of the location</param>
    public static void AssociateDistributionWithLocation(LG_DistributePickUpItem distribution, LocationID locationId)
    {
        if (!ArchipelagoFeatureHelper.GetFeature<PickupHelper>().Enabled)
            return;

        This_ConsumablesData? data = distribution.m_consumableData?.TryCast<This_ConsumablesData>();
        if (data != null)
        {
            FeatureLogger.Error($"Distribution already has location associated with it! Old ID: {data.StoredLocation}, New ID: {locationId}");
            return;
        }

        distribution.m_consumableData = new This_ConsumablesData(locationId, distribution.m_consumableData);
    }

    /// <summary>
    /// Store a ref to the last fetched distribution item. Used by <see cref="LG_Distribute_PickupItemsPerZone__Build__Patch"/>
    /// </summary>
    [ArchivePatch(typeof(LG_DistributionJobUtils), nameof(LG_DistributionJobUtils.TryGetExistingZoneFunctionDistribution))]
    public static class LG_DistributionJobUtils__TryGetExistingZoneFunctionDistribution__Patch
    {
        public static LG_DistributeItem? lastFetchedItem = null;

        public static void Postfix(ref LG_DistributeItem foundDist)
        {
            lastFetchedItem = foundDist;
        }
    }

    /// <summary>
    /// When building a pickup item, if that pickup item turns into a resource container item, make
    ///  sure the injected consumable data is forwarded along with it (so we can use it later)
    /// </summary>
    [ArchivePatch(typeof(LG_Distribute_PickupItemsPerZone), nameof(LG_Distribute_PickupItemsPerZone.Build))]
    public static class LG_Distribute_PickupItemsPerZone__Build__Patch
    {
        public static void Prefix(LG_Distribute_PickupItemsPerZone __instance)
        {
            LG_DistributionJobUtils__TryGetExistingZoneFunctionDistribution__Patch.lastFetchedItem = null;
        }

        public static void Postfix(LG_Distribute_PickupItemsPerZone __instance)
        {
            if (__instance.m_consumableDistributionData == null) return;

            LG_DistributeResourceContainer? container = 
                LG_DistributionJobUtils__TryGetExistingZoneFunctionDistribution__Patch.lastFetchedItem
                ?.TryCast<LG_DistributeResourceContainer>();

            if (container != null && container.m_packs.Count > 0)
            {
                var lastPack = container.m_packs[container.m_packs.Count - 1];


                if (lastPack.m_consumableData != null && lastPack.m_consumableData.Pointer != __instance.m_consumableDistributionData.Pointer)
                    FeatureLogger.Warning("Overwriting consumable data during consumable forwarding!");
                lastPack.m_consumableData = __instance.m_consumableDistributionData;
            }
        }
    }

    /// <summary>
    /// After the pickup is spawned, retrieve its location ID and associate it
    /// </summary>
    [ArchivePatch(typeof(LG_PickupItemBuilder), nameof(LG_PickupItemBuilder.SetupFunctionGO))]
    public static class LG_PickupItemBuilder__SetupFunctionGO__Patch
    {
        public static void Postfix(LG_PickupItemBuilder __instance, LG_LayerType layer, GameObject GO)
        {
            This_ConsumablesData? data = __instance.m_consumableData?.TryCast<This_ConsumablesData>();
            if (data == null) return; // Not associated with anything

            ItemInLevel? spawnedItem = GO.GetComponentInChildren<ItemInLevel>();
            if (spawnedItem == null) throw new NullReferenceException();

            AssociateItem(spawnedItem, data.StoredLocation);
        }
    }

    /// <summary>
    /// After the pickup is spawned, retrieve its location ID and associate it (for resource containers)
    /// </summary>
    [ArchivePatch(typeof(LG_ResourceContainer_Storage), nameof(LG_ResourceContainer_Storage.PlaceSmallGenericPickup))]
    public static class LG_ResourceContainer_Storage__PlaceSmallGenericPickup__Patch
    {
        public static void Postfix(LG_ResourceContainer_Storage __instance, ResourceContainerSpawnData pack, Transform align, int randomSeed)
        {
            This_ConsumablesData? data = pack.m_consumableData?.TryCast<This_ConsumablesData>();
            if (data == null) return; // Not associated with anything

            ItemInLevel? spawnedItem = align.GetComponentInChildren<ItemInLevel>();
            if (spawnedItem == null) throw new NullReferenceException();

            AssociateItem(spawnedItem, data.StoredLocation);
        }
    }

    /// <summary>
    /// Before picking up an item, check if it's associated with any locations.
    /// If it is, block the pickup and notify that those locations have been found.
    /// </summary>
    [ArchivePatch(typeof(LG_PickupItem_Sync), nameof(LG_PickupItem_Sync.AttemptInteract))]
    public static class LG_PickupItem_Sync__AttemptPickupInteraction__Patch
    {
        public static void Prefix(LG_PickupItem_Sync __instance, ref pPickupItemInteraction interaction)
        {
            if (interaction.type != ePickupItemInteractionType.Pickup)
                return; // We only care about when it's being picked up

            ContainsLocationPickupComp? comp = __instance.item?.PickupInteraction?.GetComponent<ContainsLocationPickupComp>();
            if (comp == null)
                return; // Not associated with a location

            StateTracker stateTracker = StateTracker.Get();
            PlayerAgent? agent;
            if (interaction.pPlayer.TryGetPlayer(out SNet_Player sPlayer))
                agent = sPlayer.PlayerAgent.TryCast<PlayerAgent>();
            else agent = null;

            var location = stateTracker.NotifyFoundLocation(comp.StoredLocation, agent);
            if (location.RandData.IsTreatedAsRandom)
            {
                // If it's a warden objective, we can also mutate objective progress
                CarryItemPickup_Core? carryItem = __instance.item!.TryCast<CarryItemPickup_Core>();
                GenericSmallPickupItem_Core? pickupItem = __instance.item.TryCast<GenericSmallPickupItem_Core>();
                if (carryItem != null)
                    carryItem.m_isWardenObjective = false;
                else if (pickupItem != null)
                    pickupItem.m_isWardenObjective = false;

                // Try and despawn it
                interaction.pPlayer.SetPlayer(null); // Fail the pickup (and hide the item)
                __instance.gameObject.transform.position = new(0, -10000, 0); // Hides it in case despawning fails
                __instance.item.ReplicationWrapper?.Replicator.Despawn();
                __instance.GetReplicator().Cast<SNet_Replicator>().Despawn();
            }

            // Big pickups can re-enter the level. If our comp is still there, it would
            //  be rechecked on next pickup, potentially causing issues
            UnityEngine.Object.Destroy(comp);
        }
    }

    /// <summary>
    /// When restoring a pickup from a recall, re-apply the custom naming if possible
    /// </summary>
    [ArchivePatch(typeof(LG_PickupItem_Sync), nameof(LG_PickupItem_Sync.SetCurrentState_NoSync))]
    public static class LG_PickupItem_Sync__SetCurrentState_NoSync__Patch
    {
        public static void Postfix(LG_PickupItem_Sync __instance)
        {
            var comp = __instance.item?.PickupInteraction?.GetComponent<ContainsLocationPickupComp>();
            if (comp == null) return;

            var interact = __instance.item?.PickupInteraction.TryCast<Interact_Pickup_PickupItem>();
            if (interact == null) return;

            Game.Data data = StateTracker.Get().MidManager.GetProcessedGameData();
            Location loc = data.Locations.LookUpValueChecked(comp.StoredLocation);
            if (!loc.RandData.IsRandomized) return;

            string backupName = data.Items.LookUpName(loc.ItemID);
            interact.SetName(new Il2CppFunc_string(() => loc.ScoutedItemName ?? backupName));
        }
    }

    /// <summary>
    /// Modify the detailed data result from a query to add multiworld info for an item
    /// </summary>
    /// <param name="item">The item to pull multiworld info from</param>
    /// <param name="detailedData">The detailed info list which will be modified</param>
    /// <param name="scout">If true, also scouts the location, providing the player with a hint of what it contains</param>
    public static void ModifyTerminalDetailInfo(ItemInLevel item, Il2CppSystem.Collections.Generic.List<string> detailedData, bool scout = true)
    {
        var id = item.PickupInteraction.GetComponent<ContainsLocationPickupComp>()?.StoredLocation;
        APCommandHandler.InsertLocationDataToDetailedInfo(
            StateTracker.Get(),
            detailedData,
            "MULTIWORLD ITEM",
            id is null ? Enumerable.Empty<LocationID>() : new LocationID[1] { id.Value }
        );
    }

    /// <summary>
    /// The vanilla imlementation for querying key items does not keep a reference to the key. So this func replaces it
    /// </summary>
    [InjectToIl2Cpp]
    public class KeyItemTerminalItemCallback : Il2CppSystem.Object
    {
        public KeyItemTerminalItemCallback(IntPtr ptr) : base(ptr)
            => KeyItem = null!;

        public KeyItemTerminalItemCallback(KeyItemPickup_Core item)
            : base(ClassInjector.DerivedConstructorPointer<KeyItemTerminalItemCallback>())
        {
            ClassInjector.DerivedConstructorBody(this);
            KeyItem = item;
        }

        public KeyItemPickup_Core KeyItem { get; private init; }
        public Il2CppSystem.Collections.Generic.List<string> OnWantDetailedInfo(Il2CppSystem.Collections.Generic.List<string> defaultDetails)
        {
            defaultDetails = KeyItemPickup_Core.__c.__9__11_0.Invoke(defaultDetails);
            ModifyTerminalDetailInfo(KeyItem, defaultDetails);
            return defaultDetails;
        }

        public Il2CppSystem.Func<Il2CppSystem.Collections.Generic.List<string>, Il2CppSystem.Collections.Generic.List<string>> GetDelegate()
        {
            IntPtr ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnWantDetailedInfo), typeof(Il2CppSystem.Collections.Generic.List<string>).FullName!, new string[] { typeof(Il2CppSystem.Collections.Generic.List<string>).FullName! }
            );
            return new Il2CppSystem.Func<Il2CppSystem.Collections.Generic.List<string>, Il2CppSystem.Collections.Generic.List<string>>(this, ptr);
        }

    }

    /// <summary>
    /// This property normally sets up the terminal item's detailed info call. We overwrite it with our custom callback
    /// </summary>
    [ArchivePatch(typeof(KeyItemPickup_Core), nameof(KeyItemPickup_Core.KeyItem), [typeof(GateKeyItem)], ArchivePatch.PatchMethodType.Setter)]
    public static class KeyItemPickup_Core__KeyItem__Patch
    {
        public static void Postfix(KeyItemPickup_Core __instance)
        {
            __instance.m_terminalItem.OnWantDetailedInfo = new KeyItemTerminalItemCallback(__instance).GetDelegate();
        }
    }

    /// <summary>
    /// Modify the query results of carry items
    /// </summary>
    [ArchivePatch(typeof(CarryItemPickup_Core.__c__DisplayClass30_0), nameof(CarryItemPickup_Core.__c__DisplayClass30_0._Setup_b__0))]
    public static class CarryItemPickup_Core____c__DisplayClass30_0___Setup_b__0__Patch
    {
        public static void Postfix(CarryItemPickup_Core.__c__DisplayClass30_0 __instance, Il2CppSystem.Collections.Generic.List<string> __result)
        {
            ModifyTerminalDetailInfo(__instance.__4__this, __result);
        }
    }

    /// <summary>
    /// The vanilla imlementation for querying generic small items does not keep a reference to the item. So this func replaces it
    /// </summary>
    [InjectToIl2Cpp]
    public class GenericSmallItemTerminalItemCallback : Il2CppSystem.Object
    {
        public GenericSmallItemTerminalItemCallback(IntPtr ptr) : base(ptr)
        {
            SmallItem = null!;
            OriginalCallback = null!;
        }

        public GenericSmallItemTerminalItemCallback(GenericSmallPickupItem_Core item, GenericSmallPickupItem_Core.__c__DisplayClass22_0 original)
            : base(ClassInjector.DerivedConstructorPointer<GenericSmallItemTerminalItemCallback>())
        {
            ClassInjector.DerivedConstructorBody(this);
            SmallItem = item;
            OriginalCallback = original;
        }

        public GenericSmallPickupItem_Core SmallItem { get; private init; }
        public GenericSmallPickupItem_Core.__c__DisplayClass22_0 OriginalCallback { get; private init; }

        public Il2CppSystem.Collections.Generic.List<string> OnWantDetailedInfo(Il2CppSystem.Collections.Generic.List<string> defaultDetails)
        {
            defaultDetails = OriginalCallback._SetupPersonnelId_b__0(defaultDetails);
            ModifyTerminalDetailInfo(SmallItem, defaultDetails);
            return defaultDetails;
        }

        public Il2CppSystem.Func<Il2CppSystem.Collections.Generic.List<string>, Il2CppSystem.Collections.Generic.List<string>> GetDelegate()
        {
            IntPtr ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnWantDetailedInfo), typeof(Il2CppSystem.Collections.Generic.List<string>).FullName!, new string[] { typeof(Il2CppSystem.Collections.Generic.List<string>).FullName! }
            );
            return new Il2CppSystem.Func<Il2CppSystem.Collections.Generic.List<string>, Il2CppSystem.Collections.Generic.List<string>>(this, ptr);
        }
    }

    /// <summary>
    /// Modify the query callback used by IDs
    /// </summary>
    [ArchivePatch(typeof(GenericSmallPickupItem_Core), nameof(GenericSmallPickupItem_Core.SetupPersonnelId))]
    public static class GenericSmallPickupItem_Core__SetupPersonnelId__Patch
    {
        public static void Postfix(GenericSmallPickupItem_Core __instance)
        {
            var test = __instance.m_terminalItem.OnWantDetailedInfo.Target?.TryCast<GenericSmallPickupItem_Core.__c__DisplayClass22_0>();
            if (test != null)
            {
                var newCallback = new GenericSmallItemTerminalItemCallback(__instance, test);
                __instance.m_terminalItem.OnWantDetailedInfo = newCallback.GetDelegate();
            }
        }
    }

    /// <summary>
    /// Modify the query callback used by generic small items
    /// </summary>
    [ArchivePatch(typeof(GenericSmallPickupItem_Core), nameof(GenericSmallPickupItem_Core._SetupGeneric_b__23_0))]
    public static class GenericSmallPickupItem_Core__SetupGeneric__Patch
    {
        public static void Postfix(GenericSmallPickupItem_Core __instance, Il2CppSystem.Collections.Generic.List<string> __result)
        {
            ModifyTerminalDetailInfo(__instance, __result);
        }
    }
}
