using System;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// An interface indicating an item contains a key value, and that
///  the indicated key value can be used to search and sort the item
/// </summary>
public interface IKeyed<T> 
    where T : IComparable<T>, IEquatable<T>
{
    T Key { get; }
}
