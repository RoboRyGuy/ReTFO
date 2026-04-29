using Clonesoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.Utilities;

// Custom formatting for lists to make them (more) inline
public sealed class ListConverter<T> : JsonConverter
{
    public override bool CanRead => false;
    public override bool CanWrite => true;

    public ListConverter(int maxPerLine = 20)
    {
        MaxPerLine = maxPerLine;
    }

    public int MaxPerLine { get; set; }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(List<T>);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is not List<T> list) throw new ArgumentException($"Expected value to be of type {typeof(List<T>).FullName}.", nameof(value));
        if (list.Count > MaxPerLine)
        {
            writer.WriteStartArray();
            foreach (var subList in list.Chunk(MaxPerLine))
                writer.WriteRawValue(string.Join(", ", subList));
            writer.WriteEndArray();
        }
        else
            writer.WriteRawValue($"[ {string.Join(", ", list)} ]");
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException("This converter cannot read.");
    }
}
