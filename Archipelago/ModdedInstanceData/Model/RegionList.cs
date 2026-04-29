using System;
using System.Collections;
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Helper which is implicitly constructed from both ints and lists of ints
/// Intended to reduce the number of overloads needed when declaring Location constructors
/// </summary>
public struct RegionList : IList<RegionID>, IList, IReadOnlyList<RegionID>
{
    public RegionList(RegionID[] value) { this.value = value; }
    private RegionID[] value;
    public static implicit operator RegionList(RegionID r) => new RegionList([ r ]);
    public static implicit operator RegionList(RegionID[] rs) => new RegionList(rs);
    public static implicit operator RegionList(List<RegionID> rs) => new RegionList(rs.ToArray());
    public static implicit operator RegionID[](RegionList rs) => rs.value;

    // Implementing IList<int>, IList, and IReadOnlyList<int> through `value`
    public int Count => ((ICollection<RegionID>)value).Count;
    public bool IsReadOnly => ((ICollection<RegionID>)value).IsReadOnly;
    public bool IsFixedSize => ((IList)value).IsFixedSize;
    public bool IsSynchronized => ((ICollection)value).IsSynchronized;
    public object SyncRoot => ((ICollection)value).SyncRoot;
    object? IList.this[int index] { get => ((IList)value)[index]; set => ((IList)this.value)[index] = value; }
    public RegionID this[int index] { get => ((IList<RegionID>)value)[index]; set => ((IList<RegionID>)this.value)[index] = value; }
    public int IndexOf(RegionID item) => ((IList<RegionID>)value).IndexOf(item);
    public void Insert(int index, RegionID item) => ((IList<RegionID>)value).Insert(index, item);
    public void RemoveAt(int index) => ((IList<RegionID>)value).RemoveAt(index);
    public void Add(RegionID item) => ((ICollection<RegionID>)value).Add(item);
    public void Clear() => ((ICollection<RegionID>)value).Clear();
    public bool Contains(RegionID item) => ((ICollection<RegionID>)value).Contains(item);
    public void CopyTo(RegionID[] array, int arrayIndex) => ((ICollection<RegionID>)value).CopyTo(array, arrayIndex);
    public bool Remove(RegionID item) => ((ICollection<RegionID>)value).Remove(item);
    public IEnumerator<RegionID> GetEnumerator() => ((IEnumerable<RegionID>)value).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)value).GetEnumerator();
    public int Add(object? value) => ((IList)this.value).Add(value);
    public bool Contains(object? value) => ((IList)this.value).Contains(value);
    public int IndexOf(object? value) => ((IList)this.value).IndexOf(value);
    public void Insert(int index, object? value) => ((IList)this.value).Insert(index, value);
    public void Remove(object? value) => ((IList)this.value).Remove(value);
    public void CopyTo(Array array, int index) => ((ICollection)value).CopyTo(array, index);
}
