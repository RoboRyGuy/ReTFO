using System;
using System.Collections;
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Helper which is implicitly constructed from both ints and lists of ints
/// Intended to reduce the number of overloads needed when declaring Location constructors
/// </summary>
public struct RegionList : IList<int>, IList, IReadOnlyList<int>
{
    public RegionList(List<int> value) { this.value = value; }
    private List<int> value;
    public static implicit operator RegionList(int r) => new RegionList(new List<int>(1) { r });
    public static implicit operator RegionList(List<int> rs) => new RegionList(rs);
    public static implicit operator List<int>(RegionList rs) => rs.value;

    // Implementing IList<int>, IList, and IReadOnlyList<int> through `value`
    public int Count => ((ICollection<int>)value).Count;
    public bool IsReadOnly => ((ICollection<int>)value).IsReadOnly;
    public bool IsFixedSize => ((IList)value).IsFixedSize;
    public bool IsSynchronized => ((ICollection)value).IsSynchronized;
    public object SyncRoot => ((ICollection)value).SyncRoot;
    object? IList.this[int index] { get => ((IList)value)[index]; set => ((IList)this.value)[index] = value; }
    public int this[int index] { get => ((IList<int>)value)[index]; set => ((IList<int>)this.value)[index] = value; }
    public int IndexOf(int item) => ((IList<int>)value).IndexOf(item);
    public void Insert(int index, int item) => ((IList<int>)value).Insert(index, item);
    public void RemoveAt(int index) => ((IList<int>)value).RemoveAt(index);
    public void Add(int item) => ((ICollection<int>)value).Add(item);
    public void Clear() => ((ICollection<int>)value).Clear();
    public bool Contains(int item) => ((ICollection<int>)value).Contains(item);
    public void CopyTo(int[] array, int arrayIndex) => ((ICollection<int>)value).CopyTo(array, arrayIndex);
    public bool Remove(int item) => ((ICollection<int>)value).Remove(item);
    public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)value).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)value).GetEnumerator();
    public int Add(object? value) => ((IList)this.value).Add(value);
    public bool Contains(object? value) => ((IList)this.value).Contains(value);
    public int IndexOf(object? value) => ((IList)this.value).IndexOf(value);
    public void Insert(int index, object? value) => ((IList)this.value).Insert(index, value);
    public void Remove(object? value) => ((IList)this.value).Remove(value);
    public void CopyTo(Array array, int index) => ((ICollection)value).CopyTo(array, index);
}
