using System;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Collections.Generic;

/// <summary>
/// Represents a location in archipelago. Some example locations:
/// <list type="bullet">
///  <item>Key spawn locations</item>
///  <item>Big pickup (e.g. cell) spawn locations</item>
///  <item>Event triggers (split into sub-locations, one for each event action in the chain)</item>
///  <item>Objective items / logical locations (e.g. extraction can't actually be picked up, but has a location)</item>
/// </list>
/// In GTFO, locations are considered reachable if and only if all regions they can be located in are reachable.
/// Note that in actual gameplay, it is still possible to reach locations without access to all possible regions.
/// </summary>
[DataContract]
public class Location
{
    /// <summary>
    /// Standard constructor
    /// </summary>
    /// <param name="regions">
    /// The regions the location can be found in. 
    /// Archipelago will require all listed regions be reachable for this location to be reachable.
    /// </param>
    /// <param name="randData">The data used to randomize this location</param>
    /// <param name="item">The item normally found in this location, if any</param>
    public Location(RegionList regions, LocationData randData, ItemID item = new())
    {
        m_owningRegionIDs = regions;
        RandData = randData;
        ItemID = item;
    }

    /// <summary>
    /// Regions this location can be in.
    /// </summary>
    [DataMember(Name = "owning_regions")]
    public IReadOnlyList<RegionID> OwningRegionIDs => m_owningRegionIDs;
    private RegionID[] m_owningRegionIDs;

    /// <summary>
    /// The data to use for this location.
    /// </summary>
    [DataMember(Name = "rand_data")]
    public LocationData RandData { get; private set; }

    /// <summary>
    /// Item typically located in this location. 
    /// If 0, this location will be a candidate for floating items.
    /// </summary>
    [DataMember(Name = "item_id")]
    public ItemID ItemID { get; private set; } = new();

    /// <summary>
    /// Name of the scouted item, if this location has been scouted
    /// </summary>
    public string? ScoutedItemName { get; set; }

    /// <summary>
    /// Name of the player for the scouted item, if this location has been scouted
    /// </summary>
    public string? ScoutedPlayerName { get; set; }

    /// <summary>
    /// Name of the game for the scouted item, if this location has been scouted
    /// </summary>
    public string? ScoutedGameName { get; set; }

    /// <summary>
    /// Add a new region to the OwningRegionIDs list
    /// </summary>
    public void AddOwningRegionIDs(params RegionID[] regions)
    {
        if (regions.Length == 0) return;
        RegionID[] ids = new RegionID[m_owningRegionIDs.Length + regions.Length];
        for (int i = 0; i < m_owningRegionIDs.Length; i++)
            ids[i] = m_owningRegionIDs![i];
        for (int i = 0; i < ids.Length; i++)
            ids[m_owningRegionIDs.Length + i] = regions[i];
        m_owningRegionIDs = ids;
    }

    /// <summary>
    /// Update the randomization of this location
    /// </summary>
    public void UpdateRandomization(bool isReachable, bool isWhitelisted, bool isBlacklisted, bool isRandomized, bool isRandomlike)
    {
        if (isRandomized && isRandomlike)
            throw new ArgumentException("Cannot set location data to be both randomized and randomlike!");
        RandData = new(RandData)
        {
            IsReachable = isReachable,
            IsWhitelisted = isWhitelisted,
            IsBlacklisted = isBlacklisted,
            IsRandomized = isRandomized,
            IsRandomlike = isRandomlike
        };
    }

    /// <summary>
    /// Set specifically the IsReachable bit in the rand data
    /// </summary>
    public void UpdateReachable(bool isReachable)
        => RandData = new(RandData) { IsReachable = isReachable };

    /// <summary>
    /// Set specifically the IsWhitelisted and IsBlacklisted bits in the rand data
    /// </summary>
    public void UpdateListing(bool isWhitelisted, bool isBlacklisted)
        => RandData = new(RandData) { IsWhitelisted = isWhitelisted, IsBlacklisted = isBlacklisted };

    /// <summary>
    /// Set specifically the IsRandomized and IsRandomlike bits in the rand data
    /// </summary>
    public void UpdateRandomized(bool isRandomized, bool isRandomlike)
    {
        if (isRandomized && isRandomlike)
            throw new ArgumentException("Cannot set location data to be both randomized and randomlike!");
        RandData = new(RandData) { IsRandomized = isRandomized, IsRandomlike = isRandomlike };
    }

    /// <summary>
    /// Change the assigned item for this location.
    /// Only possible if this location's RandData indicates it's an empty location
    /// </summary>
    /// <param name="newItem"></param>
    public void SetItem(ItemID newItem)
    {
        if (!RandData.IsEmpty)
            throw new InvalidOperationException("Cannot overwrite item for a non-empty location!");
        ItemID = newItem;
    }
}

/// <summary>
/// Simple declaration to help identify IDs for tags
/// </summary>
[DataContract]
public struct LocationID : ITagID, IEquatable<LocationID>, IComparable<LocationID>
{
    [DataMember(Name = "id")]
    public uint ID { get; init; }

    public bool IsNull => ID == 0;
    public int AsIndex { get => checked((int)ID - 1); init => ID = unchecked((uint)value + 1u); }
    public bool Equals(LocationID other) => ID == other.ID;
    public int CompareTo(LocationID other) => ID.CompareTo(other.ID);
    public override string ToString() => $"LocationID {ID}";

    public static LocationID From(Game.Data data, string name, Func<TagDefinition<LocationID>> definitionFactory, Location? item = null)
        => data.Locations.LookUpOrCreate(name, definitionFactory, item);

    public static LocationID From<TData>(TData data, string name, Func<TData, TagDefinition<LocationID>> definitionFactory, Location? item = null) where TData : Game.Data
        => data.Locations.LookUpOrCreate(data, name, definitionFactory, item);

    public static LocationID From<TData>(TData data, string name, Func<TData, TagDefinition<LocationID>> definitionFactory, RegionList regions, LocationData randData) where TData : Game.Data
        => data.Locations.LookUpOrCreate(data, name, definitionFactory, new Location(regions, randData));
}

/// <summary>
/// Simple wrapper around some enum values
/// </summary>
[DataContract]
public struct LocationData
{
    /// <summary>
    /// Enum values used by this data
    /// </summary>
    [Flags]
    public enum eType
    {
        /// <summary>
        /// Mask used to filter to the priority mode of the location
        /// </summary>
        PriorityMask = 0x03,

        /// <summary>
        /// Default priority used by most locations; can contain any item
        /// </summary>
        Default = 0,

        /// <summary>
        /// Prioritized locations will always be filled; first with progression items, then with other items as needed. 
        /// Usually there are enough progression items to guarantee a progression item fills the spot.
        /// </summary>
        Priority = 1,

        /// <summary>
        /// Exluded locations will never contain progression items; when GTFO is filling empty locations,
        /// excluded locations will be filled only with filler items.
        /// </summary>
        Excluded = 2,

        /// <summary>
        /// Same as exluded, but when GTFO is filling empty locations this will prefer trap items.
        /// </summary>
        Trap = 3,

        /// <summary>
        /// If this bit is set, the location will be automatically discovered when all its containing regions
        /// are discovered. Note that not all regions are discoverable, as not all regions have associated checks
        /// which will notify StateTracker that they are found.
        /// </summary>
        AutoDiscover = 1 << 2,

        /// <summary>
        /// If this bit is set, the location is considered "empty". When the location is created, its Item ID will
        /// be ignored (and, in general, should be left as the default "null" value).
        /// </summary>
        IsEmpty = 1 << 3,

        /// <summary>
        /// This location is in the randomization whitelist
        /// </summary>
        IsWhitelisted = 1 << 4,

        /// <summary>
        /// This location is in the randomization blacklist
        /// </summary>
        IsBlacklisted = 1 << 5,

        /// <summary>
        /// This location is located in a whitelisted region
        /// </summary>
        IsReachable = 1 << 6,

        /// <summary>
        /// If this location has an item and both meet the criteria to be randomized
        /// </summary>
        IsRandomized = 1 << 7,

        /// <summary>
        /// If this location has an item and is not randomized and the item is randomlike
        /// </summary>
        IsRandomlike = 1 << 8,
    }

    /// <summary>
    /// Construct location data; optionally provide its starting value
    /// </summary>
    public LocationData(eType value = eType.Default) => m_value = value;

    /// <summary>
    /// Copy constructor
    /// </summary>
    /// <param name="source"></param>
    public LocationData(LocationData source) => m_value = source.m_value;

    /// <summary>
    /// The stored location data
    /// </summary>
    private readonly eType m_value = eType.Default;

    /// <summary>
    /// Extract or write the priority mode for this data
    /// </summary>
    [DataMember(Name = "priority_mode")]
    public eType PriorityMode
    {
        get => m_value & eType.PriorityMask;
        init => m_value = (m_value & ~eType.PriorityMask) | value;
    }

    /// <summary>
    /// True if this location's progression priority is default
    /// </summary>
    public bool IsDefault => PriorityMode == eType.Default;

    /// <summary>
    /// True if this location's progression priority is 'Priority'
    /// </summary>
    public bool IsPriority => PriorityMode == eType.Priority;

    /// <summary>
    /// True if this location is excluded from progression items
    /// </summary>
    public bool IsExcluded => PriorityMode == eType.Excluded;

    /// <summary>
    /// True if this location is to be treated as a trap placement
    /// </summary>
    public bool IsTrap => PriorityMode == eType.Trap;

    /// <summary>
    /// Extract or write the AutoDiscover mode for this data
    /// </summary>
    public bool IsAutoDiscovered
    {
        get => (m_value & eType.AutoDiscover) != 0;
        init => m_value = value ? (m_value | eType.AutoDiscover) : (m_value & eType.AutoDiscover);
    }

    /// <summary>
    /// Extract or write the IsEmpty mode for this data
    /// </summary>
    [DataMember(Name = "is_empty")]
    public bool IsEmpty
    {
        get => (m_value & eType.IsEmpty) != 0;
        init => m_value = value ? (m_value | eType.IsEmpty) : (m_value & eType.IsEmpty);
    }

    /// <summary>
    /// Get or write the IsWhitelisted bit
    /// </summary>
    public bool IsWhitelisted
    {
        get => (m_value & eType.IsWhitelisted) != 0;
        init => m_value = value ? (m_value | eType.IsWhitelisted) : (m_value & ~eType.IsWhitelisted);
    }

    /// <summary>
    /// Get or write the IsBlacklisted bit
    /// </summary>
    public bool IsBlacklisted
    {
        get => (m_value & eType.IsBlacklisted) != 0;
        init => m_value = value ? (m_value | eType.IsBlacklisted) : (m_value & ~eType.IsBlacklisted);
    }

    /// <summary>
    /// Get or write the IsReachable bit
    /// </summary>
    public bool IsReachable
    {
        get => (m_value & eType.IsReachable) != 0;
        init => m_value = value ? (m_value | eType.IsReachable) : (m_value & ~eType.IsReachable);
    }

    /// <summary>
    /// True if this location should be randomized purely on its own merit (ignoring its item)
    /// </summary>
    public bool ShouldBeRandomized => IsReachable && IsWhitelisted && !IsBlacklisted;

    /// <summary>
    /// Get or write the IsRandomized bit
    /// </summary>
    public bool IsRandomized
    {
        get => (m_value & eType.IsRandomized) != 0;
        init => m_value = value ? (m_value | eType.IsRandomized) : (m_value & ~eType.IsRandomized);
    }

    /// <summary>
    /// Get or write the IsRandomlike bit
    /// </summary>
    public bool IsRandomlike
    {
        get => (m_value & eType.IsRandomlike) != 0;
        init => m_value = value ? (m_value | eType.IsRandomlike) : (m_value & ~eType.IsRandomlike);
    }

    /// <summary>
    /// True if IsRandomized or IsRandomlike. If testing whether to perform randomization
    /// behaviour in patches, this is typically the check that should be used.
    /// </summary>
    public bool IsTreatedAsRandom => IsRandomized || IsRandomlike;

    public LocationData AsNew => new(m_value & ~(eType.IsWhitelisted | eType.IsBlacklisted | eType.IsReachable | eType.IsRandomized | eType.IsRandomlike));
}
