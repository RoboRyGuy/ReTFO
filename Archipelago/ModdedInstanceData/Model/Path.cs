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
public readonly struct Path : INullable
{
    /// <summary>
    /// Constructs a default (null) path
    /// </summary>
    public Path() { }

    /// <summary>
    /// Copy constructor
    /// </summary>
    public Path(Path source)
    {
        Name = source.Name;
        StartingRegion = source.StartingRegion;
        EndingRegion = source.EndingRegion;
        ReqItem = source.ReqItem;
        ReqCount = source.ReqCount;
        AlternateItem = source.AlternateItem;
    }

    /// <summary>
    /// Simple requires need to traverse a path
    /// </summary>
    [DataContract]
    public readonly struct RequiredItem : INullable
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
            /// A path req that cannot be met
            /// </summary>
            Blocked,

            /// <summary>
            /// Requires a certain number of a specific item, specified exactly by ID
            /// </summary>
            Item,

            /// <summary>
            /// Requires a certain number of a specific item, specified exactly by ID.
            /// Passing through the path consumes the specified item, preventing its reuse.
            /// </summary>
            ItemConsumed,

            /// <summary>
            /// Requires a certain number of items which are all children of one shared category ID.
            /// This includes items in the provided category.
            /// </summary>
            Category,
        }

        /// <summary>
        /// Constructs a path requirements struct using the given target and target type
        /// </summary>
        public RequiredItem(eType type, ItemID target)
        {
            Type = type;
            Target = target;
        }

        /// <summary>
        /// The type of requirement this represents
        /// </summary>
        [DataMember(Name = "type")]
        public eType Type { get; private init; } = eType.None;

        /// <summary>
        /// The tag utilized to identify the target
        /// </summary>
        [DataMember(Name = "target")]
        public ItemID Target { get; private init; } = new();

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
    [DataMember(Name = "name")]
    public string? Name { get; init; } = null;

    /// <summary>
    /// Region this path starts in
    /// </summary>
    [DataMember(Name = "starting_region")]
    public RegionID StartingRegion { get; init; } = new();

    /// <summary>
    /// Region this path ends in
    /// </summary>
    [DataMember(Name = "ending_region")]
    public RegionID EndingRegion { get; init; } = new();

    /// <summary>
    /// Requirements for accessing this path
    /// </summary>
    [DataMember(Name = "req_item")]
    public RequiredItem ReqItem { get; init; } = new(RequiredItem.eType.None, new ItemID());

    /// <summary>
    /// How many ReqItems are needed to traverse this path.
    /// </summary>
    [DataMember(Name = "req_count")]
    public uint ReqCount { get; init; } = 0;

    /// <summary>
    /// Alternate item required to traverse this path
    /// <list type="=bullet">
    ///  <item>If ReqItem.Isnull, then this is ignored (by design)</item>
    ///  <item>The alternate item is assumed to only require one count to traverse the path; it is assumed to be a uniuqe item</item>
    ///  <item>This is intended for situations such as door unlock events (since all zone doors can be force unlocked via an event)</item>
    /// </list>
    /// </summary>
    [DataMember(Name = "alt_item")]
    public RequiredItem AlternateItem { get; init; } = new(RequiredItem.eType.Blocked, new ItemID());

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
public struct PathID : ITagID, IEquatable<PathID>, IComparable<PathID>
{
    [DataMember(Name = "id")]
    public uint ID { get; init; }

    public bool IsNull => ID == 0;
    public int AsIndex { get => checked((int)ID - 1); init => ID = unchecked((uint)value + 1u); }
    public bool Equals(PathID other) => ID == other.ID;
    public int CompareTo(PathID other) => ID.CompareTo(other.ID);
    public override string ToString() => $"PathID {ID}";
}
