using ReTFO.Archipelago.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
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
        Reqs = source.Reqs;
    }

    /// <summary>
    /// Simple requires need to traverse a path
    /// </summary>
    [StructLayout(LayoutKind.Explicit), DataContract]
    public readonly struct PathReq : INullable
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
            /// Requires a specific type of item. During the server-side generation, the requirements for this
            /// path type are increased by the sum of all previously-encountered RequiredItems with the same target.
            /// </summary>
            ItemGrowing,

            /// <summary>
            /// Requires a certain number of items which are all children of one shared category ID.
            /// This includes items in the provided category.
            /// </summary>
            Category,

            /// <summary>
            /// Requires a specific category of item. During the server-side generation, the requirements for this
            /// path type are increased by the sum of all previously-encountered growing RequiredItems with the same target.
            /// </summary>
            CategoryGrowing,

            /// <summary>
            /// Used by <see cref="MultiPathReq"/> to indicate that it contains an array of values.
            /// </summary>
            MultiReq,
        }

        /// <summary>
        /// Constructs a path requirements struct using the given target
        /// </summary>
        public PathReq(eType type, ItemID target, uint count)
        {
            if (type == PathReq.eType.MultiReq)
                throw new InvalidOperationException($"Cannot assign a target of type {nameof(PathReq.eType.MultiReq)} to a PathReq!");

            Type = type;
            Target = target;
            Count = count;
        }

        /// <summary>
        /// The type of requirement this represents
        /// </summary>
        [FieldOffset(0), DataMember(Name = "type")]
        public readonly eType Type = eType.None;

        /// <summary>
        /// The tag utilized to identify the target
        /// </summary>
        [FieldOffset(sizeof(eType)), DataMember(Name = "target")]
        public readonly ItemID Target = new();

        /// <summary>
        /// The number of target(s) that must be acquired
        /// </summary>
        [FieldOffset(sizeof(eType) + sizeof(uint)), DataMember(Name = "count")]
        public readonly uint Count = 1u;

        public bool IsNull => Type == eType.None;
    }

    /// <summary>
    /// A variation of PathReq which acts as a union of PathReq and PathReq[]
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct MultiPathReq : IEnumerable<PathReq>
    {
        public MultiPathReq(PathReq.eType type, ItemID target, uint count)
        {
            if (type == PathReq.eType.MultiReq)
                throw new InvalidOperationException($"Cannot manually assign a target of type {nameof(PathReq.eType.MultiReq)} to a MultiPathReq!");

            Type = type;
            Target = target;
            Count = count;
        }

        public MultiPathReq(PathReq[] reqs)
        {
            Type = PathReq.eType.MultiReq;
            Reqs = reqs;
        }

        [FieldOffset(0)]
        private readonly PathReq.eType Type;
        
        [FieldOffset(sizeof(PathReq.eType))]
        private readonly ItemID Target;

        [FieldOffset(sizeof(PathReq.eType) + sizeof(uint))]
        private readonly uint Count;

        [FieldOffset(sizeof(PathReq.eType))]
        private readonly PathReq[] Reqs = null!;

        private class Enumerator : IEnumerator<PathReq>
        {
            public Enumerator(PathReq[] s) => source = s;
            PathReq[] source;
            int position = -1;

            public PathReq Current => source[position];
            object IEnumerator.Current => Current;
            public bool MoveNext() => position++ < source.Length;
            public void Reset() => position = -1;
            public void Dispose() { }
        }

        public IEnumerator<PathReq> GetEnumerator()
            => new Enumerator(Type == PathReq.eType.MultiReq ? Reqs : [new PathReq(Type, Target, Count)]);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Creates a new copy of this path req with the inputted requirement added to it.
        /// If the requirement's target is in the list of already-required targets:
        ///  - If the type does not match, raises an error.
        ///  - If the type matches, increases the existing req's count by newReq's count
        /// Otherwise, it is appended to the list of targets.
        /// </summary>
        public MultiPathReq WithAdded(PathReq newReq)
        {
            if (Type != PathReq.eType.MultiReq)
            {
                if (Target.Equals(newReq.Target))
                {
                    if (Type != newReq.Type)
                        throw new InvalidOperationException($"Cannot add newReq to existing MultiPathReq; targets match, but target types don't match!");
                    return new MultiPathReq(Type, Target, Count + newReq.Count);
                }
                else
                {
                    return new MultiPathReq(new PathReq[]
                    {
                        new PathReq(Type, Target, Count),
                        newReq
                    });
                }
            }
            else
            {
                int existingIndex;
                for (existingIndex = 0; existingIndex < Reqs.Length; existingIndex++)
                    if (Reqs[existingIndex].Target.Equals(newReq.Target)) break;

                if (existingIndex < Reqs.Length)
                {
                    if (Reqs[existingIndex].Type != newReq.Type)
                        throw new InvalidOperationException($"Cannot add newReq to existing MultiPathReq; targets match, but target types don't match!");
                    PathReq[] arr = new PathReq[Reqs.Length];
                    Reqs.CopyTo(arr, 0);
                    arr[existingIndex] = new(newReq.Type, newReq.Target, Reqs[existingIndex].Count + newReq.Count);
                    return new MultiPathReq(arr);
                }
                else
                {
                    PathReq[] arr = new PathReq[Reqs.Length + 1];
                    Reqs.CopyTo(arr, 0);
                    Reqs[existingIndex] = newReq;
                    return new MultiPathReq(arr);
                }
            }
        }
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
    /// Requirements for accessing this path. These are AND-ed together
    /// </summary>
    [DataMember(Name = "reqs")]
    public MultiPathReq Reqs { get; init; } = new(PathReq.eType.None, new ItemID(), 1u);

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
