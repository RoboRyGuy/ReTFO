
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReTFO.ThermalOverlay.Config;

// Struct used for communicating value states in shaders
[ShaderValue_ConverterAttribute]
public struct ShaderValue<T>
{
    // Constructs a shader value which will reset properties
    public ShaderValue()
    {
        IsSet = false;
        Value = default(T)!;
    }

    // Constructs a shader value which will explicitly set properties
    public ShaderValue(T value)
    {
        IsSet = true;
        Value = value;
    }

    public bool IsSet;  // True -> The value was explcitly set; False -> This is the default / an unset value
    public T Value;     // The value in question

    public static implicit operator ShaderValue<T>(T? value)
        => new ShaderValue<T>() { IsSet = value != null, Value = value! };
    public static implicit operator T(ShaderValue<T> value)
        => value.Value;
}

// An attribute for JSON which tells it how to convert ShaderValues
[AttributeUsage(AttributeTargets.Struct)]
public class ShaderValue_ConverterAttribute : JsonConverterAttribute
{
    public override JsonConverter? CreateConverter(Type typeToConvert)
    {
        var itemType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ShaderValue_JsonConverter<>)
                                .MakeGenericType(itemType);
        return (JsonConverter)Activator
            .CreateInstance(converterType)!;
    }
}

// Converts a ShaderValue to JSON in a more intuitive way
public class ShaderValue_JsonConverter<T> : JsonConverter<ShaderValue<T>>
{
    const string NonValue = "<unset>";

    public override void Write(Utf8JsonWriter writer, ShaderValue<T> value, JsonSerializerOptions options)
    {
        if (value.IsSet)
            JsonSerializer.Serialize<T>(writer, value.Value, options);
        else
            writer.WriteStringValue(NonValue);
    }

    public override ShaderValue<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            if (reader.ValueTextEquals(NonValue))
            {
                reader.Read();
                return new ShaderValue<T>() { IsSet = false };
            }
        }

        T? value = JsonSerializer.Deserialize<T>(ref reader, options)!;
        return new ShaderValue<T>(value);
    }
}