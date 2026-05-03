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
    private event Action<ISyncedItem>? OnItemSpawned;

    /// <summary>
    /// Queue an action for this item for either when it's spawned or immediately if it's already spawned
    /// </summary>
    public void AddSpawnCallback(Action<ISyncedItem> action)
    {
        OnItemSpawned += action;
        if (Item != null)
            OnItemSpawned.Invoke(Item);
    }

    /// <summary>
    /// Either queue or immediately despawn the item
    /// </summary>
    public void QueueDespawn()
    {
        static void Despawn(ISyncedItem item)
            => item.Cast<global::Item>().ReplicationWrapper.Replicator.Despawn();
        AddSpawnCallback(Despawn);
    }

    /// <summary>
    /// Callback for when the item is spawned. Pass to ItemReplicationManager.SpawnItem.
    /// </summary>
    /// <param name="item">
    /// The item which was spawned.
    /// Depending on your spawn request, you can usually safely cast to global::Item 
    ///  or its derived types (ItemInLevel, CarryItemPickup_Core, etc)
    /// </param>
    public void OnSpawn(ISyncedItem item, PlayerAgent _)
    {
        Item = item;
        OnItemSpawned?.Invoke(item);
    }
}
