using System;
using System.Collections.Generic;
using System.Linq;
using GameData;

namespace ReTFO.Archipelago;

// Simple extension class for Il2Cpp lists which gives makes them enumerable
public static class Il2CppListIter
{
    // Convert an Il2CppList into an enumerable
    public static IEnumerable<T> Iter<T>(this Il2CppSystem.Collections.Generic.List<T>? source)
    {
        if (source != null) foreach (T element in source) yield return element;
    }

    // Various other methods, for simplicity's sake

    public static bool Any<T>(this Il2CppSystem.Collections.Generic.List<T> source, Func<T, bool> predicate)
    {
        return source.Iter().Any(predicate);
    }

    public static T First<T>(this Il2CppSystem.Collections.Generic.List<T> source)
    {
        return source.Iter().First();
    }

    public static T First<T>(this Il2CppSystem.Collections.Generic.List<T> source, Func<T, bool> predicate)
    {
        return source.Iter().First(predicate);
    }

    public static T? FirstOrDefault<T>(this Il2CppSystem.Collections.Generic.List<T> source, Func<T, bool> predicate)
    {
        return source.Iter().FirstOrDefault(predicate);
    }

    public static T? FirstOrDefault<T>(this Il2CppSystem.Collections.Generic.List<T> source)
    {
        return source.Iter().FirstOrDefault();
    }

    public static IEnumerable<U> Select<T, U>(this Il2CppSystem.Collections.Generic.List<T> source, Func<T, U> func)
    {
        return source.Iter().Select(func);
    }

    public static IEnumerable<U> SelectMany<T, U>(this Il2CppSystem.Collections.Generic.List<T> source, Func<T, IEnumerable<U>> func)
    {
        return source.Iter().SelectMany(func);
    }

    public static IEnumerable<T> Skip<T>(this Il2CppSystem.Collections.Generic.List<T> source, int count)
    {
        return source.Iter().Skip(count);
    }

    public static T[] ToArray<T>(this Il2CppSystem.Collections.Generic.List<T> source)
    {
        return source.Iter().ToArray();
    }

    public static List<T> ToList<T>(this Il2CppSystem.Collections.Generic.List<T> source)
    {
        return source.Iter().ToList();
    }

    public static IEnumerable<T> Where<T>(this Il2CppSystem.Collections.Generic.List<T> source, Func<T, bool> predicate)
    {
        return source.Iter().Where(predicate);
    }

    // Custom extension.
    // Split a list into sublists using a predicate to determine which value is used to split the list.
    // Values matching the predicate are not returned in the result.
    public static IEnumerable<IEnumerable<T>> Split<T>(this Il2CppSystem.Collections.Generic.List<T> source, Func<T, bool> predicate)
    {
        if (source == null) yield break;

        var iter = source.GetEnumerator();
        IEnumerable<T> Split_Helper()
        {
            if (predicate(iter.Current)) yield break;
            yield return iter.current;

            while (iter.MoveNext())
            {
                if (predicate(iter.Current)) yield break;
                else yield return iter.Current;
            }
        }
        
        while (iter.MoveNext())
            yield return Split_Helper();
    }

    // Helper specifically for handling event breaks in WardenObjectiveEventData lists
    public static List<List<WardenObjectiveEventData>> EventSplit(this Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> source)
    {
        return source.Split(e => (e?.Type ?? eWardenObjectiveEventType.EventBreak) == eWardenObjectiveEventType.EventBreak)
            .Select(es => es.ToList()).ToList();
    }

}
