using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.Utilities;

// Simple extension class for Il2Cpp lists which gives makes them enumerable
public static class Il2CppListIter
{
    // Convert an Il2CppList into an enumerable
    public static IEnumerable<T> Iter<T>(this Il2CppSystem.Collections.Generic.List<T>? source)
    {
        if (source != null) foreach (T element in source) yield return element;
    }

    // Various other methods, for simplicity's sake

    public static bool Any<T>(this Il2CppSystem.Collections.Generic.List<T>? source)
    {
        return source.Iter().Any();
    }

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

}
