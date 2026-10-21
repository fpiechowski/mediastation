using System.Text.Json;
using System.Text.Json.Nodes;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class CoreMessageContractTests
{
    // ── Koperta: dokładny JSON i round-trip ───────────────────────────────────

    [Fact]
    public void Action_envelope_serializes_exactly_and_round_trips()
    {
        var action = new Action(ActionKind.AppMenu, ActionPhase.Long, ActionSource.Remote, 123456);

        var json = ContractJson.SerializeEnvelope(action);

        Assert.Equal(
            """{"v":1,"type":"action","payload":{"kind":"appMenu","phase":"long","source":"remote","tsMono":123456}}""",
            json);
        Assert.Equal(action, ContractJson.DeserializeEnvelope<Action>(json));
    }

    [Fact]
    public void Set_input_routing_as_command_serializes_with_cmd_id_first_and_explicit_null()
    {
        Command command = new SetInputRouting(7, InputMode.Shell, null, PointerPolicy.Blocked, 3);

        var json = ContractJson.SerializeEnvelope(command);

        Assert.Equal(
            """{"v":1,"type":"setInputRouting","payload":{"cmdId":7,"mode":"shell","appType":null,"pointer":"blocked","epoch":3}}""",
            json);
        var back = ContractJson.DeserializeEnvelope<Command>(json);
        Assert.IsType<SetInputRouting>(back);
        Assert.Equal(command, back);
    }

    [Fact]
    public void Set_input_routing_with_app_type_round_trips()
    {
        var routing = new SetInputRouting(8, InputMode.App, AppType.Web, PointerPolicy.Allowed, 4);

        var json = ContractJson.SerializeEnvelope<Command>(routing);

        Assert.Equal(
            """{"v":1,"type":"setInputRouting","payload":{"cmdId":8,"mode":"app","appType":"web","pointer":"allowed","epoch":4}}""",
            json);
        Assert.Equal(routing, ContractJson.DeserializeEnvelope<SetInputRouting>(json));
    }

    [Fact]
    public void Command_completed_envelope_serializes_exactly_and_round_trips()
    {
        Event completed = new CommandCompleted(7);

        var json = ContractJson.SerializeEnvelope(completed);

        Assert.Equal("""{"v":1,"type":"commandCompleted","payload":{"cmdId":7}}""", json);
        var back = ContractJson.DeserializeEnvelope<Event>(json);
        Assert.IsType<CommandCompleted>(back);
        Assert.Equal(completed, back);
    }

    [Fact]
    public void Command_failed_read_as_event_is_equal_command_failed()
    {
        Event failed = new CommandFailed(9, ErrorCode.CdpUnavailable);

        var json = ContractJson.SerializeEnvelope(failed);

        Assert.Equal("""{"v":1,"type":"commandFailed","payload":{"cmdId":9,"errorCode":"cdpUnavailable"}}""", json);
        var back = ContractJson.DeserializeEnvelope<Event>(json);
        Assert.IsType<CommandFailed>(back);
        Assert.Equal(failed, back);
    }

    [Fact]
    public void Serializing_unregistered_type_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ContractJson.SerializeEnvelope<object>(new ShellSnapshot(1, ShellScreen.Placeholder, [])));
    }

    // ── Macierz I/O: odrzucane koperty ────────────────────────────────────────

    [Fact]
    public void Unknown_type_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.DeserializeEnvelope<Event>("""{"v":1,"type":"nope","payload":{}}"""));
    }

    [Fact]
    public void Type_outside_requested_base_is_rejected()
    {
        var json = ContractJson.SerializeEnvelope<Command>(new SetInputRouting(7, InputMode.Shell, null, PointerPolicy.Blocked, 3));

        Assert.Throws<JsonException>(() => ContractJson.DeserializeEnvelope<Event>(json));
    }

    [Fact]
    public void Other_major_version_is_rejected()
    {
        Assert.Throws<JsonException>(() =>
            ContractJson.DeserializeEnvelope<Event>("""{"v":2,"type":"commandCompleted","payload":{"cmdId":7}}"""));
    }

    [Fact]
    public void Integer_enum_in_payload_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.DeserializeEnvelope<Action>(
            """{"v":1,"type":"action","payload":{"kind":"ok","phase":0,"source":"remote","tsMono":1}}"""));
    }

    [Fact]
    public void Missing_payload_field_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.DeserializeEnvelope<Command>(
            """{"v":1,"type":"setInputRouting","payload":{"cmdId":7,"mode":"shell","appType":null,"pointer":"blocked"}}"""));
    }

    [Fact]
    public void Null_payload_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.DeserializeEnvelope<Event>("""{"v":1,"type":"commandCompleted","payload":null}"""));
    }

    [Fact]
    public void Missing_payload_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.DeserializeEnvelope<Event>("""{"v":1,"type":"commandCompleted"}"""));
    }

    [Fact]
    public void Additional_id_in_envelope_is_accepted()
    {
        var action = ContractJson.DeserializeEnvelope<Action>(
            """{"v":1,"type":"action","id":"x","payload":{"kind":"ok","phase":"press","source":"keyboard","tsMono":42}}""");

        Assert.Equal(new Action(ActionKind.Ok, ActionPhase.Press, ActionSource.Keyboard, 42), action);
    }

    [Theory]
    [InlineData("""{"v":1,"type":"action","payload":{"kind":"up, down","phase":"press","source":"keyboard","tsMono":1}}""")]
    [InlineData("""{"v":1,"type":"action","payload":{"kind":"UP","phase":"press","source":"keyboard","tsMono":1}}""")]
    [InlineData("""{"v":1,"type":"action","payload":{"Kind":"ok","phase":"press","source":"keyboard","tsMono":1}}""")]
    [InlineData("""{"V":1,"type":"action","payload":{"kind":"ok","phase":"press","source":"keyboard","tsMono":1}}""")]
    [InlineData("""{"v":1,"Type":"action","payload":{"kind":"ok","phase":"press","source":"keyboard","tsMono":1}}""")]
    public void Strict_envelope_row_from_matrix_is_rejected(string json)
    {
        Assert.Throws<JsonException>(() => ContractJson.DeserializeEnvelope<Action>(json));
    }

    [Fact]
    public void Additional_payload_field_is_accepted()
    {
        var action = ContractJson.DeserializeEnvelope<Action>(
            """{"v":1,"type":"action","payload":{"kind":"ok","phase":"press","source":"keyboard","tsMono":42,"extra":true}}""");

        Assert.Equal(new Action(ActionKind.Ok, ActionPhase.Press, ActionSource.Keyboard, 42), action);
    }

    // ── Rejestr ───────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_names_are_unique_camel_case_and_do_not_collide_with_bridge_types()
    {
        var names = MessageTypes.All.Select(MessageTypes.NameOf).ToArray();

        Assert.Equal(["action", "setInputRouting", "commandCompleted", "commandFailed"], names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(Envelope.SnapshotType, names);
        Assert.DoesNotContain(Envelope.ReadyType, names);
    }

    [Fact]
    public void Registry_holds_only_concrete_records()
    {
        Assert.All(MessageTypes.All, t => Assert.True(t.IsSealed && !t.IsAbstract, t.Name));
    }

    // ── Schematy ──────────────────────────────────────────────────────────────

    [Fact]
    public void Export_schemas_covers_shell_snapshot_registered_records_and_config_files_only()
    {
        var files = ContractJson.ExportSchemas().Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "action.schema.json",
                "apps-config.schema.json",
                "command-completed.schema.json",
                "command-failed.schema.json",
                "input-config.schema.json",
                "set-input-routing.schema.json",
                "settings.schema.json",
                "shell-snapshot.schema.json",
            ],
            files);
    }

    [Fact]
    public void Action_schema_has_title_required_and_enum_values()
    {
        var schema = Schema("action.schema.json");

        Assert.Equal("Action", schema["title"]!.GetValue<string>());
        Assert.Equal(["kind", "phase", "source", "tsMono"], Strings(schema["required"]));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());

        var properties = schema["properties"]!;
        AssertNamedEnum(properties["kind"], "ActionKind", ["up", "down", "left", "right", "ok", "back", "home", "appMenu", "keyboard"]);
        AssertNamedEnum(properties["phase"], "ActionPhase", ["press", "repeat", "long"]);
        AssertNamedEnum(properties["source"], "ActionSource", ["remote", "keyboard", "gamepad"]);
        Assert.Equal("integer", properties["tsMono"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Set_input_routing_schema_has_title_required_and_enum_values()
    {
        var schema = Schema("set-input-routing.schema.json");

        Assert.Equal("SetInputRouting", schema["title"]!.GetValue<string>());
        Assert.Equal(["cmdId", "mode", "appType", "pointer", "epoch"], Strings(schema["required"]));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());

        var properties = schema["properties"]!;
        Assert.Equal("integer", properties["cmdId"]!["type"]!.GetValue<string>());
        AssertNamedEnum(properties["mode"], "InputMode", ["shell", "app", "osk"]);
        AssertNamedEnum(properties["pointer"], "PointerPolicy", ["blocked", "allowed"]);
        Assert.Equal("integer", properties["epoch"]!["type"]!.GetValue<string>());

        // appType: nazwany enum albo null.
        var anyOf = properties["appType"]!["anyOf"]!.AsArray();
        Assert.Equal(2, anyOf.Count);
        AssertNamedEnum(anyOf[0], "AppType", ["web", "moonlight", "native"]);
        Assert.Equal("null", anyOf[1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Command_completed_schema_has_title_and_required_cmd_id()
    {
        var schema = Schema("command-completed.schema.json");

        Assert.Equal("CommandCompleted", schema["title"]!.GetValue<string>());
        Assert.Equal(["cmdId"], Strings(schema["required"]));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void Command_failed_schema_has_title_required_and_error_codes()
    {
        var schema = Schema("command-failed.schema.json");

        Assert.Equal("CommandFailed", schema["title"]!.GetValue<string>());
        Assert.Equal(["cmdId", "errorCode"], Strings(schema["required"]));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        AssertNamedEnum(
            schema["properties"]!["errorCode"],
            "ErrorCode",
            ["unexpected", "portInUse", "profileInUse", "cdpUnavailable", "updateDownloadFailed", "updateChecksumMismatch"]);
    }

    [Fact]
    public void Shell_snapshot_schema_in_export_matches_single_export()
    {
        Assert.Equal(ContractJson.ExportSchema(), ContractJson.ExportSchemas()["shell-snapshot.schema.json"]);
    }

    private static JsonNode Schema(string file) => JsonNode.Parse(ContractJson.ExportSchemas()[file])!;

    private static string[] Strings(JsonNode? array) => array!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

    private static void AssertNamedEnum(JsonNode? node, string title, string[] values)
    {
        Assert.NotNull(node);
        Assert.Equal(title, node["title"]!.GetValue<string>());
        Assert.Equal(values, Strings(node["enum"]));
    }
}
