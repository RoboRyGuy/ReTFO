using Clonesoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// Custom formatting for lists to compress them.
/// Works on all serializable types, serialized instances will be in compressed format.
/// </summary>
public sealed class ListConverter<T> : JsonConverter
{
    public override bool CanRead => false;
    public override bool CanWrite => true;

    public ListConverter(int maxPerLine = 20)
    {
        MaxPerLine = maxPerLine;
    }

    /// <summary>
    /// Lightweight class used for formatting
    /// </summary>
    private class ThisWriter : TextWriter
    {
        public ThisWriter(JsonWriter source) => writer = source;
        JsonWriter writer;
        public override Encoding Encoding => Encoding.Default;
        public override void Write(char value) => writer.WriteRaw(new string(value, 1));
        public override void Write(string? value) => writer.WriteRaw(value);
    }

    public int MaxPerLine { get; set; }

    public override bool CanConvert(Type objectType)
    {
        return typeof(IEnumerable<T>).IsAssignableFrom(objectType);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is not IEnumerable<T> list) 
            throw new ArgumentException($"Expected value to be of type {typeof(List<T>).FullName}.", nameof(value));

        if (writer.Formatting != Formatting.Indented)
            throw new NotSupportedException("ListConverter was either used on a nested conerted list or during an unformatted serialization. Neither are supported!");

        var subLists = list.Chunk(MaxPerLine).ToList();

        ThisWriter customWriter = new(writer);
        if (subLists.Count == 0)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }
        else if (subLists.Count == 1)
        {
            writer.WriteStartArray();
            writer.WriteRaw(" ");
            writer.Formatting = Formatting.None;
            serializer.Serialize(customWriter, subLists.First().First());
            foreach (var item in subLists.First().Skip(1))
            {
                writer.WriteRaw(", ");
                serializer.Serialize(customWriter, item);
            }
            writer.WriteEndArray();
            writer.Formatting = Formatting.Indented;
        }
        else
        {
            writer.WriteStartArray();
            foreach (var set in subLists)
            {
                serializer.Serialize(writer, set.First());
                writer.Formatting = Formatting.None;
                foreach (var item in set.Skip(1))
                {
                    writer.WriteRaw(", ");
                    serializer.Serialize(customWriter, item);
                }
                writer.Formatting = Formatting.Indented;
            }
            writer.WriteEndArray();
        }
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException("This converter cannot read.");
    }
}
