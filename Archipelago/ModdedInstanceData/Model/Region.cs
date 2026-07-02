using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using BepInEx;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Linq;

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
public readonly struct Region
{
    /// <summary>
    /// Create a new region.
    /// </summary>
    public Region() { }

    /// <summary>
    /// Copy constructor
    /// </summary>
    public Region(Region other)
    {
        Reachable = other.Reachable;
        ConnectedPaths = other.ConnectedPaths;         // Note that this copies elements to our owned array
        ConnectedLocations = other.ConnectedLocations; // Note that this copies elements to our owned array
        RegionData = other.RegionData;
    }

    /// <summary>
    /// Whether this region is reachable, typically populated during the graph traversal checks.
    /// </summary>
    public bool Reachable { get; init; } = false;

    /// <summary>
    /// All paths starting in this region
    /// </summary>
    public IReadOnlyList<PathID> ConnectedPaths 
    { 
        get => m_connectedPaths ?? []; 
        init => m_connectedPaths = value.ToArray();
    }
    private readonly PathID[]? m_connectedPaths = null;

    /// <summary>
    /// Locations that can be discovered in this region.
    /// During randomization, locations are considered discoverable if and only if all regions they can be in are discoverable.
    /// </summary>
    public IReadOnlyList<LocationID> ConnectedLocations 
    { 
        get => m_connectedLocations ?? []; 
        init => m_connectedLocations = value.ToArray();
    }
    private readonly LocationID[]? m_connectedLocations = null;

    /// <summary>
    /// Custom region data, typically used by game data or similar
    /// </summary>
    public object? RegionData { get; init; }

    /// <summary>
    /// Creates a new region with the requested reachability
    /// </summary>
    public Region WithReachable(bool newValue)
        => new(this) { Reachable = newValue };

    /// <summary>
    /// Creates a new region with the listed paths added to its connected paths array
    /// </summary>
    public Region WithAdded(params PathID[] paths)
    {
        if (paths.Length == 0) return new(this);
        int existingCount = m_connectedPaths?.Length ?? 0;
        PathID[] ids = new PathID[existingCount + paths.Length];
        for (int i = 0; i < existingCount; i++)
            ids[i] = m_connectedPaths![i];
        for (int i = 0; i < paths.Length; i++)
            ids[existingCount + i] = paths[i];
        return new(this) { ConnectedPaths = ids };
    }

    /// <summary>
    /// Creates a new region with the listed paths added to its locations array
    /// </summary>
    public Region WithAdded(params LocationID[] locations)
    {
        if (locations.Length == 0) return new(this);
        int existingCount = m_connectedLocations?.Length ?? 0;
        LocationID[] ids = new LocationID[existingCount + locations.Length];
        for (int i = 0; i < existingCount; i++)
            ids[i] = m_connectedLocations![i];
        for (int i = 0; i < locations.Length; i++)
            ids[existingCount + i] = locations[i];
        return new(this) { ConnectedLocations = ids };
    }

    /// <summary>
    /// Helper to get extract the custom data from this region type-safely and to fail if it's the wrong type
    /// </summary>
    public T GetData<T>() where T : class
        => (RegionData as T) ?? throw new NullReferenceException();

    /// <summary>
    /// Helper to extract custom data from this region type-safely.
    /// If the stored data is null, returns false; if the stored data is non-null but cannot
    ///  be cast to the requested type, throws; else, returns true and sets the result to the value.
    /// </summary>
    public bool GetDataAllowNull<T>([MaybeNullWhen(false)] out T result) where T : class
    {
        if (RegionData == null)
        {
            result = null;
            return false;
        }
        else
        {
            result = RegionData as T
                ?? throw new InvalidCastException($"Cannot cast region data from {RegionData.GetType().FullName} to {typeof(T).FullName}");
            return true;
        }
    }

    /// <summary>
    /// Try to cast the RegionData to the requested type; returns true if 
    ///  successful, false if the data is null or cannot be cast
    /// </summary>
    public bool TryGetData<T>([NotNullWhen(true)] out T? result) where T : class
        => (RegionData is T test ? (true, result = test) : (false, result = null)).Item1;
}

/// <summary>
/// Simple wrapper around a int to help identify it as a RegionID, usable
///  for looking up a Region instance in GameData.
/// </summary>
[DataContract]
public struct RegionID : ITagID, IEquatable<RegionID>, IComparable<RegionID>
{
    [DataMember(Name = "id")]
    public uint ID { get; init; }

    public bool IsNull => ID == 0;
    public int AsIndex { get => checked((int)ID - 1); init => ID = unchecked((uint)value + 1u); }
    public bool Equals(RegionID other) => ID == other.ID;
    public int CompareTo(RegionID other) => ID.CompareTo(other.ID);
    public override string ToString() => $"RegionID {ID}";

    public static RegionID From(Game.Data data, string name, Func<TagDefinition<RegionID>> definitionFactory, Region item = default)
        => data.Regions.LookUpOrCreate(name, definitionFactory, item);

    public static RegionID From<TData>(TData data, string name, Func<TData, TagDefinition<RegionID>> definitionFactory, Region item = default) where TData : Game.Data
        => data.Regions.LookUpOrCreate(data, name, definitionFactory, item);
}