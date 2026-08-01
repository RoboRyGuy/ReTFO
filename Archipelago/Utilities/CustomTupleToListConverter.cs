using Clonesoft.Json;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// A custom converter which converts from a tuple of elements to a list of elements
/// </summary>
public class CustomTupleToListConverter<T> : JsonConverter
{
    public override bool CanRead => false;
    public override bool CanWrite => true;

    public override bool CanConvert(Type objectType)
    {
        return typeof(ITuple).IsAssignableFrom(objectType);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is not ITuple tuple)
            throw new ArgumentException($"Expected value to be a type of tuple, instead got {value?.GetType().FullName ?? "null"}");

        List<T> list = new List<T>(tuple.Length);
        for (int i = 0; i < tuple.Length; i++)
            list.Add((T)tuple[i]!);

        serializer.Serialize(writer, list);
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException("This converter cannot read.");
    }
}
