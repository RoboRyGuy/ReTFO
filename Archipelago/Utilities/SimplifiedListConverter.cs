using Clonesoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// Custom formatting for lists to compress them.
/// Works using the provided type's ToString method, so use with care.
/// Will align entries if the list takes more than one line
/// </summary>
public sealed class SimplifiedListConverter<T> : JsonConverter
{
    public override bool CanRead => false;
    public override bool CanWrite => true;

    public SimplifiedListConverter(int maxPerLine = 20)
    {
        MaxPerLine = maxPerLine;
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

        var subLists = list.Chunk(MaxPerLine).ToList();

        if (subLists.Count == 0)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }
        else if (subLists.Count == 1)
        {
            writer.WriteStartArray();
            var oldFormat = writer.Formatting;
            writer.Formatting = Formatting.None;
            if (typeof(T) == typeof(string))
                writer.WriteRaw($" {string.Join(", ", subLists.First().Select(i => $"\"{i}\""))} ");
            else
                writer.WriteRaw($" {string.Join(", ", subLists.First().Select(i => i?.ToString()))} ");

            writer.WriteEndArray();
            writer.Formatting = oldFormat;
        }
        else
        {
            List<List<string>> strings;
            if (typeof(T) == typeof(string))
                strings = subLists.Select(set => set.Select(s => $"\"{s}\"").ToList()).ToList();
            else
                strings = subLists.Select(set => set.Select(s => s?.ToString() ?? "null").ToList()).ToList();
            int max = strings.Max(set => set.Max(s => s.Length));

            writer.WriteStartArray();
            foreach (var set in strings)
                writer.WriteRawValue(string.Join(", ", set.Select(s => s.PadLeft(max))));
            writer.WriteEndArray();
        }

    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException("This converter cannot read.");
    }
}
