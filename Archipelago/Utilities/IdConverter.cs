using Clonesoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ReTFO.Archipelago.ModdedInstanceData.Model;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// Ensures IDs are serialized by value, instead of as an object with a single "m_value" property.
/// Also handles lists and list-likes of IDs ahead of time so the IntListConverter can compress them
/// </summary>
public sealed class IdConverter : JsonConverter
{
    public override bool CanRead => false;
    public override bool CanWrite => true;

    public override bool CanConvert(Type objectType)
    {
        if (objectType.IsAssignableTo(typeof(ITagID)))
            return true;

        foreach (var i in objectType.GetInterfaces())
        {
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                Type itemType = i.GetGenericArguments()[0];
                if (itemType.IsAssignableTo(typeof(ITagID)))
                    return true;
            }
        }

        return false;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is ITagID id)
        {
            serializer.Serialize(writer, id.ID);
            return;
        }

        if (value is IEnumerable enumerable)
        {
            var list = enumerable.Cast<ITagID>().Select(i => i.ID).ToList();
            serializer.Serialize(writer, list);
        }

        else throw new JsonException($"{nameof(IdConverter)} cannot convert type {value?.GetType().FullName}");
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException("This converter cannot read.");
    }
}
