using GameData;
using Il2CppInterop.Runtime.Injection;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
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
        public TagResolver Tag_PickupLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Pickup Locations", "Locations checked when items are picked up (keys, cells, artifacts, etc)", gd.Tag_AllLocations));

        public TagResolver Tag_SmallPickupLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Small Pickup Locations", "Location checked when small items are picked up (keys, IDs, GLPS, etc)", gd.Tag_PickupLocations));

        public TagResolver Tag_BigPickupLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Big Pickup Locations", "Location checked when big items are picked up (cells, fog turbines, babies, etc)", gd.Tag_PickupLocations));

        public TagResolver Tag_ResourcePickupLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Resource Pickup Locations", "Location checked when resources are picked up (ammo, med, tool, disinfect)", gd.Tag_PickupLocations));

        public TagResolver Tag_ConsumablePickupLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Consumable Pickup Locations", "Location checked when consumables are picked up (glowsticks, flashlights, c-foam grenades, etc)", gd.Tag_PickupLocations));

        public TagResolver Tag_ArtifactPickupLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Artifact Pickup Locations", "Location checked when artifacts are picked up (muted, bold, aggressive)", gd.Tag_PickupLocations));


        public TagResolver Tag_PickupItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Pickup Items", "All items which are picked up (keys, cells, artifacts, etc)", gd.Tag_AllItems));

        public TagResolver Tag_SmallPickupItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Small Pickup Items", "All small pickup items which end up in the left-side menu (keys, IDs, GLPS, etc)", gd.Tag_PickupItems));

        public TagResolver Tag_BigPickupItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Big Pickup Items", "All big pickup items (cells, fog turbines, babies, etc)", gd.Tag_PickupItems));

        public TagResolver Tag_ResourcePickupItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Resource Pickup Items", "All resource pickups (ammo, med, tool, disinfect)", gd.Tag_PickupItems));

        public TagResolver Tag_ConsumablePickupItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Consumable Pickup Items", "All consumable pickups (glowsticks, flashlights, c-foam grenades, etc)", gd.Tag_PickupItems));

        public TagResolver Tag_ArtifactPickupItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Artifact Pickup Items", "All artifact picksup (muted, bold, aggressive)", gd.Tag_PickupItems));
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
        data.AddOption(new OptionWhiteOrBlacklist()
        {
            DisplayName = "Randomize Small Pickups",
            Description = "Randomize all supported small pickups. This includes colored keys, bulkhead keys, and objective pickups (ie IDs)",
            Category = PICKUPS_OPTION_CATEGORY,
            Condition = new(),
            DefaultValue = 0,
            Tag = data.Tag_SmallPickupItems,
        });
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
        ContainsLocationPickupComp? comp = item.GetComponent<ContainsLocationPickupComp>();
        if (comp == null)
            comp = item.gameObject.AddComponent<ContainsLocationPickupComp>();
        else if (!comp.StoredLocation.IsNull)
        {
            Game.Data gameData = Plugin.Get().MidManager.GetProcessedGameData();
            int locLength = Math.Max(comp.StoredLocation.AsId.ToString().Length, locationId.AsId.ToString().Length);
            string formatString = new string('0', locLength);
            FeatureLogger.Error(
                $"Overwriting location on pickup!\n"
                + $"  Old Location: [{comp.StoredLocation.AsId.ToString(formatString)}] {gameData.LookupTagDef(gameData.LookupLocation(comp.StoredLocation).NameTag).Name}"
                + $"  New Location: [{         locationId.AsId.ToString(formatString)}] {gameData.LookupTagDef(gameData.LookupLocation(locationId).NameTag).Name}"
            );
        }

        comp.StoredLocation = locationId;

        StateTracker stateTracker = StateTracker.Get();
        Location loc = stateTracker.MidManager.GetProcessedGameData().LookupLocation(locationId);
        if (despawnIfFound && stateTracker.HasLocation(locationId) && loc.RandData.IsTreatedAsRandom)
        {
            // Try to despawn the item
            if (item.ReplicationWrapper != null)
            {
                // We can despawn the item using its dynamic replicator, which is synced
                item.ReplicationWrapper?.Replicator.Despawn();
            }
            else 
            {
                // The assumption is that this code is invoked by a synced command - ie during the level build.
                // This is safe if and only if that is true
                UnityEngine.Object.Destroy(item.gameObject);
            }
        }
        else if (loc.RandData.IsRandomized)
        {
            // Set the name on the item to match the name Archipelago gave it
            Interact_Pickup_PickupItem? pickup = item.PickupInteraction.TryCast<Interact_Pickup_PickupItem>();
            if (pickup != null)
            {
                Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();
                string backupName = gameData.LookupTagDef(gameData.LookupItem(loc.ItemID).NameTag).Name;
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

            ContainsLocationPickupComp? comp = __instance.GetComponent<ContainsLocationPickupComp>();
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
                interaction.pPlayer.SetPlayer(null);

                // If it's a warden objective, we can also objective progress
                CarryItemPickup_Core? carryItem = __instance.item.TryCast<CarryItemPickup_Core>();
                GenericSmallPickupItem_Core? pickupItem = __instance.item.TryCast<GenericSmallPickupItem_Core>();
                if (carryItem != null)
                    carryItem.m_isWardenObjective = false;
                else if (pickupItem != null)
                    pickupItem.m_isWardenObjective = false;

                // Try and despawn it
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
            var comp = __instance.item?.GetComponent<ContainsLocationPickupComp>();
            if (comp == null) return;

            var interact = __instance.item?.PickupInteraction.TryCast<Interact_Pickup_PickupItem>();
            if (interact == null) return;
            
            Game.Data data = StateTracker.Get().MidManager.GetProcessedGameData();
            Location loc = data.LookupLocation(comp.StoredLocation);
            if (!loc.RandData.IsRandomized) return;

            string backupName = data.LookupTagDef(data.LookupItem(loc.ItemID).NameTag).Name;
            interact.SetName(new Il2CppFunc_string(() => loc.ScoutedItemName ?? backupName));
        }
    }
}
