
using Clonesoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.Utilities;

// Custom formatting for lists of ints to make them (more) inline
public sealed class IntListConverer : JsonConverter
{
    public override bool CanRead => false;
    public override bool CanWrite => true;

    public IntListConverer(int maxIntsPerLine = 20)
    {
        MaxIntsPerLine = maxIntsPerLine;
    }

    public int MaxIntsPerLine { get; set; }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(List<int>);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is not List<int> ints) throw new ArgumentException("Expected value to be of type List<int>.", nameof(value));
        if (ints.Count > MaxIntsPerLine)
        {
            writer.WriteStartArray();
            foreach (var subList in ints.Chunk(MaxIntsPerLine))
                writer.WriteRawValue(string.Join(", ", subList));
            writer.WriteEndArray();
        }
        else
            writer.WriteRawValue($"[ {string.Join(", ", ints)} ]");
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException("This converter cannot read.");
    }
}
