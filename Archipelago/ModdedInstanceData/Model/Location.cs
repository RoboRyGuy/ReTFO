using Archipelago.MultiClient.Net.Models;
using ReTFO.Archipelago.Utilities;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.ModdedInstanceData.Processors;

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
    /// <param name="nameTag">Name of the location</param>
    /// <param name="regions">
    /// The regions the location can be found in. 
    /// Archipelago will require all listed regions be reachable for this location to be reachable.
    /// </param>
    /// <param name="randData">The data used to randomize this location</param>
    public Location(RandomizationTag nameTag, RegionList regions, LocationData randData)
    {
        NameTag = nameTag;
        OwningRegionIDs = regions;
        RandData = randData;
    }

    /// <summary>
    /// Identifying tag used by this locations
    /// </summary>
    [DataMember(Name = "name_tag")]
    public RandomizationTag NameTag { get; init; }

    /// <summary>
    /// Optional secondary tag for this location.
    /// </summary>
    [DataMember(Name = "tag2")]
    public RandomizationTag Tag2 { get; init; }

    /// <summary>
    /// Optional tertiary tag for this location.
    /// </summary>
    [DataMember(Name = "tag3")]
    public RandomizationTag Tag3 { get; init; }

    /// <summary>
    /// Regions this location can be in.
    /// </summary>
    [DataMember(Name = "owning_regions")]
    public RegionID[] OwningRegionIDs { get; init; }

    /// <summary>
    /// Item typically located in this location. 
    /// If 0, this location will be a candidate for floating items.
    /// </summary>
    [DataMember(Name = "item_id")]
    public ItemID ItemID { get; set; } = new();

    /// <summary>
    /// The data to use for this location.
    /// </summary>
    [DataMember(Name = "rand_data")]
    public LocationData RandData { get; set; }

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
}

/// <summary>
/// Simple wrapper around a long to help identify it as a LocationID, usable
///  for looking up a Location instance in GameData.
/// </summary>
[DataContract]
public struct LocationID : INullable, IId, IIndex, IComparable<LocationID>, IEquatable<LocationID>
{
    public LocationID() { }
    [DataMember(Name = "value")] 
    private readonly long m_value = 0;

    public bool IsNull => m_value == 0;
    public long AsId { get => m_value; init => m_value = value; }
    public int AsIndex { get => checked((int)m_value) - 1; init => m_value = value + 1; }
    public int CompareTo(LocationID other) => m_value.CompareTo(other.m_value);
    public bool Equals(LocationID other) => m_value.Equals(other.m_value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is LocationID id && Equals(id);
    public override int GetHashCode() => m_value.GetHashCode();
    public override string ToString() => $"LocationID: {m_value}";
}

/// <summary>
/// A Location with an ID associated with it
/// </summary>
[DataContract]
public struct KeyedLocation : INullable
{
    /// <summary>
    /// Create a new null KeyedLocation
    /// </summary>
    public KeyedLocation()
    {
        ID = new();
        Location = null!;
    }

    /// <summary>
    /// Create a new KeyedLocation with the given location and ID
    /// </summary>
    public KeyedLocation(LocationID id, Location location)
    {
        ID = id;
        Location = location;
    }

    /// <summary>
    /// Unique ID of the Location. IDs range from 1 to 2^53-1.
    /// </summary>
    [DataMember(Name = "id")] public LocationID ID { get; init; }

    /// <summary>
    /// The location object with the given ID
    /// </summary>
    [DataMember(Name = "location")] public Location Location { get; init; }

    /// <summary>
    /// True if null (contains no location)
    /// </summary>
    public bool IsNull => ID.IsNull;
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
        /// This location is present / obtainable in the current expeditions list
        /// </summary>
        IsInRequiredExpeditions = 1 << 6,

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
    /// Get or write the IsInRequiredExpeditions bit
    /// </summary>
    public bool IsInRequiredExpeditions
    {
        get => (m_value & eType.IsInRequiredExpeditions) != 0;
        init => m_value = value ? (m_value | eType.IsInRequiredExpeditions) : (m_value & ~eType.IsInRequiredExpeditions);
    }

    /// <summary>
    /// True if this location should be randomized purely on its own merit (ignoring its item)
    /// </summary>
    public bool ShouldBeRandomized => IsInRequiredExpeditions && IsWhitelisted && !IsBlacklisted;

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

    public LocationData AsNew => new(m_value & ~(eType.IsWhitelisted | eType.IsBlacklisted | eType.IsInRequiredExpeditions | eType.IsRandomized | eType.IsRandomlike));
}
