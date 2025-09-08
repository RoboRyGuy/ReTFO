using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace ReTFO.ThermalOverlay;

/// <summary>
/// JSON Converter for Quaternion objects
/// </summary>
public class Quaternion_JsonConverter : JsonConverter<Quaternion>
{
    public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.x);
        writer.WriteNumber("y", value.y);
        writer.WriteNumber("z", value.z);
        writer.WriteNumber("w", value.w);
        writer.WriteEndObject();
    }

    public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token");

        Quaternion output = new();
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
                case "z":
                    output.z = reader.GetSingle();
                    break;
                case "w":
                    output.w = reader.GetSingle();
                    break;
                default:
                    reader.Skip(); // Ignore unknown properties
                    break;
            }
        }

        throw new JsonException("Incomplete Vector4 object");
    }

}
