using ReTFO.Archipelago.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Type of requirements possibly needed
    /// </summary>
    public enum eType
    {
        /// <summary>
        /// No required item; not assigned. Default value
        /// </summary>
        None,

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
    [DataContract]
    public readonly struct PathReq : INullable
    {
        /// <summary>
        /// Constructs a path requirements struct using the given target
        /// </summary>
        public PathReq(eType type, ItemID target, uint count)
        {
            if (type == eType.MultiReq)
                throw new InvalidOperationException($"Cannot assign a target of type {nameof(eType.MultiReq)} to a PathReq!");
            else if (type == eType.None)
                throw new InvalidOperationException($"A target of type {nameof(eType.None)} is pointless for a PathReq!");

            Type = type;
            Target = target;
            Count = count;
        }

        /// <summary>
        /// The type of requirement this represents
        /// </summary>
        [DataMember(Name = "type")]
        public readonly eType Type = eType.None;

        /// <summary>
        /// The tag utilized to identify the target
        /// </summary>
        [DataMember(Name = "target")]
        public readonly ItemID Target = new();

        /// <summary>
        /// The number of target(s) that must be acquired
        /// </summary>
        [DataMember(Name = "count")]
        public readonly uint Count = 1u;

        public bool IsNull => Type == eType.None;
    }

    /// <summary>
    /// A variation of PathReq which acts as a union of PathReq and PathReq[].
    /// Unfortuantely, implementing an actual union is too much of a pain, so this is the best we get.
    /// </summary>
    public readonly struct MultiPathReq : IEnumerable<PathReq>
    {
        /// <summary>
        /// Creates a path with no requirements
        /// </summary>
        public MultiPathReq()
        {
            Type = eType.None;
            m_target = new();
            m_count = 0u;
        }

        /// <summary>
        /// Creates a path with a single requirement
        /// </summary>
        public MultiPathReq(eType type, ItemID target, uint count)
        {
            if (type == eType.MultiReq)
                throw new InvalidOperationException($"Cannot manually assign a target of type {nameof(eType.MultiReq)} to a MultiPathReq!");

            Type = type;
            m_target = target;
            m_count = count;
        }

        public MultiPathReq(PathReq req)
        {
            Type = req.Type;
            m_target = req.Target;
            m_count = req.Count;
        }

        /// <summary>
        /// Creates a path with a series of requirements
        /// </summary>
        public MultiPathReq(params PathReq[] reqs)
        {
            if (reqs.Length == 0)
            {
                Type = eType.None;
                m_target = new();
                m_count = 0u;
            }
            else if (reqs.Length == 1)
            {
                Type = reqs[0].Type;
                m_target = reqs[0].Target;
                m_count = reqs[0].Count;
            }
            else
            {
                Type = eType.MultiReq;
                m_reqs = reqs;
            }
        }

        public readonly eType Type;

        private readonly ItemID m_target;

        private readonly uint m_count;

        private readonly PathReq[] m_reqs = null!;

        /// <summary>
        /// True if the path has no requirements
        /// </summary>
        public bool IsNone => Type == eType.None;

        /// <summary>
        /// Gets all the individual reqs for this req as an array
        /// </summary>
        public PathReq[] Reqs
            => Type switch
            {
                eType.None => [],
                eType.MultiReq => m_reqs,
                _ => [new PathReq(Type, m_target, m_count)]
            };

        private class Enumerator : IEnumerator<PathReq>
        {
            public Enumerator(PathReq[] s) => source = s;
            PathReq[] source;
            int position = -1;

            public PathReq Current => source[position];
            object IEnumerator.Current => Current;
            public bool MoveNext() => ++position < source.Length;
            public void Reset() => position = -1;
            public void Dispose() { }
        }

        public IEnumerator<PathReq> GetEnumerator() => new Enumerator(Reqs);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Creates a new copy of this path req with the inputted requirement added to it.
        /// If the requirement's target is in the list of already-required targets:
        /// - If the type does not match, raises an error.
        /// - If the type matches, increases the existing req's count by newReq's count
        /// Otherwise, it is appended to the list of targets.
        /// </summary>
        public MultiPathReq WithAdded(PathReq newReq)
        {
            if (Type == eType.None)
            {
                return new MultiPathReq(newReq.Type, newReq.Target, newReq.Count);
            }
            else if (Type != eType.MultiReq)
            {
                if (m_target.Equals(newReq.Target))
                {
                    if (Type != newReq.Type)
                        throw new ArgumentException($"Cannot add newReq to existing MultiPathReq; targets match, but target types don't match!");
                    return new MultiPathReq(Type, m_target, m_count + newReq.Count);
                }
                else
                {
                    return new MultiPathReq(
                        new PathReq(Type, m_target, m_count),
                        newReq
                    );
                }
            }
            else
            {
                int index;
                for (index = 0; index < m_reqs.Length; index++)
                    if (m_reqs[index].Target.Equals(newReq.Target)) break;

                if (index < m_reqs.Length)
                {
                    if (m_reqs[index].Type != newReq.Type)
                        throw new ArgumentException($"Cannot add newReq to existing MultiPathReq; targets match, but target types don't match!");
                    PathReq[] arr = new PathReq[m_reqs.Length];
                    m_reqs.CopyTo(arr);
                    arr[index] = new(newReq.Type, newReq.Target, m_reqs[index].Count + newReq.Count);
                    return new MultiPathReq(arr);
                }
                else
                {
                    PathReq[] arr = new PathReq[m_reqs.Length + 1];
                    m_reqs.CopyTo(arr);
                    m_reqs[index] = newReq;
                    return new MultiPathReq(arr);
                }
            }
        }

        /// <summary>
        /// Creates a new copy of this path req with the inputted requirements added to it.
        /// For each requirement, if the requirement's target is in the list of already-required targets:
        /// - If the type does not match, raises an error.
        /// - If the type matches, increases the existing req's count by newReq's count
        /// Otherwise, it is appended to the list of targets.
        /// </summary>
        public MultiPathReq WithAdded(params PathReq[] newReqs)
        {
            // See if there are simpler solutions
            if (newReqs.Length == 0)
                return this;
            else if (newReqs.Length == 1)
                return WithAdded(newReqs[0]);

            // Ensure that none of the new reqs share the same target
            for (int i = 1; i < newReqs.Length; i++)
            {
                for (int j = 0; j < i; j++)
                    if (newReqs[i].Target.Equals(newReqs[j].Target))
                        throw new ArgumentException("Cannot add multiple reqs which share a target at once to a MultiPathReq!");
            }

            if (Type == eType.None)
            {
                return new MultiPathReq(newReqs);
            }
            else if (Type != eType.MultiReq)
            {
                return new MultiPathReq(newReqs).WithAdded(new PathReq(Type, m_target, m_count));
            }
            else
            {
                int[] indicies = new int[newReqs.Length];
                int newLength = Reqs.Length;
                for (int i = 0; i < newReqs.Length; i++)
                {
                    for (indicies[i] = 0; indicies[i] < Reqs.Length; indicies[i]++)
                        if (newReqs[i].Target.Equals(Reqs[indicies[i]].Target)) break;
                    if (indicies[i] == Reqs.Length) newLength++;
                }

                PathReq[] arr = new PathReq[newLength];
                newLength = Reqs.Length;
                Reqs.CopyTo(arr);
                for (int i = 0; i < newReqs.Length; i++)
                {
                    if (indicies[i] == Reqs.Length)
                        arr[newLength++] = newReqs[i];
                    else if (newReqs[i].Type != Reqs[indicies[i]].Type)
                        throw new ArgumentException($"Cannot add newReq to existing MultiPathReq; targets match, but target types don't match!");
                    else
                        arr[indicies[i]] = new(newReqs[i].Type, newReqs[i].Target, Reqs[indicies[i]].Count + newReqs[i].Count);
                }

                return new MultiPathReq(arr);
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
    public MultiPathReq Reqs { get; init; } = new();

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
