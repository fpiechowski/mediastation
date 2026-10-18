using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MediaStation.Core.Contracts;

/// <summary>
/// Wspólna serializacja kontraktów (AD-16): camelCase, enumy jako stringi, jawne <c>null</c>.
/// </summary>
public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

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

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>
    /// JSON Schema snapshotu shella, źródło generowanych typów TS (<c>web/contracts/generated</c>).
    /// </summary>
    public static string ExportSchema() => ExportSchema(typeof(ShellSnapshot)).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

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

                // Nazwane typy (rekordy, enumy) → nazwane typy TS z json-schema-to-typescript.
                if (type.IsEnum || isObject)
                {
                    obj.Insert(0, "title", Name(type));
                }

                if (isObject && obj.ContainsKey("properties"))
                {
                    obj["additionalProperties"] = false;
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
