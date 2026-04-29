using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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
public struct Path : INullable
{
    /// <summary>
    /// Constructs a default (null) path
    /// </summary>
    public Path() { }

    /// <summary>
    /// Simple requires need to traverse a path
    /// </summary>
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
        public eType Type { get; init; } = eType.None;

        /// <summary>
        /// The tag utilized to identify the target
        /// </summary>
        public RandomizationTag Target { get; init; } = new();

        public bool IsNull => Type == eType.None;

    }

    /// <summary>
    /// Comparer for two Paths using their starting region as the key
    /// </summary>
    public class ByStartingRegionComparer : IComparer<Path>
    {
        public int Compare(Path x, Path y)
            => Comparer<int>.Default.Compare(x.StartingRegion.Value, y.StartingRegion.Value);
    }

    /// <summary>
    /// Comparer for two regions using their ending region as the key
    /// </summary>
    public class ByEndingRegionComparer : IComparer<Path>
    {
        public int Compare(Path x, Path y)
            => Comparer<int>.Default.Compare(x.EndingRegion.Value, y.EndingRegion.Value);
    }

    /// <summary>
    /// Optional name for this path
    /// </summary>
    public string? Name { get; set; } = null;

    /// <summary>
    /// Region this path starts in
    /// </summary>
    public RegionID StartingRegion { get; set; } = new();

    /// <summary>
    /// Region this path ends in
    /// </summary>
    public RegionID EndingRegion { get; set; } = new();

    /// <summary>
    /// Requirements for accessing this path
    /// </summary>
    public RequiredItem ReqItem { get; set; } = new();

    /// <summary>
    /// How many ReqItems are needed to traverse this path.
    /// </summary>
    public uint ReqCount { get; set; } = 0;

    /// <summary>
    /// Alternate item required to traverse this path
    /// <list type="=bullet">
    ///  <item>If ReqItem.Isnull, then this is ignored (by design)</item>
    ///  <item>The alternate item is assumed to only require one count to traverse the path; it is assumed to be a uniuqe item</item>
    ///  <item>This is intended for situations such as door unlock events (since all zone doors can be force unlocked via an event)</item>
    /// </list>
    /// </summary>
    public RequiredItem AlternateItem { get; set; } = new();

    /// <summary>
    /// If this path is null. Considered true if the starting and ending region are the same.
    /// </summary>
    public bool IsNull => StartingRegion.Value == EndingRegion.Value;

    /// <summary>
    /// Helper for visualizing in debugger
    /// </summary>
    public override string ToString() => $"{StartingRegion} => {EndingRegion}";
}

/// <summary>
/// Simple wrapper around a int to help identify it as a PathID, usable
///  for looking up a Path instance in GameData.
/// </summary>
public struct PathID : INullable, IIndex, IComparable<PathID>, IEquatable<PathID>
{
    public PathID () { }
    public readonly int Value = 0;

    public bool IsNull => Value == 0;
    public int AsIndex { get => Value; init => Value = value; }
    public int CompareTo(PathID other) => Value.CompareTo(other.Value);
    public bool Equals(PathID other) => Value.Equals(other.Value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PathID id && Equals(id);
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>
/// Wrapper around a path struct which makes it immutable.
/// This is often used as a return value where modifying it wouldn't make sense.
/// </summary>
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