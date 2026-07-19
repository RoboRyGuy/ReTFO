using HarmonyLib;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using SNetwork;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.Patches;

/// <summary>
/// This class is a collection of patches to help support 'dynamic items', 
///  which are spawned mid-game.
/// For all of these patches, it's worth noting that during a recall (ie loading a
///  checkpoint) dynamic items are despawned and replaced... for some reason. The game
///  doesn't handle it super well, so these patches catch some of the faulty calls
///  and redirects them to the newly-spawned object
/// </summary>
[HarmonyPatch]
public static class DynamicItemPatches
{
    /// <summary>
    /// Simple comparer used by our sorted lists below
    /// </summary>
    private class pRepComparer : Comparer<SNetStructs.pReplicator>
    {
        public override int Compare(SNetStructs.pReplicator x, SNetStructs.pReplicator y)
            => Comparer<ushort>.Default.Compare(x.keyPlusOne, y.keyPlusOne);
    }

    /// <summary>
    /// During a recall, we will populate this with items the game wants to give the player,
    ///  but which are currently destroy. Then, when items are respawned, we can reference this to see if a player
    ///  should be holding them.
    /// </summary>
    private static SortedList<SNetStructs.pReplicator, SNetStructs.pPlayer> s_desiredInventory = new(new pRepComparer());

    /// <summary>
    /// During a recall, we catch state change updates which are going to destroyed objects. In the OnSpawn
    ///  patch, we can then reference this list of lists and perform the updates we need
    /// </summary>
    private static SortedList<SNetStructs.pReplicator, List<pPickupItemState>> s_delayedUpdates = new(new pRepComparer());

    /// <summary>
    /// Clean up our delay lists before starting a recall
    /// </summary>
    [HarmonyPatch(typeof(WardenObjectiveManager), nameof(WardenObjectiveManager.OnPrepareForRecall))]
    [HarmonyPrefix]
    private static void PreRecall()
    {
        s_desiredInventory.Clear();
        s_delayedUpdates.Clear();
    }

    /// <summary>
    /// Check if we're receiving a destroyed dynamic item. If we are, don't
    /// </summary>
    [HarmonyPatch(typeof(PlayerSessionStatusManager), nameof(PlayerSessionStatusManager.OnReceiveInventoryItem))]
    [HarmonyPrefix]
    public static bool PreReceiveInventoryItem(pItemData_WithOwner inventoryItem)
    {
        if (!SNet.Capture.IsRecalling) return true;

        if (inventoryItem.data.replicatorRef.TryGetID(out var rep))
        {
            var comp = rep.ReplicatorSupplier
                .TryCast<SNet_StateReplicator<pPickupItemState, pPickupItemInteraction>>()?
                .m_provider?.TryCast<LG_PickupItem_Sync>();

            // Note that Unity overrides null checks for its types; that is the check we're explicitly performing here
            if (comp == null || (comp.item.ReplicationWrapper?.m_aboutToBeDestroyed ?? false))
            {
                FeatureLogger.Notice("Preventing null object from entering inventory");
                s_desiredInventory[inventoryItem.data.replicatorRef] = inventoryItem.owningPlayer;
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Check if the item was destroyed before allowing the state change to proceed
    /// </summary>
    [HarmonyPatch(typeof(LG_PickupItem_Sync), nameof(LG_PickupItem_Sync.OnStateChange))]
    [HarmonyPrefix]
    public static bool PreStateChange(LG_PickupItem_Sync __instance, pPickupItemState oldState, pPickupItemState newState, bool isRecall)
    {
        if (!SNet.Capture.IsRecalling)
            return true;

        if (__instance == null || __instance.item == null || (__instance.item.ReplicationWrapper?.m_aboutToBeDestroyed ?? false))
        {
            FeatureLogger.Notice("Delaying state update for non-null object");
            var key = __instance!.item!.pItemData.replicatorRef;
            if (s_delayedUpdates.TryGetValue(key, out var list))
                list.Add(newState);
            else
                s_delayedUpdates[key] = [newState];
            return false;
        }
        else
            return true;
    }

    /// <summary>
    /// Ensures dynamically spawned carry items correctly set their spawn data. Because, in vanilla, they don't (for some reason).
    /// We also check if the item is already associated with a replicator and, if it is, we overwrite its sync
    ///  to use that replicator instead of the one it created during spawn.
    /// Note that this applies even while not recalling, as long as the item is 'dynamic'
    /// </summary>
    [HarmonyPatch(typeof(SNet_ReplicationManager_Item), nameof(SNet_ReplicationManager_Item.OnSpawn))]
    [HarmonyPrefix]
    public static void PreItemSpawned_SNet(ref pItemSpawnData spawnData, ItemReplicator replicator)
    {
        if (replicator.Item?.ReplicationWrapper == null) return;

        replicator.Item.Set_pItemData(spawnData.itemData);
        if (spawnData.itemData.replicatorRef.TryGetID(out var rep))
        {
            SNet_StateReplicator<pPickupItemState, pPickupItemInteraction> srep = rep.ReplicatorSupplier.Cast<SNet_StateReplicator<pPickupItemState, pPickupItemInteraction>>();
            LG_PickupItem_Sync sync = replicator.Item.GetComponent<LG_PickupItem_Sync>();

            sync.m_stateReplicator = srep;
            srep.m_provider = sync.Cast<iSNet_StateReplicatorProvider<pPickupItemState, pPickupItemInteraction>>();
        }

        // We can set up some of these things for derived instances too

        ItemInLevel? inLevel = replicator.Item.TryCast<ItemInLevel>();
        if (inLevel != null)
        {
            if (spawnData.courseNode.TryGet(out var node))
                inLevel.CourseNode = node;
        }
        else return;

        CarryItemPickup_Core? carry = replicator.Item.TryCast<CarryItemPickup_Core>();
        if (carry != null)
        {
            if (spawnData.itemData.originCourseNode.TryGet(out var node))
                carry.SpawnNode = node;
            return;
        }

        GenericSmallPickupItem_Core? generic = replicator.Item.TryCast<GenericSmallPickupItem_Core>();
        if (generic != null)
        {
            if (spawnData.itemData.originCourseNode.TryGet(out var node))
                generic.SpawnNode = node;
            return;
        }
    }

    /// <summary>
    /// After items are spawned, we can apply our delayed updates and inventory checks
    /// </summary>
    [HarmonyPatch(typeof(SNet_ReplicationManager_Item), nameof(SNet_ReplicationManager_Item.OnSpawn))]
    [HarmonyPostfix]
    public static void PostItemSpawned_SNet(pItemSpawnData spawnData, ItemReplicator replicator)
    {
        if (!SNet.Capture.IsRecalling) return;

        var key = spawnData.itemData.replicatorRef;

        // Check if this item was in a player inventory
        if (s_desiredInventory.TryGetValue(key, out var player))
        {
            var sync = replicator.Item.GetComponent<LG_PickupItem_Sync>();
            spawnData.itemData.replicatorRef.SetID(sync.m_stateReplicator.Replicator);

            PlayerManager.Current.PlayerSessionStatusManager.OnReceiveInventoryItem(
                new pItemData_WithOwner() { owningPlayer = player, data = spawnData.itemData }
            );
            s_desiredInventory.Remove(key);
        }

        // Check if this item has a delayed state change update
        if (s_delayedUpdates.TryGetValue(key, out var updates))
        {
            // For some reason, we only want to apply the first state change.
            // I'm not really sure why this works, but it does!
            var sync = replicator.Item.Cast<ItemInLevel>().internalSync.Cast<LG_PickupItem_Sync>();
            sync.OnStateChange(sync.GetCurrentState(), updates.First(), true);
            s_delayedUpdates.Remove(key);
        }
    }

    /// <summary>
    /// For some reason, the item spawn callbacks aren't invoking, so I've added a postfix to ensure at
    ///  least the callbacks I care about work. I didn't test if this is needed for client; it's possible
    ///  the callback only invokes correctly for clients?
    /// </summary>
    [HarmonyPatch(typeof(ItemReplicationManager), nameof(ItemReplicationManager.OnItemSpawn))]
    [HarmonyPostfix]
    public static void PostOnItemSpawn(pItemSpawnData spawnData, ItemReplicator replicator)
    {
        int entry = ItemReplicationManager.m_callBacks.FindEntry(spawnData.callbackID);
        if (entry != -1 && SNet.IsMaster)
        {
            ItemReplicationManager.m_callBacks.entries[entry].value.Invoke(replicator.Item.Cast<ISyncedItem>(), null);
        }
    }

    /// <summary>
    /// Check if we failed to place any items in the inventory
    /// </summary>
    [HarmonyPatch(typeof(WardenObjectiveManager), nameof(WardenObjectiveManager.OnRecallComplete))]
    [HarmonyPostfix]
    public static void PostRecallComplete()
    {
        foreach (var desired in s_desiredInventory)
        {
            string desiredName;
            if (desired.Key.TryGetID(out var rep))
                desiredName = rep.name;
            else
                desiredName = $"Replicator {desired.Key.keyPlusOne}";

            string playerName;
            if (desired.Value.TryGetPlayer(out var player))
                playerName = player.NickName;
            else
                playerName = $"{(desired.Value.IsBot ? "Bot" : "Player")} {desired.Value.lookup}";

            FeatureLogger.Warning($"Failed to find dynamic inventory item post-recall: {desiredName} for {playerName}");
        }
    }

}
