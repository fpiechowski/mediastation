using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaStation.Core.Contracts;

/// <summary>Enum tylko jako string z dokładną nazwą camelCase; wszystko inne to <see cref="JsonException"/>.</summary>
internal sealed class StrictEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert))!;
}

internal sealed class StrictEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    private readonly Dictionary<string, T> _byName = new(StringComparer.Ordinal);

    private readonly Dictionary<T, string> _names = [];

    public StrictEnumConverter()
    {
        foreach (var value in Enum.GetValues<T>())
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
            _byName.Add(name, value);
            _names.Add(value, name);
        }
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Pusty JsonException: STJ dopisuje „could not be converted to <typ>. Path: …”.
        if (reader.TokenType == JsonTokenType.String && _byName.TryGetValue(reader.GetString()!, out var value))
        {
            return value;
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (!_names.TryGetValue(value, out var name))
        {
            throw new JsonException($"Wartość {value} spoza enuma {typeof(T).Name}.");
        }

        writer.WriteStringValue(name);
    }
}
