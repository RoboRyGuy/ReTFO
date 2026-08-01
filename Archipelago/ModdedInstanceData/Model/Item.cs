using LevelGeneration;
using Player;
using ReTFO.Archipelago.Features;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Runtime.InteropServices;

/// <summary>
/// Represents an item in GTFO. Some examples include:
/// <list type="bullet">
///   <item>Colored keys and bulkhead keys</item>
///   <item>"Extraction Reachable" (an event item)</item>
///   <item>Objective items - ie generators or a central gen cluster</item>
/// </list>
/// Items do not necessarily support randomization; some items are purely event items which
///  are used to perform pathing logic, while others may simply be unimeplemented.
/// </summary>
[DataContract]
public class Item
{
    public Item(ItemData randData)
    {
        RandData = randData;
    }

    /// <summary>
    /// Randomization data associated with this item.
    /// </summary>
    [DataMember(Name = "rand_data")]
    public ItemData RandData { get; private set; }

    /// <summary>
    /// Update this item's RandData
    /// </summary>
    /// <param name="isWhitelisted"></param>
    /// <param name="isBlacklisted"></param>
    public void UpdateRandomization(bool isWhitelisted, bool isBlacklisted)
        => RandData = new(RandData) { IsWhitelisted = isWhitelisted, IsBlacklisted = isBlacklisted };

    // === Virtuals for handling randomization events =============================================

    /// <summary>
    /// Called immediately when the item is obtained.
    /// Note that this can be called multiple times if a checkpoint is loaded from before the item being obtained.
    /// </summary>
    /// <param name="stateTracker">The stateTracker for this session</param>
    /// <param name="sourceLocationId">The ID of the location this was found in if found in this lobby.</param>
    /// <param name="player">The player who found the item, if that player is in this lobby (for randomlike items)</param>
    /// <param name="itemId">The item ID used to look up this item</param>
    /// <remarks>
    /// The sourceLocationId is supplied during <see cref="StateTracker.eState.FakeConnect"/>, where it can be used for debug.
    /// Otherwise, it is supplied when the item is not randomized but is randomlike.
    /// </remarks>
    public virtual void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId) { }

    /// <summary>
    /// Called immediately when the item is lost - Items can only be lost by a call to "uncollect".
    /// </summary>
    /// <param name="stateTracker">The stateTracker for this session</param>
    /// <param name="itemId">The item ID used to look up this item</param>
    public virtual void OnItemLost(StateTracker stateTracker, ItemID itemId) { }

    /// <summary>
    /// Called just after loading into an expedition if this item has been previously obtained
    /// </summary>
    /// <param name="stateTracker">The stateTracker for this session</param>
    /// <param name="data">The expedition being started</param>
    /// <param name="itemId">The item ID used to look up this item</param>
    public virtual void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data, ItemID itemId) { }

    /// <summary>
    /// Called when the player retrieves the item from the terminal item system (only if placed in the system)
    /// </summary>
    /// <param name="stateTracker">Current StateTracker</param>
    /// <param name="terminal">The terminal the item was claimed from. Useful for custom textual output</param>
    /// <param name="itemId">The item ID used to look up this item</param>
    /// <returns>
    /// Expected to return an enumerable of actions. The enumerable will be immediately enumerated and placed in a list.
    /// Each action in the enumerable will be executed in order, pausing when the terminal is processing.
    /// </returns>
    /// <remarks>
    /// The intended use of the output is two provide 2 actions; the first adds terminal outputs (ie trigger a wait),
    ///  and the second will give the item. In this way, the second action only triggers if the player allows the
    ///  terminal to "do work" (lingers near it), and only gives the item once the terminal is done.
    /// Also of note, items are removed from the terminal system immdiately after all queued items provide their actions.
    /// </remarks>
    public virtual IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        => throw new NotImplementedException();
}

/// <summary>
/// A variation of Item which simplifies its callbacks into a single callback invoked either when the relevant expedition
///  is entered or when the item is obtained while in the relevant expeidtion.
/// </summary>
public abstract class ExpeditionItem : Item
{
    public ExpeditionItem(ItemData randData) : base(randData) { }

    /// <summary>
    /// The region to check when determining if this item should be added to the terminal
    /// </summary>
    public abstract RegionID TargetRegion { get; }

    /// <summary>
    /// Check if in the correct expedition
    /// </summary>
    protected bool CheckExpedition(StateTracker stateTracker)
        => stateTracker.GameData.IsInCurrentExpedition(TargetRegion);

    /// <summary>
    /// Check if in the provided expedition. Slightly more efficient than the StateTracker variant.
    /// </summary>
    protected bool CheckExpedition(Expedition.Data data)
        => data.Regions.IsChild(TargetRegion, data.Region_Expedition);

    public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
    {
        if (CheckExpedition(stateTracker))
            OnEnteredExpedition(stateTracker, sourceLocationId, player, itemId);
    }

    public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data, ItemID itemId)
    {
        if (CheckExpedition(data))
            OnEnteredExpedition(stateTracker, new(), null, itemId);
    }

    /// <summary>
    /// Called when entering an expedition containing TargetRegion or when collecting the item
    ///  while in such an expedition.
    /// </summary>
    /// <param name="stateTracker">The active state tracker invoking this event</param>
    /// <param name="sourceLocationId">ID of location this item was collected from, if known</param>
    /// <param name="player">The player who collected it, if known</param>
    /// <param name="itemId">The ID this item was registered under</param>
    public abstract void OnEnteredExpedition(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId);
}

/// <summary>
/// A variation of Item which automatically registers itself in the terminal system
///  when dropping in to its expedition
/// </summary>
public abstract class TerminalItem : ExpeditionItem
{
    public TerminalItem(ItemData randData) : base(randData) { }
    public override void OnEnteredExpedition(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
        => stateTracker.AddItemToTerminal(itemId);
}

/// <summary>
/// Simple wrapper used to identify an ID for specifically an item
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = sizeof(uint)), DataContract]
public readonly struct ItemID : ITagID, IEquatable<ItemID>, IComparable<ItemID>
{
    [FieldOffset(0), DataMember(Name = "id")]
    private readonly uint m_ID;
    public uint ID 
    { 
        get => m_ID; 
        init => m_ID = value; 
    }

    public bool IsNull => ID == 0;
    public int AsIndex { get => checked((int)ID - 1); init => ID = unchecked((uint)value + 1u); }

    public bool Equals(ItemID other) => ID == other.ID;
    public int CompareTo(ItemID other) => ID.CompareTo(other.ID);

    public override int GetHashCode() => ID.GetHashCode();
    public override bool Equals(object? obj) => obj is ItemID && Equals((ItemID)obj);
    public override string ToString() => $"ItemID {ID}";

    public static bool operator ==(ItemID left, ItemID right) => left.Equals(right);
    public static bool operator !=(ItemID left, ItemID right) => !left.Equals(right);
    public static bool operator <(ItemID left, ItemID right) => left.CompareTo(right) < 0;
    public static bool operator <=(ItemID left, ItemID right) => left.CompareTo(right) <= 0;
    public static bool operator >(ItemID left, ItemID right) => left.CompareTo(right) > 0;
    public static bool operator >=(ItemID left, ItemID right) => left.CompareTo(right) >= 0;

    public static ItemID From(Game.Data data, string name, Func<TagDefinition<ItemID>> definitionFactory, Item? item = null)
        => data.Items.LookUpOrCreate(name, definitionFactory, item);

    public static ItemID From<TData>(TData data, string name, Func<TData, TagDefinition<ItemID>> definitionFactory, Item? item = null) where TData : Game.Data
        => data.Items.LookUpOrCreate(data, name, definitionFactory, item);
}

/// <summary>
/// Simple wrapper around some enum values
/// </summary>
[DataContract]
public struct ItemData
{
    /// <summary>
    /// Enum values used by this data. Note that, where applicable, these correspond to 
    ///  archipelago's item classifications
    /// </summary>
    [Flags]
    public enum eType
    {
        /// <summary>
        /// No value
        /// </summary>
        None = 0,

        /// <summary>
        /// Any item that can possibly be required for progression
        /// </summary>
        Progression = 1 << 0,

        /// <summary>
        /// Any item deemed "useful". When combined with progression,
        /// it indicates a particular useful progression item
        /// </summary>
        Useful = 1 << 1,

        /// <summary>
        /// A standard or trash item
        /// </summary>
        Filler = 1 << 2,

        /// <summary>
        /// An item which has a negative impact on the player
        /// </summary>
        Trap = 1 << 3,

        /// <summary>
        /// If set, Archipelago will avoid moving this item to an earlier sphere.
        /// Typically set on bountiful progression items (like money or tokens)
        ///  to prevent the early game being flooded with boring items
        /// </summary>
        SkipBalancing = 1 << 4,

        /// <summary>
        /// Denote a progression item which should not be placed on priority locations
        /// </summary>
        Deprioritized = 1 << 5,

        /// <summary>
        /// Random-like items will behave as randomized even if not actually randomized. This
        ///  means being collected into the pool and having OnItemCollected, etc called when
        ///  their location is found.
        /// </summary>
        RandomLike = 1 << 6,

        /// <summary>
        /// This item defaults to being collected if not in the randomized.
        /// If the item is randomized, <see cref="Item.OnItemLost(StateTracker)"/> will be called 
        ///  at the start of the session so the floating item can prep the world.
        /// </summary>
        IsCollectedByDefault = 1 << 7,

        /// <summary>
        /// This item is in the randomization whitelist
        /// </summary>
        IsWhitelisted = 1 << 8,

        /// <summary>
        /// This item is in the randomization blacklist
        /// </summary>
        IsBlacklisted = 1 << 9,
    }

    /// <summary>
    /// Construct item data; optionally provide the starting type value
    /// </summary>
    public ItemData(eType value = eType.None) => m_value = value;

    /// <summary>
    /// Copy constructor
    /// </summary>
    /// <param name="source"></param>
    public ItemData(ItemData source) => m_value = source.m_value;

    /// <summary>
    /// Stored enum value
    /// </summary>
    private readonly eType m_value = eType.None;

    /// <summary>
    /// Set or write the IsProgression bit
    /// </summary>
    [DataMember(Name = "is_progression")]
    public bool IsProgression
    {
        get => (m_value & eType.Progression) != 0;
        init => m_value = value ? (m_value | eType.Progression) : (m_value & ~eType.Progression);
    }

    /// <summary>
    /// Set or write the IsUseful bit
    /// </summary>
    [DataMember(Name = "is_useful")]
    public bool IsUseful
    {
        get => (m_value & eType.Useful) != 0;
        init => m_value = value ? (m_value | eType.Useful) : (m_value & ~eType.Useful);
    }

    /// <summary>
    /// Set or write the IsFiller bit
    /// </summary>
    [DataMember(Name = "is_filler")]
    public bool IsFiller
    {
        get => (m_value & eType.Filler) != 0;
        init => m_value = value ? (m_value | eType.Filler) : (m_value & ~eType.Filler);
    }

    /// <summary>
    /// Set or write the IsTrapbit
    /// </summary>
    [DataMember(Name = "is_trap")]
    public bool IsTrap
    {
        get => (m_value & eType.Trap) != 0;
        init => m_value = value ? (m_value | eType.Trap) : (m_value & ~eType.Trap);
    }

    /// <summary>
    /// Set or write the DoSkipBalancing
    /// </summary>
    [DataMember(Name = "do_skip_balancing")]
    public bool DoSkipBalancing
    {
        get => (m_value & eType.SkipBalancing) != 0;
        init => m_value = value ? (m_value | eType.SkipBalancing) : (m_value & ~eType.SkipBalancing);
    }

    /// <summary>
    /// Set or write the IsDeprioritized bit
    /// </summary>
    [DataMember(Name = "is_deprioritized")]
    public bool IsDeprioritized
    {
        get => (m_value & eType.Deprioritized) != 0;
        init => m_value = value ? (m_value | eType.Deprioritized) : (m_value & ~eType.Deprioritized);
    }

    /// <summary>
    /// Set or write the CollectedByDefault bit
    /// </summary>
    [DataMember(Name = "is_collected_by_default")]
    public bool IsCollectedByDefault
    {
        get => (m_value & eType.IsCollectedByDefault) != 0;
        init => m_value = value ? (m_value | eType.IsCollectedByDefault) : (m_value & ~eType.IsCollectedByDefault);
    }

    /// <summary>
    /// Set or write the IsRandomLike bit
    /// </summary>
    [DataMember(Name = "is_randomlike")]
    public bool IsRandomLike
    {
        get => (m_value & eType.RandomLike) != 0;
        init => m_value = value ? (m_value | eType.RandomLike) : (m_value & ~eType.RandomLike);
    }

    /// <summary>
    /// Set or write the IsWhitelisted bit
    /// </summary>
    public bool IsWhitelisted
    {
        get => (m_value & eType.IsWhitelisted) != 0;
        init => m_value = value ? (m_value | eType.IsWhitelisted) : (m_value & ~eType.IsWhitelisted);
    }

    /// <summary>
    /// Set or write the IsBlacklisted bit
    /// </summary>
    public bool IsBlacklisted
    {
        get => (m_value & eType.IsBlacklisted) != 0;
        init => m_value = value ? (m_value | eType.IsBlacklisted) : (m_value & ~eType.IsBlacklisted);
    }

    /// <summary>
    /// Returns true if this item can, on its own merits, be randomized.
    /// Note that this does not account region checks, which must be performed contextually.
    /// </summary>
    public bool CanBeRandomized => IsWhitelisted && !IsBlacklisted;

    /// <summary>
    /// Get a copy without randomization-specific data
    /// </summary>
    public ItemData AsNew => new(m_value & ~(eType.IsWhitelisted | eType.IsBlacklisted));
}