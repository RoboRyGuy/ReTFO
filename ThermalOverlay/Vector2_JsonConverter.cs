using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace ReTFO.ThermalOverlay;

/// <summary>
/// JSON Converter for Vector3 objects
/// </summary>
public class Vector2_JsonConverter : JsonConverter<Vector2>
{
    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.x);
        writer.WriteNumber("y", value.y);
        writer.WriteEndObject();
    }

    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token");

        Vector2 output = new();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return output;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected PropertyName token");

            string propertyName = reader.GetString()!;
            reader.Read();

            switch (propertyName)
            {
                case "x":
                    output.x = reader.GetSingle();
                    break;
                case "y":
                    output.y = reader.GetSingle();
                    break;
                default:
                    reader.Skip(); // Ignore unknown properties
                    break;
            }
        }

        throw new JsonException("Incomplete Vector2 object");
    }

}
