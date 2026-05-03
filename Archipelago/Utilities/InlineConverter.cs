using Clonesoft.Json;
using Clonesoft.Json.Serialization;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using System;
using System.Linq;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// Forces a class to inline properties of a given type
/// </summary>
public sealed class InlineConverter : JsonConverter
{
    public override bool CanRead => false;
    public override bool CanWrite => true;

    public InlineConverter(Type[] targetTypes, Type[] inlinedTypes)
    {
        TargetTypes = targetTypes;
        InlinedTypes = inlinedTypes;
    }

    public Type[] TargetTypes { get; set; }
    public Type[] InlinedTypes { get; set; }

    public override bool CanConvert(Type objectType)
    {
        return TargetTypes.Any(t => t.IsAssignableFrom(objectType)) 
            || InlinedTypes.Any(t => t.IsAssignableFrom(objectType));
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        bool isInlining = writer.WriteState == WriteState.Object;
        if (value == null)
        {
            if (!isInlining)
                writer.WriteNull();
            return;
        }

        bool isContainer = TargetTypes.Any(t => t.IsAssignableFrom(value.GetType()));
        var contract = (JsonObjectContract)serializer.ContractResolver.ResolveContract(value.GetType());

        if (!isInlining) writer.WriteStartObject();
        try
        {
            foreach (var property in contract.Properties)
            {
                if (property.Ignored || !property.Readable) continue;
                bool shouldInline = isContainer && InlinedTypes.Any(t => t.IsAssignableFrom(property.PropertyType));
                if (!shouldInline) writer.WritePropertyName(property.PropertyName!);
                serializer.Serialize(writer, property.ValueProvider!.GetValue(value));
            }
        }
        finally
        {
            if (!isInlining) writer.WriteEndObject();
        }
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotSupportedException("This converter cannot read.");
    }
}