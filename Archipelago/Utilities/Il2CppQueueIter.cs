using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.Utilities;

public static class Il2CppQueueIter
{
    /// <summary>
    /// Get an enumerator for an Il2CppSystem Queue
    /// </summary>
    public static IEnumerable<T> Iter<T>(this Il2CppSystem.Collections.Generic.Queue<T> queue)
        => Enumerable.Range(queue._head, queue._size).Select(i => queue._array[i]);

    /// <summary>
    /// Get an item from the end of a queue
    /// </summary>
    /// <param name="indexFromEnd">0-indexed item, starting from the end of the queue</param>
    public static T FromEnd<T>(this Il2CppSystem.Collections.Generic.Queue<T> queue, int indexFromEnd = 0)
        => queue._array[queue._head + queue._size - 1 - indexFromEnd];

}
