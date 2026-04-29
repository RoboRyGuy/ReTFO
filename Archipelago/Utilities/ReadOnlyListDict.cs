using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// Wraps a readonly list to act as a readonly dictionary
/// </summary>
public class ReadOnlyListDict<I, T> : IReadOnlyDictionary<I, T>
    where I : IIndex, new()
{
    private readonly IReadOnlyList<T> m_list;
    public ReadOnlyListDict(IReadOnlyList<T> source) => m_list = source;

    public T this[I key] => m_list[key.AsIndex];
    public IEnumerable<I> Keys => Enumerable.Range(0, m_list.Count).Select(i => new I() { AsIndex = i });
    public IEnumerable<T> Values => m_list;
    public int Count => m_list.Count;
    public bool IsReadOnly => true;
    public bool ContainsKey(I key) => key.AsIndex >=0 && key.AsIndex < m_list.Count;
    public IEnumerator<KeyValuePair<I, T>> GetEnumerator() => m_list.Select((v, i) =>  new KeyValuePair<I, T>(new I() { AsIndex = i }, v)).GetEnumerator();
    public bool TryGetValue(I key, [MaybeNullWhen(false)] out T value)
    {
        if (!ContainsKey(key))
        {
            value = default;
            return false;
        }
        else
        {
            value = m_list[key.AsIndex];
            return true;
        }
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
