using System;
using System.Collections.Generic;

namespace ReTFO.Archipelago.Utilities;

// Extra linq-style utilies
public static class LinqUtils
{
    /// <summary>
    /// Same as the Take Linq method, but if the desired count exceeds the number of enumerable items,
    ///  instead loops the enumeration to continue sampling indefinitely.
    /// </summary>
    /// <typeparam name="T">Type of the enumerable</typeparam>
    /// <param name="source">Source enumerable to pull elements from</param>
    /// <param name="count">How many elements to pull</param>
    /// <returns>The looped enumeration with the desired element count</returns>
    /// <exception cref="ArgumentException">If count is less than 0</exception>
    public static IEnumerable<T> TakeLooped<T>(this IEnumerable<T> source, int count)
    {
        if (count < 0)
            throw new ArgumentException("Count cannot be negative");
        else if (count == 0)
            yield break;

        // Either reuse the existng list or create a new one
        IReadOnlyList<T>? cache = source as IReadOnlyList<T>;
        if (cache == null)
        {
            List<T> mutableCache = new();
            foreach (T item in source)
            {
                mutableCache.Add(item);
                yield return item;
                if (--count == 0) break;
            }
            cache = mutableCache;
        }

        while (count-- > 0)
        {
            foreach (var item in cache)
                yield return item;
        }
    }
}
