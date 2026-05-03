using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// Represents a region in archipelago. Some examples of regions:
/// <list type="bullet">
///  <item>The main menu</item>
///  <item>Each zone</item>
///  <item>Terminals in zones</item>
///  <item>Objective steps (objectives are built out as traversable graphs)</item>
/// </list>
/// </summary>
[DataContract]
public struct Region
{
    /// <summary>
    /// Create a new region by name.
    /// Typically, prefer using <see cref="Game.Data.LookupOrCreateRegion"/>
    /// </summary>
    /// <param name="name"></param>
    public Region(string name) { Name = name; }
    
    /// <summary>
    /// Copy constructor
    /// </summary>
    public Region(Region other)
    {
        Name = other.Name;
        Reachable = other.Reachable;
        ConnectedPaths = other.ConnectedPaths;             // Note that this copies elements to our owned list
        ConnectedLocations = other.ConnectedLocations; // Note that this copies elements to our owned list
    }

    /// <summary>
    /// Unique name of the region, used to identify it
    /// </summary>
    [DataMember]
    public string Name { get; private init; }

    /// <summary>
    /// Whether this region is reachable, typically populated during the graph traversal checks.
    /// </summary>
    public bool Reachable { get; set; } = false;

    /// <summary>
    /// All paths starting in this region
    /// </summary>
    public IReadOnlyCollection<PathID> ConnectedPaths 
    { 
        get => m_connectedPaths; 
        init => m_connectedPaths.AddRange(value);
    }
    [DataMember(Name = "ConnectedPaths")]
    private List<PathID> m_connectedPaths = new();

    /// <summary>
    /// Locations that can be discovered in this region.
    /// During randomization, locations are considered discoverable if and only if all regions they can be in are discoverable.
    /// </summary>
    public IReadOnlyCollection<LocationID> ConnectedLocations 
    { 
        get => m_connectedLocations; 
        init => m_connectedLocations.AddRange(value);
    }
    private List<LocationID> m_connectedLocations = new();

    /// <summary>
    /// Add a path to the connected paths list. Note that this cannot be removed later
    /// </summary>
    public void AddPath(PathID pathID)
    {
        if (m_connectedPaths.Contains(pathID))
            FeatureLogger.Error($"Cannot add duplicated path {pathID} to region: {Name}");
        else
            m_connectedPaths.Add(pathID);
    }

    /// <summary>
    /// Add a location to the location IDs list. Note that this cnanot be removed later
    /// </summary>
    public void AddLocation(LocationID locationID)
    {
        if (m_connectedLocations.Contains(locationID))
            FeatureLogger.Error($"Cannot add duplicate location {locationID.AsId} to region: {Name}");
        else
            m_connectedLocations.Add(locationID);
    }

    /// <summary>
    /// Called at the end of processing to trim lists
    /// </summary>
    public void CleanUp()
    {
        m_connectedPaths.TrimExcess();
        m_connectedLocations.TrimExcess();
    }
}

/// <summary>
/// Simple wrapper around a int to help identify it as a RegionID, usable
///  for looking up a Region instance in GameData.
/// </summary>
[DataContract]
public struct RegionID : INullable, IId, IIndex, IComparable<RegionID>, IEquatable<RegionID>
{
    public RegionID() { }
    [DataMember(Name = "Value")] 
    private readonly long m_value = 0;

    public bool IsNull => m_value == 0;
    public long AsId { get => m_value; init => m_value = value; }
    public int AsIndex { get => checked((int)m_value) - 1; init => m_value = value + 1; }
    public int CompareTo(RegionID other) => m_value.CompareTo(other.m_value);
    public bool Equals(RegionID other) => m_value.Equals(other.m_value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RegionID id && Equals(id);
    public override int GetHashCode() => m_value.GetHashCode();
}

/// <summary>
/// A Region with an ID associated with it
/// </summary>
[DataContract]
public struct KeyedRegion : INullable
{
    /// <summary>
    /// Create a deafult, null KeyedRegion
    /// </summary>
    public KeyedRegion()
    {
        ID = new();
        Region = new();
    }

    /// <summary>
    /// Create a keyed item with the given item and ID
    /// </summary>
    public KeyedRegion(RegionID id, ReadOnlyRegion region)
    {
        ID = id;
        Region = region;
    }

    /// <summary>
    /// Unique ID of the Region
    /// </summary>
    [DataMember] public readonly RegionID ID;

    /// <summary>
    /// The Region object
    /// </summary>
    [DataMember] public ReadOnlyRegion Region;

    public bool IsNull => ID.IsNull;
}

/// <summary>
/// A variation of region which is readonly
/// </summary>
[DataContract]
public struct ReadOnlyRegion
{
    /// <summary>
    /// Create a new read-only region wrapping the provided region
    /// </summary>
    public ReadOnlyRegion(Region source) => m_region = source;

    /// <summary>
    /// Implicitly construct a new ReadOnlyRegion from the provided region
    /// </summary>
    public static implicit operator ReadOnlyRegion(Region source) => new(source);

    /// <summary>
    /// Create a mutable copy of the contained region
    /// </summary>
    public Region MakeMutable() => new Region(m_region);

    /// <summary>
    /// Contained region
    /// </summary>
    [DataMember(Name = "ContainedRegion")] 
    private Region m_region;

    /// <inheritdoc cref="Region.Name"/>
    public string Name => m_region.Name;

    /// <inheritdoc cref="Region.Reachable"/>
    public bool Reachable => m_region.Reachable;

    /// <inheritdoc cref="Region.ConnectedPaths"/>
    public IReadOnlyCollection<PathID> ConnectedPaths => m_region.ConnectedPaths;

    /// <inheritdoc cref="Region.ConnectedLocations"/>
    public IReadOnlyCollection<LocationID> ConnectedLocationIds => m_region.ConnectedLocations;
}