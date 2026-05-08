using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Represents a directed path between two regions, implying an entrance and exit which connect the two regions.
/// <br />
/// Examples of a path:
/// <list type="bullet">
///  <item>Sec and bulkhead doors which connect zones</item>
///  <item>Getting acess to a zone's terminal (since it may be locked)</item>
///  <item>Teleporting between dimensions</item>
///  <item>Progressing or completing an objective</item>
/// </list>
/// </summary>
[DataContract]
public struct Path : INullable
{
    /// <summary>
    /// Constructs a default (null) path
    /// </summary>
    public Path() { }

    /// <summary>
    /// Simple requires need to traverse a path
    /// </summary>
    [DataContract]
    public struct RequiredItem : INullable
    {
        /// <summary>
        /// Type of requirements possibly needed
        /// </summary>
        public enum eType
        {
            /// <summary>
            /// No required item; not assigned. Default value
            /// </summary>
            None,

            /// <summary>
            /// Requires a certain number of items
            /// </summary>
            Item,

            /// <summary>
            /// Requires a certain number of items, all of which are part of a specified category
            /// </summary>
            Category,
        }

        /// <summary>
        /// Default constructor; constructs a null RequiredItem
        /// </summary>
        public RequiredItem() { }

        /// <summary>
        /// Constructs a path requirements struct using the given target and target type
        /// </summary>
        public RequiredItem(eType type, RandomizationTag target)
        {
            Type = type;
            Target = target;
        }

        /// <summary>
        /// The type of requirement this represents
        /// </summary>
        [DataMember]
        public eType Type { get; init; } = eType.None;

        /// <summary>
        /// The tag utilized to identify the target
        /// </summary>
        [DataMember]
        public RandomizationTag Target { get; init; } = new();

        public bool IsNull => Type == eType.None;

    }

    /// <summary>
    /// Comparer for two Paths using their starting region as the key
    /// </summary>
    public class ByStartingRegionComparer : IComparer<Path>
    {
        public int Compare(Path x, Path y)
        {
            int value = x.StartingRegion.CompareTo(y.StartingRegion);
            if (value == 0) value = x.EndingRegion.CompareTo(y.EndingRegion);
            return value;
        }
    }

    /// <summary>
    /// Comparer for two regions using their ending region as the key
    /// </summary>
    public class ByEndingRegionComparer : IComparer<Path>
    {
        public int Compare(Path x, Path y)
        {
            int value = x.EndingRegion.CompareTo(y.EndingRegion);
            if (value == 0) value = x.StartingRegion.CompareTo(y.StartingRegion);
            return value;
        }
    }

    /// <summary>
    /// Optional name for this path
    /// </summary>
    [DataMember]
    public string? Name { get; set; } = null;

    /// <summary>
    /// Region this path starts in
    /// </summary>
    [DataMember]
    public RegionID StartingRegion { get; set; } = new();

    /// <summary>
    /// Region this path ends in
    /// </summary>
    [DataMember]
    public RegionID EndingRegion { get; set; } = new();

    /// <summary>
    /// Requirements for accessing this path
    /// </summary>
    [DataMember]
    public RequiredItem ReqItem { get; set; } = new();

    /// <summary>
    /// How many ReqItems are needed to traverse this path.
    /// </summary>
    [DataMember]
    public uint ReqCount { get; set; } = 0;

    /// <summary>
    /// Alternate item required to traverse this path
    /// <list type="=bullet">
    ///  <item>If ReqItem.Isnull, then this is ignored (by design)</item>
    ///  <item>The alternate item is assumed to only require one count to traverse the path; it is assumed to be a uniuqe item</item>
    ///  <item>This is intended for situations such as door unlock events (since all zone doors can be force unlocked via an event)</item>
    /// </list>
    /// </summary>
    [DataMember]
    public RequiredItem AlternateItem { get; set; } = new();

    /// <summary>
    /// If this path is null. Considered true if the starting and ending region are the same.
    /// </summary>
    public bool IsNull => StartingRegion.Equals(EndingRegion);

    /// <summary>
    /// Helper for visualizing in debugger
    /// </summary>
    public override string ToString() => $"{StartingRegion} => {EndingRegion}";
}

/// <summary>
/// Simple wrapper around a int to help identify it as a PathID, usable
///  for looking up a Path instance in GameData.
/// </summary>
[DataContract]
public struct PathID : INullable, IId, IIndex, IComparable<PathID>, IEquatable<PathID>
{
    public PathID () { }
    [DataMember(Name = "Value")] 
    private readonly long m_value = 0;

    public bool IsNull => m_value == 0;
    public long AsId { get => m_value; init => m_value = value; }
    public int AsIndex { get => checked((int)m_value) - 1; init => m_value = value + 1; }
    public int CompareTo(PathID other) => m_value.CompareTo(other.m_value);
    public bool Equals(PathID other) => m_value.Equals(other.m_value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PathID id && Equals(id);
    public override int GetHashCode() => m_value.GetHashCode();
    public override string ToString() => $"PathID: {m_value}";
}

/// <summary>
/// Wrapper around a path struct which makes it immutable.
/// This is often used as a return value where modifying it wouldn't make sense.
/// </summary>
[DataContract]
public struct ReadOnlyPath : INullable
{
    /// <summary>
    /// Constructs a new ReadOnlyPath wrapping around the source path object.
    /// </summary>
    /// <param name="source"></param>
    public ReadOnlyPath(Path source) => m_path = source;

    /// <summary>
    /// Internal path which should be treated as immutable
    /// </summary>
    [DataMember(Name = "ContainedPath")] 
    private readonly Path m_path;

    /// <inheritdoc cref="Path.Name"/>
    public string? Name => m_path.Name;

    /// <inheritdoc cref="Path.StartingRegion"/>
    public RegionID StartingRegion => m_path.StartingRegion;

    /// <inheritdoc cref="Path.EndingRegion"/>
    public RegionID EndingRegion => m_path.EndingRegion;

    /// <inheritdoc cref="Path.ReqItem"/>
    public Path.RequiredItem ReqItem => m_path.ReqItem;

    /// <inheritdoc cref="Path.ReqCount"/>
    public uint ReqCount => m_path.ReqCount;

    /// <inheritdoc cref="Path.AlternateItem"/>
    public Path.RequiredItem AlternateItem => m_path.AlternateItem;

    /// <inheritdoc cref="Path.IsNull"/>
    public bool IsNull => m_path.IsNull;

    /// <inheritdoc cref="Path.ToString"/>
    public override string ToString() => m_path.ToString();

    /// <summary>
    /// Converts this to a mutable path, allowing modifications.
    /// Note that this creates a copy, and is not the same as modifying the original path.
    /// </summary>
    /// <returns></returns>
    public Path MakeMutable() => m_path;

    /// <summary>
    /// Implicitly constructs a ReadOnlyPath from a mutable path
    /// </summary>
    public static implicit operator ReadOnlyPath(Path source) => new(source);
}

/// <summary>
/// A Path with an ID associated with it
/// </summary>
[DataContract]
public struct KeyedPath : INullable
{
    /// <summary>
    /// Create a new null KeyedPath
    /// </summary>
    public KeyedPath()
    {
        ID = new();
        Path = new();
    }

    /// <summary>
    /// Create a new KeyedPath with the given path and ID
    /// </summary>
    public KeyedPath(PathID id, ReadOnlyPath path)
    {
        ID = id;
        Path = path;
    }

    /// <summary>
    /// Unique ID of the Path
    /// </summary>
    [DataMember] public PathID ID { get; init; }

    /// <summary>
    /// True if null (contains no path)
    /// </summary>
    public bool IsNull => ID.IsNull;

    /// <summary>
    /// The path object with the given ID
    /// </summary>
    [DataMember] public ReadOnlyPath Path { get; init; }

    // Below are helper for accessing data in the path

    /// <inheritdoc cref="Path.Name"/>
    public string? Name => Path.Name;

    /// <inheritdoc cref="Path.StartingRegion"/>
    public RegionID StartingRegion => Path.StartingRegion;

    /// <inheritdoc cref="Path.EndingRegion"/>
    public RegionID EndingRegion => Path.EndingRegion;

    /// <inheritdoc cref="Path.ReqItem"/>
    public Path.RequiredItem ReqItem => Path.ReqItem;

    /// <inheritdoc cref="Path.ReqCount"/>
    public uint ReqCount => Path.ReqCount;

    /// <inheritdoc cref="Path.AlternateItem"/>
    public Path.RequiredItem AlternateItem => Path.AlternateItem;
}