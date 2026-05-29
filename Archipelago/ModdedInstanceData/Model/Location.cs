using Archipelago.MultiClient.Net.Models;
using ReTFO.Archipelago.Utilities;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

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
    public LocationData RandData { get; init; }

    /// <summary>
    /// Currrent randomization mode of this item, with some added data
    /// </summary>
    public RandTest RandMode { get; set; }

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
    /// True if null (contains no location)
    /// </summary>
    public bool IsNull => ID.IsNull;

    /// <summary>
    /// The location object with the given ID
    /// </summary>
    [DataMember(Name = "location")] public Location Location { get; init; }

    // Below are helper for accessing data in the location

    /// <inheritdoc cref="Location.NameTag"/>
    public RandomizationTag NameTag => Location.NameTag;

    /// <inheritdoc cref="Location.OwningRegionIDs"/>
    public RegionID[] OwningRegionIds => Location.OwningRegionIDs;

    /// <inheritdoc cref="Location.ItemID"/>
    public ItemID ItemID => Location.ItemID;

    /// <inheritdoc cref="Location.RandData"/>
    public LocationData RandData => Location.RandData;

    /// <inheritdoc cref="Location.ScoutedItem"/>
    public ScoutedItemInfo? ScoutedItem => ScoutedItem;
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
    }

    /// <summary>
    /// Construct default location data
    /// </summary>
    public LocationData() { }

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
        init
        {
            if (value != (value & eType.PriorityMask)) throw new ArgumentException("Value assigned to PriorityMode must be a priority type!");
            m_value = value | (m_value & ~eType.PriorityMask);
        }
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
        init
        {
            if (value) m_value |= eType.AutoDiscover;
            else m_value &= ~eType.AutoDiscover;
        }
    }

    /// <summary>
    /// Extract or write the IsEmpty mode for this data
    /// </summary>
    [DataMember(Name = "is_empty")]
    public bool IsEmpty
    {
        get => (m_value & eType.IsEmpty) != 0;
        init
        {
            if (value) m_value |= eType.IsEmpty;
            else m_value &= ~eType.IsEmpty;
        }
    }
}
