using System;
using System.Collections.Generic;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// A type of event where all items are executed in order, with the next callback using the
/// result of the previous callback
/// </summary>
public class ChainedEvent<T> : List<Func<T, T>>
{
    /// <summary>
    /// Invokes this event
    /// </summary>
    /// <param name="input">The initial input</param>
    /// <returns>The transformed output</returns>
    public T Invoke(T input)
    {
        for (int i = 0; i < Count; i++)
            input = this[i].Invoke(input);
        return input;
    }
}
