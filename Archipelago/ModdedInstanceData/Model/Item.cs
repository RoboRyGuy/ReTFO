using LevelGeneration;
using Player;
using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.ModdedInstanceData.Processors;

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
    /// <summary>
    /// Construct an abstract item from a name and categories
    /// </summary>
    /// <param name="nameTag">The main tag for the item. Must be unique</param>
    /// <param name="randData">Data used by this item for randomization</param>
    public Item(RandomizationTag nameTag, ItemData randData)
    {
        NameTag = nameTag;
        RandData = randData;
    }

    /// <summary>
    /// Identifying tag used by this item
    /// </summary>
    [DataMember(Name = "name_tag")]
    public RandomizationTag NameTag { get; init; }

    /// <summary>
    /// Optional secondary tag for this item.
    /// </summary>
    [DataMember(Name = "tag2")]
    public RandomizationTag Tag2 { get; init; } = new();

    /// <summary>
    /// Optional tertiary tag for this item.
    /// </summary>
    [DataMember(Name = "tag3")]
    public RandomizationTag Tag3 { get; init; } = new();

    /// <summary>
    /// Randomization data associated with this item.
    /// </summary>
    [DataMember(Name = "rand_data")]
    public ItemData RandData { get; set; }

    /// <summary>
    /// Optional; if not null, this item can only be randomized if the supplied expedition 
    /// is randomized. Typically used by floating items to help ensure only relevant floating
    /// items are randomized.
    /// </summary>
    public virtual Expedition.Data? RequiredExpedition => null;

    /// <summary>
    /// Property used purely to assist with serialization, since Expedition.Data is not serializable
    /// </summary>
    [DataMember(Name = "required_expedition")]
    private string? RequiredExpeditionName
    {
        get => RequiredExpedition?.ExpeditionName ?? null;
        set { } // Discard
    }

    /// <summary>
    /// How this item should be represented when a path uses it as a requirement.
    /// Override this if you need the item to use a category instead.
    /// </summary>
    [DataMember(Name = "path_reqs")]
    public virtual Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Item, NameTag);

    /// <summary>
    /// Implicit conversion helper using PathReqs virtual property to perform the conversion
    /// </summary>
    public static implicit operator Path.RequiredItem(Item self) => self.PathReqs;

    // === Virtuals for handling randomization events =============================================

    /// <summary>
    /// Called immediately when the item is obtained.
    /// Note that this can be called multiple times if a checkpoint is loaded from before the item being obtained.
    /// </summary>
    /// <param name="stateTracker">The stateTracker for this session</param>
    /// <param name="sourceLocationId">The ID of the location this was found in if found in this lobby.</param>
    /// <param name="player">The player who found the item, if that player is in this lobby (for randomlike items)</param>
    /// <remarks>
    /// The sourceLocationId is supplied during <see cref="StateTracker.eState.FakeConnect"/>, where it can be used for debug.
    /// Otherwise, it is supplied when the item is not randomized but is randomlike.
    /// </remarks>
    public virtual void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player) { }

    /// <summary>
    /// Called immediately when the item is lost - Items can only be lost by a call to "uncollect".
    /// </summary>
    /// <param name="stateTracker">The stateTracker for this session</param>
    public virtual void OnItemLost(StateTracker stateTracker) { }

    /// <summary>
    /// Called just after loading into an expedition if this item has been previously obtained
    /// </summary>
    /// <param name="stateTracker">The stateTracker for this session</param>
    /// <param name="data">The expedition being started</param>
    public virtual void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data) { }

    /// <summary>
    /// Called when the player retrieves the item from the terminal item system (only if placed in the system)
    /// </summary>
    /// <param name="stateTracker">Current StateTracker</param>
    /// <param name="terminal">The terminal the item was claimed from. Useful for custom textual output</param>
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
    public virtual IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        => throw new NotImplementedException();
}

/// <summary>
/// Simple wrapper around a long to help identify it as an ItemID, usable
///  for looking up an Item instance in GameData.
/// </summary>
[DataContract]
public struct ItemID : INullable, IId, IIndex, IComparable<ItemID>, IEquatable<ItemID>
{
    public ItemID() { }
    [DataMember(Name = "value")] 
    private readonly long m_value = 0;

    public bool IsNull => m_value == 0;
    public long AsId { get => m_value; init => m_value = value; }
    public int AsIndex { get => checked((int)m_value) - 1; init => m_value = value + 1; }
    public int CompareTo(ItemID other) => m_value.CompareTo(other.m_value);
    public bool Equals(ItemID other) => m_value.Equals(other.m_value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ItemID id && Equals(id);
    public override int GetHashCode() => m_value.GetHashCode();
    public override string ToString() => $"ItemID: {m_value}";
}

/// <summary>
/// A Item with an ID associated with it
/// </summary>
[DataContract]
public struct KeyedItem : INullable
{
    /// <summary>
    /// Create a deafult, null KeyedItem
    /// </summary>
    public KeyedItem()
    {
        ID = new();
        Item = null!; // Todo: Class default item?
    }

    /// <summary>
    /// Create a keyed item with the given item and ID
    /// </summary>
    public KeyedItem(ItemID id, Item item)
    {
        ID = id;
        Item = item;
    }

    /// <summary>
    /// Unique ID of the Item. IDs range from 1 to 2^53-1.
    /// </summary>
    [DataMember(Name = "id")] public readonly ItemID ID;

    /// <summary>
    /// The Item object with the given ID
    /// </summary>
    [DataMember(Name = "item")] public readonly Item Item;

    /// <summary>
    /// True if the item is null, false otherwise
    /// </summary>
    public bool IsNull => ID.IsNull;
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

        /// <summary>
        /// This item is present / obtainable in the current expeditions list
        /// </summary>
        IsInRequiredExpeditions = 1 << 10,
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
    /// Set or write the IsInRequiredExpeditions bit
    /// </summary>
    public bool IsInRequiredExpeditions
    {
        get => (m_value & eType.IsInRequiredExpeditions) != 0;
        init => m_value = value ? (m_value | eType.IsInRequiredExpeditions) : (m_value & ~eType.IsInRequiredExpeditions);
    }

    /// <summary>
    /// Returns true if this item should, on its own merits, be randomized.
    /// </summary>
    public bool ShouldBeRandomized => IsInRequiredExpeditions && IsWhitelisted && !IsBlacklisted;

    /// <summary>
    /// Get a copy without randomization-specific data
    /// </summary>
    public ItemData AsNew => new(m_value & ~(eType.IsWhitelisted | eType.IsBlacklisted | eType.IsInRequiredExpeditions));
}