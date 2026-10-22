using System.Text.Json;
using System.Text.Json.Nodes;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class ShellSnapshotContractTests
{
    private static readonly ShellSnapshot ErrorSnapshot = new(
        7,
        ShellScreen.ConfigError,
        [new ConfigError("settings.json", null, "brak pliku"), new ConfigError("input.json", "$.a", "nieczytelny JSON")],
        [],
        null);

    private static readonly ShellSnapshot PlaceholderSnapshot = new(
        1,
        ShellScreen.Placeholder,
        [],
        [new AppSnapshot("netflix", AppCategory.Video, AppLifecycleState.NotRunning)],
        new FocusRequest(FocusRequest.DefaultTarget, 1));

    [Fact]
    public void Serializes_camel_case_enum_as_string_and_explicit_null()
    {
        var json = ContractJson.Serialize(ErrorSnapshot);

        Assert.Equal(
            """{"rev":7,"screen":"configError","configErrors":[{"file":"settings.json","path":null,"reason":"brak pliku"},{"file":"input.json","path":"$.a","reason":"nieczytelny JSON"}],"apps":[],"focusRequest":null}""",
            json);
    }

    [Fact]
    public void Round_trips()
    {
        var back = ContractJson.Deserialize<ShellSnapshot>(ContractJson.Serialize(ErrorSnapshot));

        Assert.NotNull(back);
        Assert.Equal(ErrorSnapshot.Rev, back.Rev);
        Assert.Equal(ErrorSnapshot.Screen, back.Screen);
        Assert.Equal(ErrorSnapshot.ConfigErrors, back.ConfigErrors);
        Assert.Equal(ErrorSnapshot.Apps, back.Apps);
        Assert.Equal(ErrorSnapshot.FocusRequest, back.FocusRequest);
    }

    [Fact]
    public void Placeholder_screen_serializes_apps_and_focus_request()
    {
        var json = ContractJson.Serialize(PlaceholderSnapshot);

        Assert.Equal(
            """{"rev":1,"screen":"placeholder","configErrors":[],"apps":[{"appId":"netflix","category":"video","state":"notRunning"}],"focusRequest":{"target":"default","seq":1}}""",
            json);
        var back = ContractJson.Deserialize<ShellSnapshot>(json);
        Assert.NotNull(back);
        Assert.Equal(PlaceholderSnapshot.Rev, back.Rev);
        Assert.Equal(PlaceholderSnapshot.Screen, back.Screen);
        Assert.Equal(PlaceholderSnapshot.ConfigErrors, back.ConfigErrors);
        Assert.Equal(PlaceholderSnapshot.Apps, back.Apps);
        Assert.Equal(PlaceholderSnapshot.FocusRequest, back.FocusRequest);
    }

    [Fact]
    public void Envelope_wraps_snapshot()
    {
        var json = ContractJson.Serialize(Envelope.Snapshot(PlaceholderSnapshot));

        Assert.Equal(
            """{"v":1,"type":"snapshot","payload":{"rev":1,"screen":"placeholder","configErrors":[],"apps":[{"appId":"netflix","category":"video","state":"notRunning"}],"focusRequest":{"target":"default","seq":1}}}""",
            json);
    }

    [Fact]
    public void Ready_envelope_parses_with_null_payload()
    {
        var envelope = ContractJson.Deserialize<Envelope<JsonElement?>>("""{"v":1,"type":"ready","payload":null}""");

        Assert.NotNull(envelope);
        Assert.Equal(Envelope.Version, envelope.V);
        Assert.Equal(Envelope.ReadyType, envelope.Type);
        Assert.Null(envelope.Payload);
    }

    [Fact]
    public void Envelope_with_additional_id_field_is_accepted()
    {
        var envelope = ContractJson.Deserialize<Envelope<JsonElement?>>("""{"v":1,"type":"ready","id":"x1","payload":null}""");

        Assert.NotNull(envelope);
        Assert.Equal(Envelope.ReadyType, envelope.Type);
    }

    [Fact]
    public void Envelope_with_pascal_case_type_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.Deserialize<Envelope<JsonElement?>>("""{"v":1,"Type":"ready","payload":null}"""));
    }

    [Fact]
    public void Read_options_are_strict_and_writing_options_stay_lenient()
    {
        Assert.NotSame(ContractJson.Options, ContractJson.ReadOptions);
        Assert.True(ContractJson.Options.PropertyNameCaseInsensitive);
        Assert.False(ContractJson.ReadOptions.PropertyNameCaseInsensitive);
        Assert.True(ContractJson.ReadOptions.IsReadOnly);
    }

    [Theory]
    [InlineData("""{"rev":1,"screen":"placeholder","configErrors":[],"focusRequest":null}""")]
    [InlineData("""{"rev":1,"screen":"placeholder","configErrors":[],"apps":[]}""")]
    public void Missing_property_is_rejected(string json)
    {
        Assert.Throws<JsonException>(() => ContractJson.Deserialize<ShellSnapshot>(json));
    }

    [Fact]
    public void Integer_enum_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.Deserialize<ShellSnapshot>(
            """{"rev":1,"screen":0,"configErrors":[],"apps":[],"focusRequest":null}"""));
    }

    [Fact]
    public void Unknown_app_lifecycle_state_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.Deserialize<ShellSnapshot>(
            """{"rev":1,"screen":"placeholder","configErrors":[],"apps":[{"appId":"netflix","category":"video","state":"sleeping"}],"focusRequest":null}"""));
    }

    [Fact]
    public void Schema_requires_rev_screen_config_errors_apps_and_focus_request()
    {
        var schema = JsonNode.Parse(ContractJson.ExportSchema())!.AsObject();

        var properties = schema["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("rev"));
        Assert.True(properties.ContainsKey("screen"));
        Assert.True(properties.ContainsKey("configErrors"));
        Assert.True(properties.ContainsKey("apps"));
        Assert.True(properties.ContainsKey("focusRequest"));

        var required = schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(["rev", "screen", "configErrors", "apps", "focusRequest"], required);

        Assert.Equal("integer", properties["rev"]!["type"]!.GetValue<string>());
        var screens = properties["screen"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(["placeholder", "configError"], screens);
    }

    [Fact]
    public void Schema_marks_config_error_path_as_nullable_and_required()
    {
        var schema = JsonNode.Parse(ContractJson.ExportSchema())!;
        var item = schema["properties"]!["configErrors"]!["items"]!;

        Assert.Equal("ConfigError", item["title"]!.GetValue<string>());
        var pathTypes = item["properties"]!["path"]!["type"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(["string", "null"], pathTypes);
        var required = item["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(["file", "path", "reason"], required);
    }

    [Fact]
    public void Schema_describes_apps_and_focus_request()
    {
        var schema = JsonNode.Parse(ContractJson.ExportSchema())!;
        var properties = schema["properties"]!;

        var app = properties["apps"]!["items"]!;
        Assert.Equal("AppSnapshot", app["title"]!.GetValue<string>());
        Assert.Equal(
            ["appId", "category", "state"],
            app["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        var category = app["properties"]!["category"]!;
        Assert.Equal("AppCategory", category["title"]!.GetValue<string>());
        Assert.Equal(["video", "audio", "game"], category["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        var state = app["properties"]!["state"]!;
        Assert.Equal("AppLifecycleState", state["title"]!.GetValue<string>());
        Assert.Equal(
            ["notRunning", "starting", "foreground", "background", "stopping", "failed"],
            state["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());

        var focus = properties["focusRequest"]!;
        Assert.Equal("FocusRequest", focus["title"]!.GetValue<string>());
        Assert.Equal(["object", "null"], focus["type"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        Assert.Equal(["target", "seq"], focus["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
    }
}
