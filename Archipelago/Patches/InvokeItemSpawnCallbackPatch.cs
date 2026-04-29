using HarmonyLib;

namespace ReTFO.Archipelago.Patches;

/// <summary>
/// For some reason, the item spawn callbacks aren't invoking, so I've added a postfix to ensure at
///  least the callbacks I care about work. I didn't test if this is needed for client; it's possible
///  the callback only invokes correctly for clients?
/// </summary>
[HarmonyPatch(typeof(ItemReplicationManager), nameof(ItemReplicationManager.OnItemSpawn))]
public static class InvokeItemSpawnCallbackPatch
{
    public static void Postfix(pItemSpawnData spawnData, ItemReplicator replicator)
    {
        int entry = ItemReplicationManager.m_callBacks.FindEntry(spawnData.callbackID);
        if (entry != -1 && SNetwork.SNet.IsMaster)
        {
            ItemReplicationManager.m_callBacks.entries[entry].value.Invoke(replicator.Item.Cast<ISyncedItem>(), null);
            ItemReplicationManager.RemoveCallback(spawnData.callbackID);
        }
    }
}
