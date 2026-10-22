using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class ConfigValidationTests
{
    private const string Apps = ConfigPreflight.AppsFile;
    private const string Input = ConfigPreflight.InputFile;
    private const string SettingsJson = ConfigPreflight.SettingsFile;

    private const string NetflixFields =
        "\"id\": \"netflix\", \"category\": \"video\", \"title\": \"Netflix\", \"icon\": \"netflix.png\", \"service\": \"netflix\", \"browser\": \"edge\", \"cdpPort\": 9222";

    private const string Netflix = $$"""{ "type": "web", {{NetflixFields}} }""";

    private const string Moonlight =
        """{ "type": "moonlight", "id": "moonlight-pc", "category": "game", "title": "PC", "icon": "pc.png", "host": "desktop", "app": "Desktop" }""";

    private const string Plex =
        """{ "type": "native", "id": "plex", "category": "video", "title": "Plex", "icon": "plex.png", "exe": "C:\\Plex\\Plex.exe" }""";

    private const string ValidInput = """
        {
          "devices": [
            { "signature": "VID_1915&PID_1025", "source": "remote", "keys": { "0x26": "up", "0x0D": "ok", "0xAC": "home" } }
          ],
          "gamepadHome": ["back", "start"]
        }
        """;

    private static string AppsJson(params string[] entries) => $$"""{ "apps": [ {{string.Join(", ", entries)}} ] }""";

    private static string Web(string id = "netflix", string port = "9222", string extra = "") =>
        $$"""{ "type": "web", "id": "{{id}}", "category": "video", "title": "T", "icon": "i.png", "service": "netflix", "browser": "edge", "cdpPort": {{port}}{{extra}} }""";

    private static string InputJson(string keys = """{ "0x26": "up" }""", string signature = "VID_1915&PID_1025", string gamepadHome = """["back", "start"]""", string extra = "") =>
        $$"""{ "devices": [ { "signature": "{{signature}}", "source": "remote", "keys": {{keys}} } ], "gamepadHome": {{gamepadHome}}{{extra}} }""";

    private static Dictionary<string, ConfigFileState> Files(string? apps = null, string? input = null, string? settings = null) => new()
    {
        [Apps] = new ConfigFileState.Present(apps ?? AppsJson(Netflix, Moonlight, Plex)),
        [Input] = new ConfigFileState.Present(input ?? ValidInput),
        [SettingsJson] = new ConfigFileState.Present(settings ?? "{}"),
    };

    private static ConfigResult Validate(string? apps = null, string? input = null, string? settings = null) =>
        ConfigValidation.Validate(Files(apps, input, settings));

    private static ConfigError SingleError(string? apps = null, string? input = null, string? settings = null)
    {
        var result = Validate(apps, input, settings);
        Assert.Null(result.Config);
        return Assert.Single(result.Errors);
    }

    private static LoadedConfig Loaded(string? apps = null, string? input = null, string? settings = null)
    {
        var result = Validate(apps, input, settings);
        Assert.Empty(result.Errors);
        return Assert.IsType<LoadedConfig>(result.Config);
    }

    // ── Poprawny config ───────────────────────────────────────────────────────

    [Fact]
    public void Valid_config_loads_with_default_policies_and_settings()
    {
        var config = Loaded();

        Assert.Collection(
            config.Apps.Apps,
            a =>
            {
                var web = Assert.IsType<WebApp>(a);
                Assert.Equal(
                    new WebApp("netflix", AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222),
                    web);
                Assert.Equal(PointerPolicy.Blocked, web.Pointer);
            },
            a =>
            {
                var moonlight = Assert.IsType<MoonlightApp>(a);
                Assert.Equal("desktop", moonlight.Host);
                Assert.Equal("Desktop", moonlight.App);
                Assert.Equal("key:Escape", moonlight.Back);
                Assert.Equal(PointerPolicy.Allowed, moonlight.Pointer);
            },
            a =>
            {
                var native = Assert.IsType<NativeApp>(a);
                Assert.Equal(@"C:\Plex\Plex.exe", native.Exe);
                Assert.Empty(native.Args);
                Assert.Equal("home", native.Back);
                Assert.Equal(PointerPolicy.Allowed, native.Pointer);
            });

        var device = Assert.Single(config.Input.Devices);
        Assert.Equal("VID_1915&PID_1025", device.Signature);
        Assert.Equal(ActionSource.Remote, device.Source);
        Assert.Equal(ActionKind.Up, device.Keys["0x26"]);
        Assert.Equal(ActionKind.Ok, device.Keys["0x0D"]);
        Assert.Equal(ActionKind.Home, device.Keys["0xAC"]);
        Assert.Equal([GamepadButton.Back, GamepadButton.Start], config.Input.GamepadHome);
        Assert.Equal(ActionKind.Up, config.Input.Gamepad.DpadUp);
        Assert.Equal(ActionKind.Down, config.Input.Gamepad.DpadDown);
        Assert.Equal(ActionKind.Left, config.Input.Gamepad.DpadLeft);
        Assert.Equal(ActionKind.Right, config.Input.Gamepad.DpadRight);
        Assert.Equal(ActionKind.Ok, config.Input.Gamepad.A);
        Assert.Equal(ActionKind.Back, config.Input.Gamepad.B);
        Assert.Equal(ActionKind.Keyboard, config.Input.Gamepad.Y);
        Assert.Null(config.Input.Gamepad.Start);
        Assert.Equal(7, config.Input.Gamepad.Mappings().Count());
        Assert.Equal(600, config.Input.Timing.LongPressMs);
        Assert.Equal(400, config.Input.Timing.RepeatDelayMs);
        Assert.Equal(120, config.Input.Timing.RepeatIntervalMs);

        Assert.Equal(new Settings(), config.Settings);
        Assert.Equal(7, config.Settings.RatingPendingDays);
        Assert.Equal(30, config.Settings.StartingTimeout);
        Assert.Equal(45, config.Settings.StoppingTimeout);
        Assert.Equal(15, config.Settings.CdpConnectTimeout);
        Assert.Equal(30, config.Settings.CdpReconnectTimeout);
        Assert.Equal("fpiechowski/mediastation", config.Settings.Update.Repo);
    }

    [Fact]
    public void Explicit_values_override_defaults()
    {
        var config = Loaded(
            apps: AppsJson(
                Web(extra: """, "pointer": "allowed" """),
                """{ "type": "moonlight", "id": "pc", "category": "game", "title": "PC", "icon": "i", "host": "h", "app": "a", "back": "home", "pointer": "blocked" }""",
                """{ "type": "native", "id": "plex", "category": "video", "title": "P", "icon": "i", "exe": "p.exe", "args": ["--tv", "-f"], "back": "key:Escape" }"""),
            settings: """{ "ratingPendingDays": 3, "startingTimeout": 60, "stoppingTimeout": 50, "cdpConnectTimeout": 20, "cdpReconnectTimeout": 45, "update": { "repo": "someone/fork" } }""");

        Assert.Equal(PointerPolicy.Allowed, Assert.IsType<WebApp>(config.Apps.Apps[0]).Pointer);
        var moonlight = Assert.IsType<MoonlightApp>(config.Apps.Apps[1]);
        Assert.Equal("home", moonlight.Back);
        Assert.Equal(PointerPolicy.Blocked, moonlight.Pointer);
        var native = Assert.IsType<NativeApp>(config.Apps.Apps[2]);
        Assert.Equal(["--tv", "-f"], native.Args);
        Assert.Equal("key:Escape", native.Back);
        Assert.Equal(
            new Settings
            {
                RatingPendingDays = 3,
                StartingTimeout = 60,
                StoppingTimeout = 50,
                CdpConnectTimeout = 20,
                CdpReconnectTimeout = 45,
                Update = new UpdateSettings { Repo = "someone/fork" },
            },
            config.Settings);
    }

    [Fact]
    public void Partial_update_section_keeps_default_repo()
    {
        var config = Loaded(settings: """{ "startingTimeout": 40, "update": {} }""");

        Assert.Equal(40, config.Settings.StartingTimeout);
        Assert.Equal("fpiechowski/mediastation", config.Settings.Update.Repo);
    }

    [Fact]
    public void Type_not_first_is_read()
    {
        var config = Loaded(apps: $$"""{ "apps": [ { {{NetflixFields}}, "type": "web" } ] }""");

        Assert.IsType<WebApp>(Assert.Single(config.Apps.Apps));
    }

    [Fact]
    public void Empty_apps_and_devices_are_valid()
    {
        var config = Loaded(apps: """{ "apps": [] }""", input: """{ "devices": [], "gamepadHome": ["back", "start"] }""");

        Assert.Empty(config.Apps.Apps);
        Assert.Empty(config.Input.Devices);
    }

    [Fact]
    public void Sample_config_files_are_valid()
    {
        var result = ConfigValidation.Validate(SampleFiles());

        Assert.Empty(result.Errors);
        var config = Assert.IsType<LoadedConfig>(result.Config);
        Assert.Equal(3, config.Apps.Apps.Count);
        Assert.Equal(2, config.Input.Devices.Count);
        var remote = config.Input.Devices[0];
        Assert.Equal("VID_1915&PID_1025", remote.Signature);
        Assert.Equal(7, remote.Keys.Count);
        Assert.Equal(ActionKind.Back, remote.Keys["0xA6"]);
        var wildcard = config.Input.Devices[1];
        Assert.Equal(InputDevice.WildcardSignature, wildcard.Signature);
        Assert.Equal(ActionSource.Keyboard, wildcard.Source);
        Assert.Equal(8, wildcard.Keys.Count);
        Assert.Equal(ActionKind.Keyboard, wildcard.Keys["0x71"]);
        Assert.Equal(7, config.Input.Gamepad.Mappings().Count());
        Assert.Equal(ActionKind.Ok, config.Input.Gamepad.A);
        Assert.Null(config.Input.Gamepad.X);
        Assert.Equal(600, config.Input.Timing.LongPressMs);
        Assert.Equal(400, config.Input.Timing.RepeatDelayMs);
        Assert.Equal(120, config.Input.Timing.RepeatIntervalMs);
    }

    public static TheoryData<string, string, string, string> InvalidSamples => new()
    {
        { "apps-duplicate-port.json", Apps, "$.apps[1].cdpPort", "port 9222 jest już użyty w $.apps[0]" },
        { "apps-port-out-of-range.json", Apps, "$.apps[0].cdpPort", "port 9300 poza zakresem 9220–9239" },
        { "settings-unknown-field.json", SettingsJson, "$.x", "nieznane pole „x”" },
        {
            "input-gamepad-home-mapped.json",
            Input,
            "$.gamepad.back",
            "przycisk „back” należy do gamepadHome i nie może mieć własnego mapowania; ustaw dla niego null"
        },
    };

    [Theory]
    [MemberData(nameof(InvalidSamples))]
    public void Invalid_sample_is_rejected_with_one_named_error(string sample, string file, string path, string reason)
    {
        var files = SampleFiles();
        files[file] = new ConfigFileState.Present(File.ReadAllText(Path.Combine(SampleDir(), "invalid", sample)));

        var result = ConfigValidation.Validate(files);

        Assert.Null(result.Config);
        Assert.Equal(new ConfigError(file, path, reason), Assert.Single(result.Errors));
    }

    // ── Nieznane pole ─────────────────────────────────────────────────────────

    public static TheoryData<string, string, string, string> UnknownFields => new()
    {
        { Apps, AppsJson(Web(extra: """, "foo": 1""")), "$.apps[0].foo", "foo" },
        { Apps, """{ "apps": [], "foo": 1 }""", "$.foo", "foo" },
        { Apps, AppsJson(Moonlight, Web(extra: """, "back": "home" """)), "$.apps[1].back", "back" },
        { Input, InputJson().Replace("\"source\"", "\"foo\": 1, \"source\""), "$.devices[0].foo", "foo" },
        { Input, """{ "devices": [], "gamepadHome": ["back", "start"], "foo": 1 }""", "$.foo", "foo" },
        { Input, """{ "devices": [], "gamepadHome": ["back", "start"], "timing": { "foo": 1 } }""", "$.timing.foo", "foo" },
        { Input, """{ "devices": [], "gamepadHome": ["back", "start"], "gamepad": { "foo": "ok" } }""", "$.gamepad.foo", "foo" },
        { SettingsJson, """{ "foo": 1 }""", "$.foo", "foo" },
        { SettingsJson, """{ "update": { "repo": "a/b", "foo": 1 } }""", "$.update.foo", "foo" },
    };

    [Theory]
    [MemberData(nameof(UnknownFields))]
    public void Unknown_field_is_rejected_in_every_file_and_level(string file, string json, string path, string name)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present(json);

        var result = ConfigValidation.Validate(files);

        Assert.Null(result.Config);
        Assert.Equal(new ConfigError(file, path, $"nieznane pole „{name}”"), Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "id": "netflix", "category": "video", "title": "T", "icon": "i", "service": "netflix", "browser": "edge", "CdpPort": 9222 } ] }""", "$.apps[0].CdpPort", "CdpPort")]
    [InlineData(Apps, """{ "Apps": [] }""", "$.Apps", "Apps")]
    [InlineData(SettingsJson, """{ "StartingTimeout": 40 }""", "$.StartingTimeout", "StartingTimeout")]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "id": "netflix", "category": "video", "title": "T", "icon": "i", "service": "netflix", "browser": "edge", "cdpPort": 9222, "pointerPolicy": { "x": 1 } } ] }""", "$.apps[0].pointerPolicy", "pointerPolicy")]
    public void Field_name_in_other_case_or_alias_is_unknown(string file, string json, string path, string name)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present(json);

        var error = Assert.Single(ConfigValidation.Validate(files).Errors);

        Assert.Equal(new ConfigError(file, path, $"nieznane pole „{name}”"), error);
    }

    // ── Enumy: tylko dokładna nazwa camelCase ─────────────────────────────────

    [Theory]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "id": "n", "category": "VIDEO", "title": "T", "icon": "i", "service": "s", "browser": "edge", "cdpPort": 9222 } ] }""", "$.apps[0].category")]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "id": "n", "category": "video", "title": "T", "icon": "i", "service": "s", "browser": "Edge", "cdpPort": 9222 } ] }""", "$.apps[0].browser")]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "id": "n", "category": "audio, game", "title": "T", "icon": "i", "service": "s", "browser": "edge", "cdpPort": 9222 } ] }""", "$.apps[0].category")]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "id": "n", "category": 0, "title": "T", "icon": "i", "service": "s", "browser": "edge", "cdpPort": 9222 } ] }""", "$.apps[0].category")]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "id": "n", "category": "video", "title": "T", "icon": "i", "service": "s", "browser": "edge", "cdpPort": 9222, "pointer": "Allowed" } ] }""", "$.apps[0].pointer")]
    [InlineData(Input, """{ "devices": [ { "signature": "s", "source": "remote", "keys": { "0x26": "Up" } } ], "gamepadHome": ["back", "start"] }""", "$.devices[0].keys['0x26']")]
    [InlineData(Input, """{ "devices": [ { "signature": "s", "source": "remote", "keys": { "0x26": "up, down" } } ], "gamepadHome": ["back", "start"] }""", "$.devices[0].keys['0x26']")]
    [InlineData(Input, """{ "devices": [ { "signature": "s", "source": "Remote", "keys": {} } ], "gamepadHome": ["back", "start"] }""", "$.devices[0].source")]
    [InlineData(Input, """{ "devices": [], "gamepadHome": ["back", "START"] }""", "$.gamepadHome[1]")]
    [InlineData(Input, """{ "devices": [], "gamepadHome": ["start, back"] }""", "$.gamepadHome[0]")]
    [InlineData(Input, """{ "devices": [], "gamepadHome": ["back", null] }""", "$.gamepadHome[1]")]
    public void Enum_in_other_case_comma_list_or_number_is_wrong_value(string file, string json, string path)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present(json);

        var result = ConfigValidation.Validate(files);

        Assert.Null(result.Config);
        Assert.Equal(new ConfigError(file, path, "zła wartość albo typ"), Assert.Single(result.Errors));
    }

    // ── apps.json: cdpPort ────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_cdp_port_is_reported_on_second_entry_pointing_at_first()
    {
        var error = SingleError(apps: AppsJson(Web("netflix", "9222"), Web("prime", "9222")));

        Assert.Equal(Apps, error.File);
        Assert.Equal("$.apps[1].cdpPort", error.Path);
        Assert.Contains("$.apps[0]", error.Reason);
        Assert.Contains("9222", error.Reason);
    }

    [Theory]
    [InlineData("9300")]
    [InlineData("9219")]
    [InlineData("9240")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Cdp_port_out_of_range_is_rejected(string port)
    {
        var error = SingleError(apps: AppsJson(Web(port: port)));

        Assert.Equal("$.apps[0].cdpPort", error.Path);
        Assert.EndsWith("poza zakresem 9220–9239", error.Reason);
    }

    [Fact]
    public void Cdp_port_range_bounds_are_inclusive()
    {
        Loaded(apps: AppsJson(Web("a", "9220"), Web("b", "9239")));
    }

    [Theory]
    [InlineData("\"9222\"")]
    [InlineData("9222.5")]
    [InlineData("null")]
    public void Cdp_port_must_be_an_integer(string port)
    {
        var error = SingleError(apps: AppsJson(Web(port: port)));

        Assert.Equal("$.apps[0].cdpPort", error.Path);
        Assert.Equal("zła wartość albo typ", error.Reason);
    }

    // ── apps.json: type ───────────────────────────────────────────────────────

    [Fact]
    public void Missing_type_gives_entry_path_without_crash()
    {
        var error = SingleError(apps: $$"""{ "apps": [ {{Netflix}}, { {{NetflixFields.Replace("netflix\", \"category", "other\", \"category")}} } ] }""");

        Assert.Equal(new ConfigError(Apps, "$.apps[1]", "brak pola „type” (dozwolone: web, moonlight, native)"), error);
    }

    [Theory]
    [InlineData("\"x\"", "x")]
    [InlineData("\"Web\"", "Web")]
    [InlineData("1", "1")]
    public void Unknown_type_lists_allowed_types(string type, string shown)
    {
        var error = SingleError(apps: $$"""{ "apps": [ { "type": {{type}}, {{NetflixFields}} } ] }""");

        Assert.Equal(new ConfigError(Apps, "$.apps[0]", $"nieznany typ „{shown}” (dozwolone: web, moonlight, native)"), error);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"web\"")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("{}")]
    public void Non_object_entry_is_an_error_not_a_crash(string entry)
    {
        var error = SingleError(apps: $$"""{ "apps": [ {{entry}} ] }""");

        Assert.Equal(Apps, error.File);
        Assert.Equal("$.apps[0]", error.Path);
    }

    // ── Brak pola wymaganego, null ────────────────────────────────────────────

    [Fact]
    public void Missing_required_field_is_named()
    {
        var error = SingleError(apps: $$"""{ "apps": [ { {{NetflixFields.Replace(", \"cdpPort\": 9222", "")}}, "type": "web" } ] }""");

        Assert.Equal(new ConfigError(Apps, "$.apps[0]", "brak wymaganego pola „cdpPort”"), error);
    }

    [Fact]
    public void Several_missing_required_fields_are_listed()
    {
        var error = SingleError(apps: """{ "apps": [ { "type": "web", "id": "netflix" } ] }""");

        Assert.Equal("$.apps[0]", error.Path);
        Assert.Equal("brak wymaganych pól „service”, „browser”, „cdpPort”, „category”, „title”, „icon”", error.Reason);
    }

    [Theory]
    [InlineData(Apps, """{}""", "$", "brak wymaganego pola „apps”")]
    [InlineData(Input, """{ "devices": [] }""", "$", "brak wymaganego pola „gamepadHome”")]
    [InlineData(Input, """{ "devices": [ { "signature": "s", "source": "remote" } ], "gamepadHome": ["back", "start"] }""", "$.devices[0]", "brak wymaganego pola „keys”")]
    public void Missing_required_field_in_other_files(string file, string json, string path, string reason)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present(json);

        Assert.Equal(new ConfigError(file, path, reason), Assert.Single(ConfigValidation.Validate(files).Errors));
    }

    [Theory]
    [InlineData(Apps, """{ "apps": null }""", "$.apps")]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "id": "n", "category": "video", "title": null, "icon": "i", "service": "s", "browser": "edge", "cdpPort": 9222 } ] }""", "$.apps[0].title")]
    [InlineData(Apps, """{ "apps": [ { "type": "native", "id": "p", "category": "video", "title": "P", "icon": "i", "exe": "p.exe", "args": null } ] }""", "$.apps[0].args")]
    [InlineData(Apps, """{ "apps": [ { "type": "moonlight", "id": "m", "category": "game", "title": "M", "icon": "i", "host": "h", "app": "a", "back": null } ] }""", "$.apps[0].back")]
    [InlineData(Input, """{ "devices": [ { "signature": "s", "source": "remote", "keys": null } ], "gamepadHome": ["back", "start"] }""", "$.devices[0].keys")]
    [InlineData(SettingsJson, """{ "update": null }""", "$.update")]
    [InlineData(SettingsJson, """{ "update": { "repo": null } }""", "$.update.repo")]
    public void Null_field_is_rejected(string file, string json, string path)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present(json);

        Assert.Equal(new ConfigError(file, path, "pole nie może być null"), Assert.Single(ConfigValidation.Validate(files).Errors));
    }

    [Theory]
    [InlineData(Apps)]
    [InlineData(Input)]
    [InlineData(SettingsJson)]
    public void Null_document_is_rejected(string file)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present("null");

        Assert.Equal(new ConfigError(file, "$", "oczekiwano obiektu, a nie null"), Assert.Single(ConfigValidation.Validate(files).Errors));
    }

    [Theory]
    [InlineData(Apps, "[]")]
    [InlineData(SettingsJson, "\"x\"")]
    [InlineData(Input, "1")]
    public void Wrong_document_kind_is_wrong_value(string file, string json)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present(json);

        Assert.Equal(new ConfigError(file, "$", "zła wartość albo typ"), Assert.Single(ConfigValidation.Validate(files).Errors));
    }

    [Fact]
    public void Null_entries_in_arrays_are_rejected()
    {
        var result = Validate(
            apps: AppsJson(Netflix, "null", """{ "type": "native", "id": "plex", "category": "video", "title": "P", "icon": "i", "exe": "p.exe", "args": ["-a", null] }"""),
            input: """{ "devices": [ null ], "gamepadHome": ["back", "start"] }""");

        Assert.Null(result.Config);
        Assert.Equal(
            [
                new ConfigError(Apps, "$.apps[1]", "oczekiwano obiektu, a nie null"),
                new ConfigError(Apps, "$.apps[2].args[1]", "oczekiwano tekstu, a nie null"),
                new ConfigError(Input, "$.devices[0]", "oczekiwano obiektu, a nie null"),
            ],
            result.Errors);
    }

    // ── Zduplikowany klucz JSON ───────────────────────────────────────────────

    [Theory]
    [InlineData(Apps, """{ "apps": [], "apps": [] }""", "$.apps", "apps")]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "id": "n", "id": "m", "category": "video", "title": "T", "icon": "i", "service": "s", "browser": "edge", "cdpPort": 9222 } ] }""", "$.apps[0].id", "id")]
    [InlineData(Apps, """{ "apps": [ { "type": "web", "type": "web", "id": "n", "category": "video", "title": "T", "icon": "i", "service": "s", "browser": "edge", "cdpPort": 9222 } ] }""", "$.apps[0].type", "type")]
    [InlineData(Input, """{ "devices": [ { "signature": "s", "source": "remote", "keys": { "0x26": "up", "0x26": "down" } } ], "gamepadHome": ["back", "start"] }""", "$.devices[0].keys['0x26']", "0x26")]
    [InlineData(Input, """{ "devices": [], "devices": [], "gamepadHome": ["back", "start"] }""", "$.devices", "devices")]
    [InlineData(SettingsJson, """{ "startingTimeout": 30, "startingTimeout": 40 }""", "$.startingTimeout", "startingTimeout")]
    [InlineData(SettingsJson, """{ "update": { "repo": "a/b", "repo": "c/d" } }""", "$.update.repo", "repo")]
    public void Duplicate_json_key_is_rejected_in_every_file(string file, string json, string path, string name)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present(json);

        Assert.Equal(new ConfigError(file, path, $"zduplikowany klucz „{name}”"), Assert.Single(ConfigValidation.Validate(files).Errors));
    }

    // ── apps.json: id i back ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Netflix")]
    [InlineData("plex_htpc")]
    [InlineData("")]
    [InlineData("-plex")]
    [InlineData("plex-")]
    [InlineData("plex--htpc")]
    [InlineData("1plex")]
    [InlineData("plex htpc")]
    public void Id_must_be_kebab_case(string id)
    {
        var error = SingleError(apps: AppsJson(Web(id: id)));

        Assert.Equal("$.apps[0].id", error.Path);
        Assert.Contains("kebab-case", error.Reason);
    }

    [Theory]
    [InlineData("netflix")]
    [InlineData("plex-htpc")]
    [InlineData("moonlight-pc2")]
    public void Kebab_case_ids_are_accepted(string id)
    {
        Loaded(apps: AppsJson(Web(id: id)));
    }

    [Fact]
    public void Duplicate_id_is_reported_on_second_entry_pointing_at_first()
    {
        var error = SingleError(apps: AppsJson(Web("netflix", "9222"), Plex, Web("netflix", "9223")));

        Assert.Equal("$.apps[2].id", error.Path);
        Assert.Contains("$.apps[0]", error.Reason);
    }

    [Theory]
    [InlineData("escape")]
    [InlineData("key:")]
    [InlineData("key:Esc ape")]
    [InlineData("Home")]
    [InlineData("")]
    public void Back_must_be_home_or_key(string back)
    {
        var error = SingleError(apps: AppsJson(
            $$"""{ "type": "moonlight", "id": "pc", "category": "game", "title": "PC", "icon": "i", "host": "h", "app": "a", "back": "{{back}}" }"""));

        Assert.Equal("$.apps[0].back", error.Path);
        Assert.Contains("„home” albo „key:<nazwa>”", error.Reason);
    }

    [Fact]
    public void Native_back_is_validated_too()
    {
        var error = SingleError(apps: AppsJson(
            """{ "type": "native", "id": "plex", "category": "video", "title": "P", "icon": "i", "exe": "p.exe", "back": "esc" }"""));

        Assert.Equal("$.apps[0].back", error.Path);
    }

    [Fact]
    public void All_semantic_errors_of_a_file_are_reported_in_document_order()
    {
        var result = Validate(apps: AppsJson(Web("Netflix", "9300"), Web("prime", "9222"), Web("disney", "9222")));

        Assert.Collection(
            result.Errors,
            e => Assert.Equal("$.apps[0].id", e.Path),
            e => Assert.Equal("$.apps[0].cdpPort", e.Path),
            e => Assert.Equal("$.apps[2].cdpPort", e.Path));
    }

    // ── input.json ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Signature_must_not_be_empty(string signature)
    {
        var error = SingleError(input: InputJson(signature: signature));

        Assert.Equal(new ConfigError(Input, "$.devices[0].signature", "sygnatura nie może być pusta"), error);
    }

    [Fact]
    public void Signature_is_unique_ignoring_case()
    {
        var error = SingleError(input: """
            { "devices": [
                { "signature": "VID_1915&PID_1025", "source": "remote", "keys": {} },
                { "signature": "vid_1915&pid_1025", "source": "keyboard", "keys": {} }
              ], "gamepadHome": ["back", "start"] }
            """);

        Assert.Equal("$.devices[1].signature", error.Path);
        Assert.Contains("$.devices[0]", error.Reason);
    }

    [Fact]
    public void Wildcard_signature_with_keyboard_source_is_valid()
    {
        var config = Loaded(input: """
            { "devices": [ { "signature": "*", "source": "keyboard", "keys": { "0x71": "keyboard" } } ],
              "gamepadHome": ["back", "start"] }
            """);

        Assert.Equal(ActionSource.Keyboard, Assert.Single(config.Input.Devices).Source);
    }

    [Theory]
    [InlineData("remote")]
    [InlineData("gamepad")]
    public void Wildcard_signature_requires_keyboard_source(string source)
    {
        var error = SingleError(input: $$"""
            { "devices": [ { "signature": "*", "source": "{{source}}", "keys": {} } ],
              "gamepadHome": ["back", "start"] }
            """);

        Assert.Equal(Input, error.File);
        Assert.Equal("$.devices[0].source", error.Path);
        Assert.Contains("keyboard", error.Reason);
    }

    [Fact]
    public void Second_wildcard_is_rejected_on_its_signature_path()
    {
        var error = SingleError(input: """
            { "devices": [
                { "signature": "*", "source": "keyboard", "keys": {} },
                { "signature": "*", "source": "keyboard", "keys": {} }
              ], "gamepadHome": ["back", "start"] }
            """);

        Assert.Equal("$.devices[1].signature", error.Path);
        Assert.Contains("$.devices[0]", error.Reason);
    }

    [Theory]
    [InlineData("38")]
    [InlineData("0x1")]
    [InlineData("0xZZ")]
    [InlineData("0x100")]
    [InlineData("VK_UP")]
    [InlineData("0X26")]
    public void Key_must_be_vk_hex(string key)
    {
        var error = SingleError(input: InputJson(keys: $$"""{ "{{key}}": "up" }"""));

        Assert.Equal(Input, error.File);
        Assert.StartsWith("$.devices[0].keys", error.Path);
        Assert.Contains("0xNN", error.Reason);
    }

    [Theory]
    [InlineData("0x00")]
    [InlineData("0xFF")]
    [InlineData("0xff")]
    public void Key_must_be_in_vk_range(string key)
    {
        var error = SingleError(input: InputJson(keys: $$"""{ "{{key}}": "up" }"""));

        Assert.Equal($"$.devices[0].keys['{key}']", error.Path);
        Assert.Contains("0x01–0xFE", error.Reason);
    }

    [Fact]
    public void Same_vk_in_other_case_is_a_duplicate()
    {
        var error = SingleError(input: InputJson(keys: """{ "0x0D": "ok", "0x0d": "back" }"""));

        Assert.Equal("$.devices[0].keys['0x0d']", error.Path);
        Assert.Contains("0x0D", error.Reason);
    }

    [Fact]
    public void App_menu_is_not_allowed_as_mapping()
    {
        var error = SingleError(input: InputJson(keys: """{ "0x5D": "appMenu" }"""));

        Assert.Equal("$.devices[0].keys['0x5D']", error.Path);
        Assert.Contains("appMenu", error.Reason);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Long_press_threshold_must_be_positive(string value)
    {
        var error = SingleError(input: InputJson(extra: $$""", "timing": { "longPressMs": {{value}} }"""));

        Assert.Equal(new ConfigError(Input, "$.timing.longPressMs", "wartość musi być większa od 0"), error);
    }

    [Fact]
    public void Long_press_threshold_must_be_an_integer()
    {
        var error = SingleError(input: InputJson(extra: """, "timing": { "longPressMs": "600" }"""));

        Assert.Equal(new ConfigError(Input, "$.timing.longPressMs", "zła wartość albo typ"), error);
    }

    [Fact]
    public void Missing_or_empty_timing_defaults_to_600_ms()
    {
        Assert.Equal(600, Loaded(input: InputJson()).Input.Timing.LongPressMs);
        Assert.Equal(600, Loaded(input: InputJson(extra: """, "timing": {}""")).Input.Timing.LongPressMs);
        Assert.Equal(
            450,
            Loaded(input: InputJson(extra: """, "timing": { "longPressMs": 450 }""")).Input.Timing.LongPressMs);
    }

    [Fact]
    public void Missing_timing_defaults_repeat_to_400_and_120()
    {
        var timing = Loaded(input: InputJson()).Input.Timing;

        Assert.Equal(400, timing.RepeatDelayMs);
        Assert.Equal(120, timing.RepeatIntervalMs);

        var explicitTiming = Loaded(
            input: InputJson(extra: """, "timing": { "repeatDelayMs": 300, "repeatIntervalMs": 80 }""")).Input.Timing;

        Assert.Equal(300, explicitTiming.RepeatDelayMs);
        Assert.Equal(80, explicitTiming.RepeatIntervalMs);
        Assert.Equal(600, explicitTiming.LongPressMs);
    }

    [Theory]
    [InlineData("repeatDelayMs", "0")]
    [InlineData("repeatDelayMs", "-1")]
    [InlineData("repeatIntervalMs", "0")]
    [InlineData("repeatIntervalMs", "-1")]
    public void Repeat_thresholds_must_be_positive(string field, string value)
    {
        var error = SingleError(input: InputJson(extra: $$""", "timing": { "{{field}}": {{value}} }"""));

        Assert.Equal(new ConfigError(Input, $"$.timing.{field}", "wartość musi być większa od 0"), error);
    }

    [Theory]
    [InlineData("repeatDelayMs")]
    [InlineData("repeatIntervalMs")]
    public void Repeat_thresholds_must_be_integers(string field)
    {
        var error = SingleError(input: InputJson(extra: $$""", "timing": { "{{field}}": "400" }"""));

        Assert.Equal(new ConfigError(Input, $"$.timing.{field}", "zła wartość albo typ"), error);
    }

    [Fact]
    public void Same_vk_with_different_kind_in_two_devices_is_rejected_on_later_device()
    {
        var error = SingleError(input: """
            { "devices": [
                { "signature": "VID_1915&PID_1025", "source": "remote", "keys": { "0x0D": "ok" } },
                { "signature": "HID\\VID_046D&PID_C52B", "source": "keyboard", "keys": { "0x0D": "home" } }
              ], "gamepadHome": ["back", "start"] }
            """);

        Assert.Equal(
            new ConfigError(
                Input,
                "$.devices[1].keys['0x0D']",
                "kod VK „0x0D” ma już kind „ok” w $.devices[0].keys['0x0D']"),
            error);
    }

    [Fact]
    public void Vk_conflict_ignores_hex_case()
    {
        var error = SingleError(input: """
            { "devices": [
                { "signature": "VID_1915&PID_1025", "source": "remote", "keys": { "0x0d": "ok" } },
                { "signature": "HID\\VID_046D&PID_C52B", "source": "keyboard", "keys": { "0x0D": "home" } }
              ], "gamepadHome": ["back", "start"] }
            """);

        Assert.Equal("$.devices[1].keys['0x0D']", error.Path);
        Assert.Equal("kod VK „0x0D” ma już kind „ok” w $.devices[0].keys['0x0d']", error.Reason);
    }

    [Fact]
    public void Same_vk_with_same_kind_in_two_devices_is_valid()
    {
        var config = Loaded(input: """
            { "devices": [
                { "signature": "VID_1915&PID_1025", "source": "remote", "keys": { "0x0D": "ok" } },
                { "signature": "HID\\VID_046D&PID_C52B", "source": "keyboard", "keys": { "0x0D": "ok" } }
              ], "gamepadHome": ["back", "start"] }
            """);

        Assert.Equal(2, config.Input.Devices.Count);
        Assert.Equal(ActionKind.Ok, config.Input.Devices[1].Keys["0x0D"]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""["back"]""")]
    public void Gamepad_home_needs_two_buttons(string combo)
    {
        var error = SingleError(input: InputJson(gamepadHome: combo));

        Assert.Equal(new ConfigError(Input, "$.gamepadHome", "potrzeba co najmniej 2 różnych przycisków"), error);
    }

    [Fact]
    public void Gamepad_home_repeated_button_is_reported()
    {
        var result = Validate(input: InputJson(gamepadHome: """["back", "back"]"""));

        Assert.Collection(
            result.Errors,
            e => Assert.Equal(new ConfigError(Input, "$.gamepadHome[1]", "przycisk „back” powtarza $.gamepadHome[0]"), e),
            e => Assert.Equal(new ConfigError(Input, "$.gamepadHome", "potrzeba co najmniej 2 różnych przycisków"), e));
    }

    [Fact]
    public void Gamepad_home_accepts_three_buttons()
    {
        var config = Loaded(input: InputJson(gamepadHome: """["leftShoulder", "rightShoulder", "start"]"""));

        Assert.Equal([GamepadButton.LeftShoulder, GamepadButton.RightShoulder, GamepadButton.Start], config.Input.GamepadHome);
    }

    [Fact]
    public void Gamepad_button_of_the_home_combo_cannot_have_its_own_mapping()
    {
        var error = SingleError(input: """
            { "devices": [], "gamepadHome": ["back", "start"], "gamepad": { "start": "ok" } }
            """);

        Assert.Equal(
            new ConfigError(
                Input,
                "$.gamepad.start",
                "przycisk „start” należy do gamepadHome i nie może mieć własnego mapowania; ustaw dla niego null"),
            error);
    }

    [Fact]
    public void Gamepad_home_with_a_default_mapped_button_is_rejected()
    {
        var error = SingleError(input: InputJson(gamepadHome: """["a", "start"]"""));

        Assert.Equal(
            new ConfigError(
                Input,
                "$.gamepad.a",
                "przycisk „a” należy do gamepadHome i nie może mieć własnego mapowania; ustaw dla niego null"),
            error);
    }

    [Fact]
    public void Gamepad_mapping_rejects_app_menu()
    {
        var error = SingleError(input: """
            { "devices": [], "gamepadHome": ["back", "start"], "gamepad": { "y": "appMenu" } }
            """);

        Assert.Equal(
            new ConfigError(Input, "$.gamepad.y", "„appMenu” niedozwolone w mapowaniu (AppMenu to długie „ok” w Shell)"),
            error);
    }

    [Fact]
    public void Gamepad_buttons_outside_the_home_combo_are_mapped()
    {
        var config = Loaded(input: """
            { "devices": [], "gamepadHome": ["back", "start"], "gamepad": {
                "leftShoulder": "back", "rightShoulder": "keyboard", "x": "home" } }
            """);

        Assert.Equal(ActionKind.Back, config.Input.Gamepad.LeftShoulder);
        Assert.Equal(ActionKind.Keyboard, config.Input.Gamepad.RightShoulder);
        Assert.Equal(ActionKind.Home, config.Input.Gamepad.X);
        Assert.Null(config.Input.Gamepad.Start);
        Assert.Null(config.Input.Gamepad.Back);
    }

    [Fact]
    public void Null_gamepad_section_is_rejected()
    {
        var error = SingleError(input: """
            { "devices": [], "gamepadHome": ["back", "start"], "gamepad": null }
            """);

        Assert.Equal(new ConfigError(Input, "$.gamepad", "pole nie może być null"), error);
    }

    [Fact]
    public void Explicit_null_disables_a_default_gamepad_mapping()
    {
        var config = Loaded(input: """
            { "devices": [], "gamepadHome": ["back", "start"], "gamepad": { "a": null } }
            """);

        Assert.Null(config.Input.Gamepad.A);
        Assert.DoesNotContain(config.Input.Gamepad.Mappings(), mapping => mapping.Button == GamepadButton.A);
        Assert.Equal(ActionKind.Back, config.Input.Gamepad.B);
    }

    // ── settings.json ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ratingPendingDays", "0")]
    [InlineData("startingTimeout", "-5")]
    [InlineData("stoppingTimeout", "0")]
    [InlineData("cdpConnectTimeout", "0")]
    [InlineData("cdpReconnectTimeout", "-1")]
    public void Settings_numbers_must_be_positive(string field, string value)
    {
        var error = SingleError(settings: $$"""{ "{{field}}": {{value}} }""");

        Assert.Equal(new ConfigError(SettingsJson, $"$.{field}", "wartość musi być większa od 0"), error);
    }

    [Theory]
    [InlineData("startingTimeout")]
    [InlineData("stoppingTimeout")]
    public void Timeout_of_600_seconds_is_accepted(string field)
    {
        var config = Loaded(settings: $$"""{ "{{field}}": 600 }""");

        Assert.Equal(600, field == "startingTimeout" ? config.Settings.StartingTimeout : config.Settings.StoppingTimeout);
    }

    [Theory]
    [InlineData("startingTimeout")]
    [InlineData("stoppingTimeout")]
    public void Timeout_above_600_seconds_is_rejected(string field)
    {
        var error = SingleError(settings: $$"""{ "{{field}}": 601 }""");

        Assert.Equal(new ConfigError(SettingsJson, $"$.{field}", "wartość musi być nie większa niż 600"), error);
    }

    /// <summary>Brak dolnej relacji do 30 s limitu <c>quit</c> Moonlighta: limit żyje w hoście, nie w walidacji.</summary>
    [Fact]
    public void Stopping_timeout_needs_no_relation_to_moonlight_quit_limit()
    {
        var config = Loaded(settings: """{ "stoppingTimeout": 10 }""");

        Assert.Equal(10, config.Settings.StoppingTimeout);
        Assert.Equal(30, config.Settings.StartingTimeout);
    }

    [Theory]
    [InlineData("""{ "cdpConnectTimeout": 30 }""")]
    [InlineData("""{ "cdpConnectTimeout": 40 }""")]
    [InlineData("""{ "startingTimeout": 10 }""")]
    public void Cdp_connect_timeout_must_be_shorter_than_starting_timeout(string json)
    {
        var error = SingleError(settings: json);

        Assert.Equal("$.cdpConnectTimeout", error.Path);
        Assert.StartsWith("musi być mniejsze niż startingTimeout", error.Reason);
    }

    [Fact]
    public void Timeout_order_is_checked_only_when_both_are_positive()
    {
        var error = SingleError(settings: """{ "startingTimeout": 0, "cdpConnectTimeout": 40 }""");

        Assert.Equal(new ConfigError(SettingsJson, "$.startingTimeout", "wartość musi być większa od 0"), error);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("\"30\"")]
    [InlineData("true")]
    public void Settings_times_are_whole_seconds(string value)
    {
        var error = SingleError(settings: $$"""{ "startingTimeout": {{value}} }""");

        Assert.Equal(new ConfigError(SettingsJson, "$.startingTimeout", "zła wartość albo typ"), error);
    }

    [Theory]
    [InlineData("mediastation")]
    [InlineData("a/b/c")]
    [InlineData("/name")]
    [InlineData("owner/")]
    [InlineData("")]
    [InlineData("https://github.com/fpiechowski/mediastation")]
    public void Update_repo_must_be_owner_slash_name(string repo)
    {
        var error = SingleError(settings: $$"""{ "update": { "repo": "{{repo}}" } }""");

        Assert.Equal("$.update.repo", error.Path);
        Assert.Contains("owner/name", error.Reason);
    }

    // ── Końcowy \n nie przechodzi reguł (regexy kotwiczone \z, nie $) ─────────

    public static TheoryData<string, string, string> TrailingNewlineValues => new()
    {
        { Apps, AppsJson(Web(id: "netflix\\n")), "$.apps[0].id" },
        { Apps, AppsJson("""{ "type": "moonlight", "id": "pc", "category": "game", "title": "PC", "icon": "i", "host": "h", "app": "a", "back": "home\n" }"""), "$.apps[0].back" },
        { Input, InputJson(keys: """{ "0x26\n": "up" }"""), "$.devices[0].keys['0x26\n']" },
        { SettingsJson, """{ "update": { "repo": "a/b\n" } }""", "$.update.repo" },
    };

    [Theory]
    [MemberData(nameof(TrailingNewlineValues))]
    public void Trailing_newline_is_a_config_error_not_a_crash(string file, string json, string path)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present(json);

        var result = ConfigValidation.Validate(files);

        Assert.Null(result.Config);
        var error = Assert.Single(result.Errors);
        Assert.Equal(file, error.File);
        Assert.Equal(path, error.Path);
    }

    // ── Kolejność plików i preflight ──────────────────────────────────────────

    [Fact]
    public void Errors_in_several_files_are_ordered_apps_input_settings()
    {
        var result = Validate(
            apps: AppsJson(Web(extra: """, "foo": 1""")),
            settings: """{ "startingTimeout": 0 }""");

        Assert.Null(result.Config);
        Assert.Collection(
            result.Errors,
            e => Assert.Equal(Apps, e.File),
            e => Assert.Equal(SettingsJson, e.File));
    }

    [Fact]
    public void Preflight_errors_are_unchanged()
    {
        var files = new Dictionary<string, ConfigFileState>
        {
            [Input] = new ConfigFileState.Present("""{ "a": """),
            [SettingsJson] = new ConfigFileState.Unreadable("zablokowany"),
        };

        var result = ConfigValidation.Validate(files);

        Assert.Null(result.Config);
        Assert.Equal(ConfigPreflight.Check(files), result.Errors);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void Preflight_failure_in_one_file_does_not_hide_schema_errors_in_others()
    {
        var files = Files(settings: """{ "foo": 1 }""");
        files[Apps] = new ConfigFileState.Missing();

        var result = ConfigValidation.Validate(files);

        Assert.Equal(
            [
                new ConfigError(Apps, null, "brak pliku"),
                new ConfigError(SettingsJson, "$.foo", "nieznane pole „foo”"),
            ],
            result.Errors);
    }

    [Theory]
    [InlineData(Apps, """{ "apps": [] /* komentarz */ }""")]
    [InlineData(Input, "// komentarz\n{ \"devices\": [], \"gamepadHome\": [\"back\", \"start\"] }")]
    [InlineData(SettingsJson, """{ "a": 1, }""")]
    public void Comments_are_not_allowed(string file, string json)
    {
        var files = Files();
        files[file] = new ConfigFileState.Present(json);

        var error = Assert.Single(ConfigValidation.Validate(files).Errors);

        Assert.Equal(file, error.File);
        Assert.StartsWith("nieczytelny JSON", error.Reason);
    }

    // ── Schematy ──────────────────────────────────────────────────────────────

    [Fact]
    public void Apps_schema_is_a_closed_discriminated_union_on_type()
    {
        var schema = Schema("apps-config.schema.json");

        Assert.Equal("AppsConfig", schema["title"]!.GetValue<string>());
        Assert.Equal(["apps"], Strings(schema["required"]));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());

        var entry = schema["properties"]!["apps"]!["items"]!;
        Assert.Equal("AppEntry", entry["title"]!.GetValue<string>());
        var variants = entry["anyOf"]!.AsArray();
        Assert.Equal(["WebApp", "MoonlightApp", "NativeApp"], variants.Select(v => v!["title"]!.GetValue<string>()));
        Assert.Equal(["web", "moonlight", "native"], variants.Select(v => v!["properties"]!["type"]!["const"]!.GetValue<string>()));

        foreach (var variant in variants)
        {
            Assert.False(variant!["additionalProperties"]!.GetValue<bool>());
            var required = Strings(variant["required"]);
            Assert.Equal("type", required[0]);
            Assert.Contains("id", required);
            Assert.DoesNotContain("pointer", required);
            Assert.DoesNotContain("back", required);
            Assert.DoesNotContain("args", required);
        }

        var web = variants[0]!["properties"]!.AsObject();
        Assert.Equal(
            ["type", "service", "browser", "cdpPort", "pointer", "id", "category", "title", "icon"],
            web.Select(p => p.Key));
        Assert.Equal(["edge", "chrome"], Strings(web["browser"]!["enum"]));
        Assert.Equal(["video", "audio", "game"], Strings(web["category"]!["enum"]));
        Assert.Contains("back", variants[1]!["properties"]!.AsObject().Select(p => p.Key));
        Assert.Contains("args", variants[2]!["properties"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void Input_schema_maps_keys_to_action_kinds()
    {
        var schema = Schema("input-config.schema.json");

        Assert.Equal("InputConfig", schema["title"]!.GetValue<string>());
        Assert.Equal(["devices", "gamepadHome"], Strings(schema["required"]));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());

        var device = schema["properties"]!["devices"]!["items"]!;
        Assert.Equal("InputDevice", device["title"]!.GetValue<string>());
        Assert.Equal(["signature", "source", "keys"], Strings(device["required"]));
        Assert.False(device["additionalProperties"]!.GetValue<bool>());
        Assert.Equal("ActionKind", device["properties"]!["keys"]!["additionalProperties"]!["title"]!.GetValue<string>());

        var button = schema["properties"]!["gamepadHome"]!["items"]!;
        Assert.Equal("GamepadButton", button["title"]!.GetValue<string>());
        Assert.Contains("start", Strings(button["enum"]));

        Assert.Equal(["devices", "gamepadHome", "gamepad", "timing"], schema["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["devices", "gamepadHome"], Strings(schema["required"]));

        var gamepad = schema["properties"]!["gamepad"]!;
        Assert.Equal("InputGamepad", gamepad["title"]!.GetValue<string>());
        Assert.Null(gamepad["required"]);
        Assert.False(gamepad["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            [
                "dpadUp",
                "dpadDown",
                "dpadLeft",
                "dpadRight",
                "start",
                "back",
                "leftThumb",
                "rightThumb",
                "leftShoulder",
                "rightShoulder",
                "a",
                "b",
                "x",
                "y",
            ],
            gamepad["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal("ActionKind", gamepad["properties"]!["a"]!["anyOf"]![0]!["title"]!.GetValue<string>());
        Assert.Equal("null", gamepad["properties"]!["a"]!["anyOf"]![1]!["type"]!.GetValue<string>());

        var timing = schema["properties"]!["timing"]!;
        Assert.Equal("InputTiming", timing["title"]!.GetValue<string>());
        Assert.Null(timing["required"]);
        Assert.False(timing["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            ["longPressMs", "repeatDelayMs", "repeatIntervalMs"],
            timing["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal("integer", timing["properties"]!["longPressMs"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", timing["properties"]!["repeatDelayMs"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", timing["properties"]!["repeatIntervalMs"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Settings_schema_has_no_required_fields_and_is_closed()
    {
        var schema = Schema("settings.schema.json");

        Assert.Equal("Settings", schema["title"]!.GetValue<string>());
        Assert.Null(schema["required"]);
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            ["ratingPendingDays", "startingTimeout", "stoppingTimeout", "cdpConnectTimeout", "cdpReconnectTimeout", "update"],
            schema["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal("integer", schema["properties"]!["startingTimeout"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", schema["properties"]!["stoppingTimeout"]!["type"]!.GetValue<string>());

        var update = schema["properties"]!["update"]!;
        Assert.Equal("UpdateSettings", update["title"]!.GetValue<string>());
        Assert.Null(update["required"]);
        Assert.False(update["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void Config_options_do_not_change_contract_options()
    {
        Assert.NotSame(ContractJson.Options, ConfigJson.Options);
        Assert.True(ContractJson.Options.PropertyNameCaseInsensitive);
        Assert.Equal(JsonUnmappedMemberHandling.Skip, ContractJson.Options.UnmappedMemberHandling);
        Assert.True(ContractJson.Options.AllowDuplicateProperties);
        Assert.False(ConfigJson.Options.PropertyNameCaseInsensitive);
        Assert.Equal(JsonUnmappedMemberHandling.Disallow, ConfigJson.Options.UnmappedMemberHandling);
        Assert.False(ConfigJson.Options.AllowDuplicateProperties);
    }

    private static Dictionary<string, ConfigFileState> SampleFiles()
    {
        var dir = SampleDir();
        return new Dictionary<string, ConfigFileState>
        {
            [Apps] = new ConfigFileState.Present(File.ReadAllText(Path.Combine(dir, Apps))),
            [Input] = new ConfigFileState.Present(File.ReadAllText(Path.Combine(dir, Input))),
            [SettingsJson] = new ConfigFileState.Present(File.ReadAllText(Path.Combine(dir, SettingsJson))),
        };
    }

    private static string SampleDir() => Path.Combine(RepoRoot(), "tools", "samples", "config");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MediaStation.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Nie znaleziono MediaStation.slnx.");
    }

    private static JsonNode Schema(string file) => JsonNode.Parse(ContractJson.ExportSchemas()[file])!;

    private static string[] Strings(JsonNode? array) => array!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
}
