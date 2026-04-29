using Player;
using System;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// For use when spawning an item mid-level.
/// </summary>
public class AsyncItemSpawnWrapper
{
    public AsyncItemSpawnWrapper() { }

    /// <summary>
    /// The item which was spawned if successful, null otherwise.
    /// </summary>
    public ISyncedItem? Item { get; private set; } = null;

    /// <summary>
    /// Invoked when the item is spawned.
    /// </summary>
    public event Action<ISyncedItem, PlayerAgent>? OnItemSpawned;

    /// <summary>
    /// Set this item to despawn if it is spawned in.
    /// Useful if you were waiting for it to spawn, it failed, and now you are ignoring it.
    /// </summary>
    public void QueueDespawn()
    {
        static void DespawnOnSpawn(ISyncedItem item, PlayerAgent _)
            => item.Cast<global::Item>().ReplicationWrapper.Replicator.Despawn();
        if (Item != null)
            DespawnOnSpawn(Item, null!);
        OnItemSpawned += DespawnOnSpawn;
    }

    /// <summary>
    /// Callback for when the item is spawned. Pass to ItemReplicationManager.SpawnItem.
    /// </summary>
    /// <param name="item">
    /// The item which was spawned.
    /// Depending on your spawn request, you can usually safely cast to global::Item 
    ///  or its derived types (ItemInLevel, CarryItemPickup_Core, etc)
    /// </param>
    /// <param name="player">
    /// The player which was submitted in the spawn request, typically the player which spawned the item.
    /// </param>
    public void OnSpawn(ISyncedItem item, PlayerAgent player)
    {
        Item = item;
        OnItemSpawned?.Invoke(item, player);
    }
}
