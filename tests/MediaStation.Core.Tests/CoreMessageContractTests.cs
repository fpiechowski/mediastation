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
    public void Orchestrator_started_envelope_serializes_exactly_and_round_trips()
    {
        Event started = new OrchestratorStarted();

        var json = ContractJson.SerializeEnvelope(started);

        Assert.Equal("""{"v":1,"type":"orchestratorStarted","payload":{}}""", json);
        var back = ContractJson.DeserializeEnvelope<Event>(json);
        Assert.IsType<OrchestratorStarted>(back);
    }

    [Fact]
    public void System_resumed_envelope_serializes_exactly_and_round_trips()
    {
        Event resumed = new SystemResumed("PBT_APMRESUMEAUTOMATIC");

        var json = ContractJson.SerializeEnvelope(resumed);

        Assert.Equal(
            """{"v":1,"type":"systemResumed","payload":{"source":"PBT_APMRESUMEAUTOMATIC"}}""",
            json);
        var back = ContractJson.DeserializeEnvelope<Event>(json);
        Assert.IsType<SystemResumed>(back);
        Assert.Equal(resumed, back);
    }

    [Fact]
    public void Launch_app_intent_envelope_serializes_exactly_and_round_trips()
    {
        Intent launch = new LaunchApp("netflix");

        var json = ContractJson.SerializeEnvelope(launch);

        Assert.Equal("""{"v":1,"type":"launchApp","payload":{"appId":"netflix"}}""", json);
        var back = ContractJson.DeserializeEnvelope<Intent>(json);
        Assert.IsType<LaunchApp>(back);
        Assert.Equal(launch, back);
    }

    [Fact]
    public void Close_app_intent_envelope_serializes_exactly_and_round_trips()
    {
        Intent close = new CloseApp("netflix");

        var json = ContractJson.SerializeEnvelope(close);

        Assert.Equal("""{"v":1,"type":"closeApp","payload":{"appId":"netflix"}}""", json);
        var back = ContractJson.DeserializeEnvelope<Intent>(json);
        Assert.IsType<CloseApp>(back);
        Assert.Equal(close, back);
    }

    [Fact]
    public void Restart_app_intent_envelope_serializes_exactly_and_round_trips()
    {
        Intent restart = new RestartApp("netflix");

        var json = ContractJson.SerializeEnvelope(restart);

        Assert.Equal("""{"v":1,"type":"restartApp","payload":{"appId":"netflix"}}""", json);
        var back = ContractJson.DeserializeEnvelope<Intent>(json);
        Assert.IsType<RestartApp>(back);
        Assert.Equal(restart, back);
    }

    [Fact]
    public void Start_app_command_envelope_serializes_with_cmd_id_first_and_round_trips()
    {
        Command start = new StartApp(2, "netflix");

        var json = ContractJson.SerializeEnvelope(start);

        Assert.Equal("""{"v":1,"type":"startApp","payload":{"cmdId":2,"appId":"netflix"}}""", json);
        var back = ContractJson.DeserializeEnvelope<Command>(json);
        Assert.IsType<StartApp>(back);
        Assert.Equal(start, back);
    }

    [Fact]
    public void Stop_app_command_envelope_serializes_with_cmd_id_first_and_round_trips()
    {
        Command stop = new StopApp(6, "netflix");

        var json = ContractJson.SerializeEnvelope(stop);

        Assert.Equal("""{"v":1,"type":"stopApp","payload":{"cmdId":6,"appId":"netflix"}}""", json);
        var back = ContractJson.DeserializeEnvelope<Command>(json);
        Assert.IsType<StopApp>(back);
        Assert.Equal(stop, back);
    }

    [Fact]
    public void Process_exited_event_envelope_serializes_exactly_and_round_trips()
    {
        Event exited = new ProcessExited("netflix");

        var json = ContractJson.SerializeEnvelope(exited);

        Assert.Equal("""{"v":1,"type":"processExited","payload":{"appId":"netflix"}}""", json);
        var back = ContractJson.DeserializeEnvelope<Event>(json);
        Assert.IsType<ProcessExited>(back);
        Assert.Equal(exited, back);
    }

    [Fact]
    public void Foreground_changed_event_envelope_serializes_exactly_and_round_trips()
    {
        Event changed = new ForegroundChanged(1234);

        var json = ContractJson.SerializeEnvelope(changed);

        Assert.Equal("""{"v":1,"type":"foregroundChanged","payload":{"hwnd":1234}}""", json);
        var back = ContractJson.DeserializeEnvelope<Event>(json);
        Assert.IsType<ForegroundChanged>(back);
        Assert.Equal(changed, back);
    }

    [Fact]
    public void App_window_attached_event_envelope_serializes_exactly_and_round_trips()
    {
        Event attached = new AppWindowAttached("netflix", 6699, 4321);

        var json = ContractJson.SerializeEnvelope(attached);

        Assert.Equal(
            """{"v":1,"type":"appWindowAttached","payload":{"appId":"netflix","hwnd":6699,"rootPid":4321}}""",
            json);
        var back = ContractJson.DeserializeEnvelope<Event>(json);
        Assert.IsType<AppWindowAttached>(back);
        Assert.Equal(attached, back);
    }

    [Fact]
    public void Activate_window_command_envelope_serializes_exactly_and_round_trips()
    {
        Command activate = new ActivateWindow(3, 6699);

        var json = ContractJson.SerializeEnvelope(activate);

        Assert.Equal("""{"v":1,"type":"activateWindow","payload":{"cmdId":3,"hwnd":6699}}""", json);
        var back = ContractJson.DeserializeEnvelope<Command>(json);
        Assert.IsType<ActivateWindow>(back);
        Assert.Equal(activate, back);
    }

    [Fact]
    public void Minimize_window_command_envelope_serializes_exactly_and_round_trips()
    {
        Command minimize = new MinimizeWindow(4, 6699);

        var json = ContractJson.SerializeEnvelope(minimize);

        Assert.Equal("""{"v":1,"type":"minimizeWindow","payload":{"cmdId":4,"hwnd":6699}}""", json);
        var back = ContractJson.DeserializeEnvelope<Command>(json);
        Assert.IsType<MinimizeWindow>(back);
        Assert.Equal(minimize, back);
    }

    [Fact]
    public void Show_shell_command_envelope_serializes_exactly_and_round_trips()
    {
        Command show = new ShowShell(5);

        var json = ContractJson.SerializeEnvelope(show);

        Assert.Equal("""{"v":1,"type":"showShell","payload":{"cmdId":5}}""", json);
        var back = ContractJson.DeserializeEnvelope<Command>(json);
        Assert.IsType<ShowShell>(back);
        Assert.Equal(show, back);
    }

    [Fact]
    public void Orchestrator_messages_share_the_orchestrator_queue_base()
    {
        Assert.IsAssignableFrom<IOrchestratorMessage>(new OrchestratorStarted());
        Assert.IsAssignableFrom<IOrchestratorMessage>(new CommandCompleted(1));
        Assert.IsAssignableFrom<IOrchestratorMessage>(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1));
        Assert.IsAssignableFrom<IOrchestratorMessage>(new LaunchApp("netflix"));
        Assert.IsAssignableFrom<IOrchestratorMessage>(new CloseApp("netflix"));
        Assert.IsAssignableFrom<IOrchestratorMessage>(new RestartApp("netflix"));
        Assert.IsAssignableFrom<IOrchestratorMessage>(new AppWindowAttached("netflix", 1, 2));
        Assert.IsAssignableFrom<IOrchestratorMessage>(new ProcessExited("netflix"));
        Assert.IsAssignableFrom<IOrchestratorMessage>(new ForegroundChanged(1));
    }

    [Fact]
    public void Serializing_unregistered_type_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ContractJson.SerializeEnvelope<object>(new ShellSnapshot(1, ShellScreen.Placeholder, [], [], null)));
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
    public void Duplicate_envelope_key_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.Deserialize<Envelope<JsonElement?>>(
            """{"v":1,"type":"snapshot","type":"ready","payload":null}"""));
    }

    [Fact]
    public void Duplicate_payload_key_is_rejected()
    {
        Assert.Throws<JsonException>(() => ContractJson.DeserializeEnvelope<Action>(
            """{"v":1,"type":"action","payload":{"kind":"up","kind":"down","phase":"press","source":"keyboard","tsMono":1}}"""));
    }

    [Fact]
    public void Aliased_enum_names_are_read_deterministically()
    {
        Assert.Equal(AliasedEnum.First, ContractJson.Deserialize<AliasedEnum>("\"first\""));
        Assert.Equal(AliasedEnum.First, ContractJson.Deserialize<AliasedEnum>("\"second\""));
        Assert.Throws<JsonException>(() => ContractJson.Deserialize<AliasedEnum>("\"third\""));
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

        Assert.Equal(["action", "launchApp", "closeApp", "restartApp", "setInputRouting", "startApp", "stopApp", "activateWindow", "minimizeWindow", "showShell", "commandCompleted", "commandFailed", "orchestratorStarted", "systemResumed", "appWindowAttached", "processExited", "foregroundChanged"], names);
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
                "activate-window.schema.json",
                "app-window-attached.schema.json",
                "apps-config.schema.json",
                "close-app.schema.json",
                "command-completed.schema.json",
                "command-failed.schema.json",
                "foreground-changed.schema.json",
                "input-config.schema.json",
                "launch-app.schema.json",
                "minimize-window.schema.json",
                "orchestrator-started.schema.json",
                "process-exited.schema.json",
                "restart-app.schema.json",
                "set-input-routing.schema.json",
                "settings.schema.json",
                "shell-snapshot.schema.json",
                "show-shell.schema.json",
                "start-app.schema.json",
                "stop-app.schema.json",
                "system-resumed.schema.json",
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
    public void Orchestrator_started_schema_has_title()
    {
        var schema = Schema("orchestrator-started.schema.json");

        Assert.Equal("OrchestratorStarted", schema["title"]!.GetValue<string>());
        Assert.Equal("object", schema["type"]!.GetValue<string>());
    }

    [Fact]
    public void System_resumed_schema_has_title_required_and_string_source()
    {
        var schema = Schema("system-resumed.schema.json");

        Assert.Equal("SystemResumed", schema["title"]!.GetValue<string>());
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.Equal(["source"], Strings(schema["required"]));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal("string", schema["properties"]!["source"]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("launch-app.schema.json", "LaunchApp", "appId")]
    [InlineData("close-app.schema.json", "CloseApp", "appId")]
    [InlineData("restart-app.schema.json", "RestartApp", "appId")]
    [InlineData("start-app.schema.json", "StartApp", "cmdId,appId")]
    [InlineData("stop-app.schema.json", "StopApp", "cmdId,appId")]
    [InlineData("app-window-attached.schema.json", "AppWindowAttached", "appId,hwnd,rootPid")]
    [InlineData("process-exited.schema.json", "ProcessExited", "appId")]
    [InlineData("foreground-changed.schema.json", "ForegroundChanged", "hwnd")]
    [InlineData("activate-window.schema.json", "ActivateWindow", "cmdId,hwnd")]
    [InlineData("minimize-window.schema.json", "MinimizeWindow", "cmdId,hwnd")]
    [InlineData("show-shell.schema.json", "ShowShell", "cmdId")]
    public void Launch_path_schemas_have_title_required_fields_and_no_additional_properties(
        string file,
        string title,
        string required)
    {
        var schema = Schema(file);

        Assert.Equal(title, schema["title"]!.GetValue<string>());
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.Equal(required.Split(','), Strings(schema["required"]));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void Launch_path_schemas_type_app_id_as_string_and_window_handles_as_integer()
    {
        Assert.Equal("string", Schema("launch-app.schema.json")["properties"]!["appId"]!["type"]!.GetValue<string>());
        Assert.Equal("string", Schema("start-app.schema.json")["properties"]!["appId"]!["type"]!.GetValue<string>());
        Assert.Equal("string", Schema("close-app.schema.json")["properties"]!["appId"]!["type"]!.GetValue<string>());
        Assert.Equal("string", Schema("restart-app.schema.json")["properties"]!["appId"]!["type"]!.GetValue<string>());
        Assert.Equal("string", Schema("stop-app.schema.json")["properties"]!["appId"]!["type"]!.GetValue<string>());
        Assert.Equal("string", Schema("process-exited.schema.json")["properties"]!["appId"]!["type"]!.GetValue<string>());

        var attached = Schema("app-window-attached.schema.json")["properties"]!;
        Assert.Equal("string", attached["appId"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", attached["hwnd"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", attached["rootPid"]!["type"]!.GetValue<string>());

        Assert.Equal("integer", Schema("foreground-changed.schema.json")["properties"]!["hwnd"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", Schema("activate-window.schema.json")["properties"]!["hwnd"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", Schema("minimize-window.schema.json")["properties"]!["hwnd"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Shell_snapshot_schema_in_export_matches_single_export()
    {
        Assert.Equal(ContractJson.ExportSchema(), ContractJson.ExportSchemas()["shell-snapshot.schema.json"]);
    }

    private enum AliasedEnum
    {
        First = 1,
        Second = 1,
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
