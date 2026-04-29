using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Represents a region in archipelago. Some examples of regions:
/// <list type="bullet">
///  <item>The main menu</item>
///  <item>Each zone</item>
///  <item>Terminals in zones</item>
///  <item>Objective steps (objectives are built out as traversable graphs)</item>
/// </list>
/// </summary>
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
        ConnectedLocationIds = other.ConnectedLocationIds; // Note that this copies elements to our owned list
    }

    /// <summary>
    /// Unique name of the region, used to identify it
    /// </summary>
    public string Name { get; private init; }

    /// <summary>
    /// Whether this region is reachable, typically populated during the graph traversal checks.
    /// 
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
    private List<PathID> m_connectedPaths = new();

    /// <summary>
    /// Locations that can be discovered in this region.
    /// During randomization, locations are considered discoverable if and only if all regions they can be in are discoverable.
    /// </summary>
    public IReadOnlyCollection<LocationID> ConnectedLocationIds 
    { 
        get => m_connectedLocationIds; 
        init => m_connectedLocationIds.AddRange(value); 
    }
    private List<LocationID> m_connectedLocationIds = new();

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
        if (m_connectedLocationIds.Contains(locationID))
            FeatureLogger.Error($"Cannot add duplicate location {locationID.Value} to region: {Name}");
        else
            m_connectedLocationIds.Add(locationID);
    }

    /// <summary>
    /// Called at the end of processing to trim lists
    /// </summary>
    public void CleanUp()
    {
        m_connectedPaths.TrimExcess();
        m_connectedLocationIds.TrimExcess();
    }
}

/// <summary>
/// Simple wrapper around a int to help identify it as a RegionID, usable
///  for looking up a Region instance in GameData.
/// </summary>
public struct RegionID : INullable, IIndex, IComparable<RegionID>, IEquatable<RegionID>
{
    public RegionID() { }
    public RegionID(int value) => Value = value;
    public readonly int Value = 0;

    public bool IsNull => Value == 0;
    public int AsIndex { get => Value - 1; init => Value = value + 1; }
    public int CompareTo(RegionID other) => Value.CompareTo(other.Value);
    public bool Equals(RegionID other) => Value.Equals(other.Value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RegionID id && Equals(id);
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>
/// A Region with an ID associated with it
/// </summary>
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
    public readonly RegionID ID;

    /// <summary>
    /// The Region object
    /// </summary>
    public ReadOnlyRegion Region;

    public bool IsNull => ID.IsNull;
}

/// <summary>
/// A variation of region which is readonly
/// </summary>
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
    private Region m_region;

    /// <inheritdoc cref="Region.Name"/>
    public string Name => m_region.Name;

    /// <inheritdoc cref="Region.Reachable"/>
    public bool Reachable => m_region.Reachable;

    /// <inheritdoc cref="Region.ConnectedPaths"/>
    public IReadOnlyCollection<PathID> ConnectedPaths => m_region.ConnectedPaths;

    /// <inheritdoc cref="Region.ConnectedLocationIds"/>
    public IReadOnlyCollection<LocationID> ConnectedLocationIds => m_region.ConnectedLocationIds;
}