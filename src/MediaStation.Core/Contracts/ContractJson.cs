using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MediaStation.Core.Config;

namespace MediaStation.Core.Contracts;

/// <summary>
/// Wspólna serializacja kontraktów (AD-16): camelCase, enumy jako stringi, jawne <c>null</c>.
/// <see cref="Options"/> służy zapisowi i schematom; odczyt idzie przez ścisłe <see cref="ReadOptions"/>.
/// </summary>
public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Ścisły odczyt kontraktów: nazwy pól z rozróżnieniem wielkości liter, enum tylko jako dokładna nazwa camelCase.
    /// Dodatkowe pola koperty i payloadu nadal są ignorowane.
    /// </summary>
    public static JsonSerializerOptions ReadOptions { get; } = CreateReadOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.Strict,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions(Options)
        {
            PropertyNameCaseInsensitive = false,
        };

        // Przed odziedziczonym JsonStringEnumConverter, który czyta bez względu na wielkość liter i sumuje listy po przecinku.
        options.Converters.Insert(0, new StrictEnumConverterFactory());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, ReadOptions);

    /// <summary>
    /// Zapisuje komunikat w kopercie <c>{ v, type, payload }</c>: <c>type</c> z <see cref="MessageTypes"/>,
    /// payload serializowany po typie runtime, bez dyskryminatora (AD-16).
    /// </summary>
    /// <exception cref="ArgumentException">Typ runtime nie jest w <see cref="MessageTypes"/>.</exception>
    public static string SerializeEnvelope<TBase>(TBase message)
        where TBase : class
    {
        ArgumentNullException.ThrowIfNull(message);
        var type = message.GetType();
        var name = MessageTypes.NameOf(type);
        var payload = JsonSerializer.SerializeToElement(message, type, Options);
        return Serialize(new Envelope<JsonElement>(Envelope.Version, name, payload));
    }

    /// <summary>
    /// Czyta komunikat z koperty: <c>type</c> wybiera rekord z <see cref="MessageTypes"/>, który musi dziedziczyć
    /// po <typeparamref name="TBase"/>. Dodatkowe pola koperty (np. <c>id</c>) są ignorowane.
    /// </summary>
    /// <exception cref="JsonException">
    /// Inny major <c>v</c>, nieznany <c>type</c>, typ spoza <typeparamref name="TBase"/> albo payload niezgodny z rekordem.
    /// </exception>
    public static TBase DeserializeEnvelope<TBase>(string json)
        where TBase : class
    {
        var envelope = Deserialize<Envelope<JsonElement>>(json)
            ?? throw new JsonException("Koperta jest null.");

        if (envelope.V != Envelope.Version)
        {
            throw new JsonException($"Nieobsługiwana wersja koperty v={envelope.V}, oczekiwano {Envelope.Version}.");
        }

        if (!MessageTypes.TryGet(envelope.Type, out var type))
        {
            throw new JsonException($"Nieznany typ komunikatu '{envelope.Type}'.");
        }

        if (!typeof(TBase).IsAssignableFrom(type))
        {
            throw new JsonException($"Typ komunikatu '{envelope.Type}' nie jest {typeof(TBase).Name}.");
        }

        return envelope.Payload.Deserialize(type, ReadOptions) as TBase
            ?? throw new JsonException($"Payload komunikatu '{envelope.Type}' jest null.");
    }

    /// <summary>
    /// JSON Schema snapshotu shella, źródło generowanych typów TS (<c>web/contracts/generated</c>).
    /// </summary>
    public static string ExportSchema() => ExportSchema(typeof(ShellSnapshot)).ToJsonString(IndentedOptions);

    /// <summary>
    /// Schematy wszystkich eksportowanych kontraktów: nazwa pliku (<c>kebab-case.schema.json</c>) → JSON Schema.
    /// Obejmuje <see cref="ShellSnapshot"/>, każdy rekord z <see cref="MessageTypes"/> i trzy pliki configu
    /// (<see cref="AppsConfig"/>, <see cref="InputConfig"/>, <see cref="Settings"/>, AD-11); bazy komunikatów
    /// (<see cref="Intent"/>, <see cref="Command"/>, <see cref="Event"/>) nie mają własnego schematu.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExportSchemas()
    {
        var schemas = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in (Type[])[typeof(ShellSnapshot), .. MessageTypes.All, .. ConfigTypes])
        {
            schemas.Add(SchemaFileName(type), ExportSchema(type).ToJsonString(IndentedOptions));
        }

        return schemas;
    }

    /// <summary>Korzenie plików <c>config/</c>; runtime czyta je ściśle przez <see cref="ConfigJson.Options"/> (ReadOptions + reguły plików).</summary>
    private static readonly Type[] ConfigTypes = [typeof(AppsConfig), typeof(InputConfig), typeof(Settings)];

    /// <summary>Nazwa pliku schematu dla typu, np. <c>set-input-routing.schema.json</c>.</summary>
    public static string SchemaFileName(Type type) => JsonNamingPolicy.KebabCaseLower.ConvertName(Name(type)) + ".schema.json";

    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public static JsonNode ExportSchema(Type type)
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = static (context, node) =>
            {
                if (node is not JsonObject obj)
                {
                    return node;
                }

                var type = context.TypeInfo.Type;
                var isObject = context.TypeInfo.Kind == JsonTypeInfoKind.Object;

                // Nullable enum (np. AppType?) → anyOf [nazwany enum, null]: TS dostaje `AppType | null`, a nie anonimową unię.
                if (Nullable.GetUnderlyingType(type) is { IsEnum: true } enumType && obj["enum"] is JsonArray values)
                {
                    var members = new JsonArray();
                    foreach (var value in values)
                    {
                        if (value is not null)
                        {
                            members.Add(value.DeepClone());
                        }
                    }

                    var named = new JsonObject { ["title"] = Name(enumType), ["enum"] = members };
                    return new JsonObject { ["anyOf"] = new JsonArray(named, new JsonObject { ["type"] = "null" }) };
                }

                // Nazwane typy (rekordy, enumy) → nazwane typy TS z json-schema-to-typescript.
                if (type.IsEnum || isObject)
                {
                    obj.Insert(0, "title", Name(type));
                }

                if (isObject && obj.ContainsKey("properties"))
                {
                    obj["additionalProperties"] = false;
                }

                // Typ pochodny polimorficznej bazy: dyskryminator jest wymagany, więc TS dostaje rozłączną unię (`type: "web"`).
                if (isObject
                    && context.BaseTypeInfo?.PolymorphismOptions?.TypeDiscriminatorPropertyName is { } discriminator
                    && context.BaseTypeInfo.Type != type
                    && obj["properties"] is JsonObject derivedProperties
                    && derivedProperties.ContainsKey(discriminator))
                {
                    if (obj["required"] is not JsonArray required)
                    {
                        required = [];
                        obj["required"] = required;
                    }

                    if (!required.Any(n => n?.GetValue<string>() == discriminator))
                    {
                        required.Insert(0, discriminator);
                    }
                }

                return obj;
            },
        };

        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(Options, type, exporterOptions);
        if (schema is JsonObject root)
        {
            root.Insert(0, "$schema", "https://json-schema.org/draft/2020-12/schema");
        }

        return schema;
    }

    private static string Name(Type t) => t.IsGenericType ? t.Name[..t.Name.IndexOf('`')] : t.Name;
}
