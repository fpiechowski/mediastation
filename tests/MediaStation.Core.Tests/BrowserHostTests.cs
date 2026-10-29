using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MediaStation.App.Hosts.Browser;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;
using OrchestratorResult = MediaStation.Core.Orchestrator.OrchestratorResult;

namespace MediaStation.Core.Tests;

/// <summary>
/// BrowserHost (story 6.2) na fake'ach procesu, okna i transportu CDP: kolejność Events, dokładne argumenty AD-1,
/// metody CDP bez <c>Runtime.enable</c>, <c>hello</c> i odrzucane komunikaty strony, binding po nawigacji głównej
/// ramki, termin CDP na <see cref="FakeTimeProvider"/>, porażki startu i Stop. Story 6.3: reconnect do tego samego
/// celu z backoffem, termin <c>cdpReconnectTimeout</c>, <see cref="CdpLost"/> i Home na prawdziwym Orchestratorze.
/// Story 6.5: adopcja żywego korzenia przez sondę (bez drugiego procesu), CDP po adopcji albo <see cref="CdpLost"/>,
/// Start w trakcie sondy, odświeżenie sesji po wybudzeniu i spóźnione fakty na prawdziwym Orchestratorze.
/// Story 6.7: J4 (restart kafelka <c>Failed</c> jednym OK) na prawdziwym Orchestratorze i prawdziwym hoście (<see cref="J4Pump"/>).
/// </summary>
public class BrowserHostTests
{
    private const string AppId = "netflix";
    private const string Exe = @"C:\Edge\msedge.exe";
    private const string StartUrl = "https://www.netflix.com/browse";
    private static readonly string ProfilesDir = @"C:\data\profiles";
    private static readonly string PageDir = @"C:\app\page";
    private static readonly string RuntimePath = Path.Combine(PageDir, "runtime.js");
    private static readonly string ManifestPath = Path.Combine(PageDir, "drivers", "netflix", "manifest.json");
    private static readonly WindowInfo Window = new(0x1234, 4242, "Chrome_WidgetWin_1", Visible: true, HasOwner: false);
    private static readonly WebApp Netflix = new(AppId, AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222);

    private const string ManifestJson =
        """{ "service": "netflix", "matches": ["https://www.netflix.com/*"], "startUrl": "https://www.netflix.com/browse", "driverVersion": "0.0.0" }""";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>Wersja Edge na UM760 w chwili story 6.4: <c>FileVersion</c> binarki i <c>product</c> z CDP.</summary>
    private const string CurrentVersion = "156.0.4314.50";

    private static readonly string ProfileDir = Path.Combine(ProfilesDir, AppId);
    private static readonly string LastVersionPath = Path.Combine(ProfileDir, "Last Version");

    /// <summary>Metody CDP pierwszego setupu w kolejności (start); reconnect wysyła tylko trzy pierwsze.</summary>
    private static readonly string[] StartSetup =
        ["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument", "Browser.getVersion", "Page.navigate"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Plik <c>runtime.js</c> harnessu (<see cref="Harness"/>) owinięty przez <see cref="BrowserLaunch.BuildInitScript"/>.</summary>
    private const string WrappedRuntime = "(function (__msPageConfig) {\n(config) => {}\n})(";

    // ── BrowserLaunch ─────────────────────────────────────────────────────────

    [Fact]
    public void Arguments_are_exactly_the_ad1_list_in_order()
    {
        var args = BrowserLaunch.BuildArguments(@"C:\d\profiles\netflix", 9222);

        Assert.Equal(
            [
                @"--user-data-dir=C:\d\profiles\netflix",
                "--remote-debugging-port=9222",
                "--no-first-run",
                "--no-default-browser-check",
                "--autoplay-policy=no-user-gesture-required",
                "--kiosk",
                "about:blank",
            ],
            args);
        Assert.DoesNotContain(args, a => a.StartsWith("--remote-allow-origins", StringComparison.Ordinal)
            || a.StartsWith("--remote-debugging-address", StringComparison.Ordinal)
            || a.StartsWith("--enable-automation", StringComparison.Ordinal));
    }

    [Fact]
    public void Init_script_wraps_the_bundle_verbatim_in_a_function_taking_the_json_config()
    {
        var script = BrowserLaunch.BuildInitScript("// banner\n(() => { go(__msPageConfig); })();\n", "__msBridge", "netflix", "0.0.0");

        Assert.Equal(
            "(function (__msPageConfig) {\n// banner\n(() => { go(__msPageConfig); })();\n\n})({\"binding\":\"__msBridge\",\"service\":\"netflix\",\"driverVersion\":\"0.0.0\"});",
            script);
        Assert.Equal("__msPageConfig", BrowserLaunch.PageConfigParameter);
    }

    [Fact]
    public void Init_script_carries_null_driver_version()
    {
        var script = BrowserLaunch.BuildInitScript("(() => {})();", "__msBridge", "netflix", null);

        Assert.EndsWith("\n})({\"binding\":\"__msBridge\",\"service\":\"netflix\",\"driverVersion\":null});", script, StringComparison.Ordinal);
    }

    // Plik poprawiony ręcznie (hotfix) może kończyć się średnikiem albo komentarzem: zostaje dosłownie, a nowa linia przed
    // zamknięciem funkcji nie pozwala komentarzowi połknąć `})(...)`.
    [Theory]
    [InlineData("(() => {})();")]
    [InlineData("(() => {})()\n// x")]
    [InlineData("(() => {})(); // x")]
    public void Init_script_keeps_a_trailing_semicolon_or_comment_verbatim(string file)
    {
        var script = BrowserLaunch.BuildInitScript(file, "__msBridge", "netflix", "1");

        Assert.Equal("(function (__msPageConfig) {\n" + file + "\n})({\"binding\":\"__msBridge\",\"service\":\"netflix\",\"driverVersion\":\"1\"});", script);
    }

    // Zgodność z nazwą w bundlu: main.ts deklaruje parametr, którego host używa w BuildInitScript.
    [Fact]
    public void Page_config_parameter_matches_the_bundle_entry()
    {
        var main = File.ReadAllText(Path.Combine(RepoRoot(), "web", "page", "runtime", "main.ts"));

        Assert.Contains($"declare const {BrowserLaunch.PageConfigParameter}: PageConfig;", main, StringComparison.Ordinal);
        Assert.Contains($"start({BrowserLaunch.PageConfigParameter}, window);", main, StringComparison.Ordinal);
    }

    // ── CdpTargets ────────────────────────────────────────────────────────────

    [Fact]
    public void Target_is_the_first_loopback_page_that_is_not_devtools()
    {
        const string json = """
            [
              { "type": "service_worker", "url": "https://x/", "webSocketDebuggerUrl": "ws://127.0.0.1:9222/devtools/page/SW" },
              { "type": "page", "url": "devtools://devtools/bundled/inspector.html", "webSocketDebuggerUrl": "ws://127.0.0.1:9222/devtools/page/DT" },
              { "type": "page", "url": "about:blank", "webSocketDebuggerUrl": "ws://127.0.0.1:9222/devtools/page/P1" },
              { "type": "page", "url": "about:blank", "webSocketDebuggerUrl": "ws://127.0.0.1:9222/devtools/page/P2" }
            ]
            """;

        Assert.Equal(new Uri("ws://127.0.0.1:9222/devtools/page/P1"), CdpTargets.PageWebSocketUrl(json, 9222));
    }

    [Theory]
    [InlineData("ws://0.0.0.0:9222/devtools/page/P")]
    [InlineData("ws://localhost:9222/devtools/page/P")]
    [InlineData("ws://192.168.1.5:9222/devtools/page/P")]
    [InlineData("ws://127.0.0.1:9223/devtools/page/P")]
    [InlineData("wss://127.0.0.1:9222/devtools/page/P")]
    [InlineData("ws://user@127.0.0.1:9222/devtools/page/P")]
    [InlineData("not a url")]
    public void Non_loopback_or_other_port_websocket_is_rejected(string url)
    {
        var json = $$"""[ { "type": "page", "url": "about:blank", "webSocketDebuggerUrl": "{{url}}" } ]""";

        Assert.Null(CdpTargets.PageWebSocketUrl(json, 9222));
    }

    [Theory]
    [InlineData("")]
    [InlineData("junk")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""[ { "type": "page", "url": "about:blank" } ]""")]
    public void Unusable_list_gives_no_target(string json)
    {
        Assert.Null(CdpTargets.PageWebSocketUrl(json, 9222));
    }

    [Fact]
    public void List_uri_is_loopback_http()
    {
        Assert.Equal(new Uri("http://127.0.0.1:9222/json/list"), CdpTargets.ListUri(9222));
    }

    // ── CdpSession ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Session_refuses_runtime_enable_before_sending_anything()
    {
        using var transport = new FakeTransport();
        using var session = new CdpSession(transport, (_, _) => { });
        session.Start();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync("Runtime.enable", cancellationToken: Ct));
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task Session_correlates_replies_by_increasing_id_and_turns_error_into_exception()
    {
        using var transport = new FakeTransport { FailMethod = "Page.enable" };
        using var session = new CdpSession(transport, (_, _) => { });
        session.Start();

        var result = await session.SendAsync("Page.navigate", new Dictionary<string, object?> { ["url"] = StartUrl }, Ct);
        var error = await Assert.ThrowsAsync<CdpException>(() => session.SendAsync("Page.enable", cancellationToken: Ct));

        Assert.Equal("F1", result.GetProperty("frameId").GetString());
        Assert.Contains("nope", error.Message, StringComparison.Ordinal);
        Assert.Equal([1L, 2L], transport.Sent.Select(m => m.GetProperty("id").GetInt64()));
    }

    [Fact]
    public async Task Session_delivers_events_and_signals_close()
    {
        using var transport = new FakeTransport();
        var events = new ConcurrentQueue<string>();
        using var session = new CdpSession(transport, (method, _) => events.Enqueue(method));
        session.Start();

        transport.Push("""{ "method": "Page.loadEventFired", "params": { "timestamp": 1 } }""");
        transport.Push("junk");
        await Eventually(() => events.Count == 1);
        transport.Close();
        await session.Closed.WaitAsync(Wait, Ct);

        Assert.Equal(["Page.loadEventFired"], events);
        await Assert.ThrowsAsync<CdpException>(() => session.SendAsync("Page.enable", cancellationToken: Ct));
    }

    // ── Page protocol i manifest (Core) ───────────────────────────────────────

    [Fact]
    public void Hello_envelope_v1_is_parsed()
    {
        var parsed = PageProtocol.Parse("""{ "v": 1, "type": "hello", "id": null, "payload": { "service": "netflix", "driverVersion": null, "webdriver": false } }""");

        Assert.Equal(new PageHello("netflix", null, false), parsed.Hello);
        Assert.Null(parsed.Reason);
    }

    [Theory]
    [InlineData("""{ "type": "hello", "id": null, "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", "envelope")]
    [InlineData("""{ "v": "1", "type": "hello", "id": null, "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", "envelope")]
    [InlineData("""{ "v": 1.5, "type": "hello", "id": null, "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", "envelope")]
    [InlineData("""{ "v": null, "type": "hello", "id": null, "payload": null }""", "envelope")]
    [InlineData("""{ "v": 1, "id": null, "payload": null }""", "envelope")]
    [InlineData("""{ "v": 1, "type": "", "id": null, "payload": null }""", "envelope")]
    [InlineData("""{ "v": 1, "type": 3, "id": null, "payload": null }""", "envelope")]
    [InlineData("""{ "v": 1, "type": "hello", "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", "envelope")]
    [InlineData("""{ "v": 1, "type": "hello", "id": "", "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", "envelope")]
    [InlineData("""{ "v": 1, "type": "hello", "id": 7, "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", "envelope")]
    [InlineData("""{ "v": 1, "type": "hello", "id": null }""", "envelope")]
    [InlineData("""{ "v": 1, "type": "screen", "id": null, "payload": {} }""", "type")]
    [InlineData("""{ "v": 1, "type": "screen", "id": "x" }""", "envelope")]
    [InlineData("""{ "v": 1, "type": "hello", "id": "7", "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", "id")]
    [InlineData("""{ "v": 1, "type": "hello", "id": "7", "payload": "x" }""", "id")]
    [InlineData("""{ "v": 1, "type": "hello", "id": null, "payload": { "service": "netflix", "driverVersion": "0" } }""", "payload")]
    [InlineData("""{ "v": 1, "type": "hello", "id": null, "payload": "x" }""", "payload")]
    [InlineData("""{ "v": 1, "type": "hello", "id": null, "payload": null }""", "payload")]
    [InlineData("junk", "unparsable")]
    [InlineData("[1]", "unparsable")]
    [InlineData("", "unparsable")]
    [InlineData("   ", "unparsable")]
    public void Bad_page_messages_are_rejected_with_a_reason(string json, string reason)
    {
        var parsed = PageProtocol.Parse(json);

        Assert.Null(parsed.Hello);
        Assert.Equal(reason, parsed.Reason);
        Assert.Null(parsed.Version);
    }

    // `version` wygrywa z każdą inną wadą koperty, bo strona innej wersji może mieć inny kształt.
    [Theory]
    [InlineData("""{ "v": 2, "type": "hello", "id": null, "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", 2)]
    [InlineData("""{ "v": 2 }""", 2)]
    [InlineData("""{ "v": 0, "type": "", "id": 5 }""", 0)]
    [InlineData("""{ "v": -3, "type": "hello", "id": null, "payload": null }""", -3)]
    public void Other_integer_major_version_is_rejected_as_version_with_the_value(string json, int version)
    {
        var parsed = PageProtocol.Parse(json);

        Assert.Null(parsed.Hello);
        Assert.Equal("version", parsed.Reason);
        Assert.Equal(version, parsed.Version);
    }

    [Fact]
    public void Page_envelope_record_carries_every_field()
    {
        using var document = JsonDocument.Parse("""{ "handled": true }""");
        var envelope = new PageEnvelope(1, "backResult", "7", document.RootElement.Clone());

        Assert.Equal(1, envelope.V);
        Assert.Equal("backResult", envelope.Type);
        Assert.Equal("7", envelope.Id);
        Assert.True(envelope.Payload.GetProperty("handled").GetBoolean());
    }

    [Fact]
    public void Manifest_is_parsed()
    {
        var parsed = DriverManifest.Parse(ManifestJson, "netflix");

        Assert.Null(parsed.Reason);
        Assert.Equal("netflix", parsed.Manifest!.Service);
        Assert.Equal(["https://www.netflix.com/*"], parsed.Manifest.Matches);
        Assert.Equal(StartUrl, parsed.Manifest.StartUrl);
        Assert.Equal("0.0.0", parsed.Manifest.DriverVersion);
    }

    [Theory]
    [InlineData("""{ "service": "prime", "matches": ["https://a.example/*"], "startUrl": "https://a.example/", "driverVersion": "1" }""", "różni się")]
    [InlineData("""{ "service": "netflix", "matches": ["https://a.example/*"], "startUrl": "http://a.example/", "driverVersion": "1" }""", "https")]
    [InlineData("""{ "service": "netflix", "matches": ["https://a.example/*"], "startUrl": "/browse", "driverVersion": "1" }""", "https")]
    [InlineData("""{ "service": "netflix", "matches": ["https://a.example/*"], "startUrl": "https://a.example/" }""", "driverVersion")]
    [InlineData("""{ "service": "netflix", "matches": ["https://a.example/*"], "startUrl": "https://a.example/", "driverVersion": "1", "x": 1 }""", "nieznane pole")]
    [InlineData("""{ "service": "netflix", "matches": [], "startUrl": "https://a.example/", "driverVersion": "1" }""", "nie może być puste")]
    [InlineData("""{ "service": "netflix", "startUrl": "https://a.example/", "driverVersion": "1" }""", "matches")]
    [InlineData("junk", "JSON")]
    public void Bad_manifest_is_rejected(string json, string reasonPart)
    {
        var parsed = DriverManifest.Parse(json, "netflix");

        Assert.Null(parsed.Manifest);
        Assert.Contains(reasonPart, parsed.Reason, StringComparison.Ordinal);
    }

    // Manifesty kopiowane do page\drivers\<service>\ muszą przejść ten sam ścisły odczyt co przy starcie.
    [Fact]
    public void Shipped_driver_manifests_parse_under_their_folder_name()
    {
        var drivers = Path.Combine(RepoRoot(), "web", "page", "drivers");
        var manifests = Directory.EnumerateFiles(drivers, "manifest.json", SearchOption.AllDirectories).ToList();

        Assert.Contains(manifests, f => Path.GetFileName(Path.GetDirectoryName(f)) == "netflix");
        foreach (var file in manifests)
        {
            var service = Path.GetFileName(Path.GetDirectoryName(file))!;
            var parsed = DriverManifest.Parse(File.ReadAllText(file), service);
            Assert.True(parsed.Manifest is not null, $"{file}: {parsed.Reason}");
        }
    }

    // ── BrowserHost ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Happy_path_launches_with_exact_args_attaches_then_completes_after_cdp_setup()
    {
        using var h = new Harness();

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal(
            [new AppWindowAttached(AppId, 0x1234, 4242), new CommandCompleted(7)],
            h.Posted);
        var (exe, args) = Assert.Single(h.Launcher.Starts);
        Assert.Equal(Exe, exe);
        Assert.Equal(BrowserLaunch.BuildArguments(Path.Combine(ProfilesDir, AppId), 9222), args);
        Assert.Equal(StartSetup, h.Transport.Methods());
        Assert.Equal("__msBridge", h.Transport.Sent[0].GetProperty("params").GetProperty("name").GetString());
        var source = h.Transport.Sent[2].GetProperty("params").GetProperty("source").GetString()!;
        Assert.StartsWith(WrappedRuntime, source, StringComparison.Ordinal);
        Assert.Contains("\"binding\":\"__msBridge\",\"service\":\"netflix\",\"driverVersion\":\"0.0.0\"", source, StringComparison.Ordinal);
        Assert.Equal(StartUrl, h.Transport.Sent[4].GetProperty("params").GetProperty("url").GetString());
        Assert.Equal(9222, h.ConnectedPort);

        var launch = h.Log.Entries.Single(e => e.Msg == "appLaunch");
        Assert.Equal("browser", launch.Src);
        Assert.Contains("--kiosk", (IEnumerable<string>)launch.Data!["args"]!);
        Assert.Contains(h.Log.Entries, e => e.Msg == "appWindowAttached" && Equals(e.Data!["rootPid"], 4242));
        Assert.Contains(h.Log.Entries, e => e.Msg == "cdpConnected");
        Assert.DoesNotContain(h.Log.Entries, e => e.Level is LogLevel.Warn or LogLevel.Error);
    }

    [Fact]
    public async Task Hello_from_the_page_is_logged_with_webdriver()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.PushBinding("__msBridge", """{"v":1,"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0.0.0","webdriver":false}}""");
        var hello = await h.WaitLog("pageHello");

        Assert.Equal(LogLevel.Info, hello.Level);
        Assert.Equal("browser", hello.Src);
        Assert.Equal(AppId, hello.Data!["appId"]);
        Assert.Equal("netflix", hello.Data["service"]);
        Assert.Equal("0.0.0", hello.Data["driverVersion"]);
        Assert.False((bool)hello.Data["webdriver"]!);
    }

    [Theory]
    [InlineData("__msBridge", """{"v":1,"type":"playback","id":null,"payload":{}}""", "type")]
    [InlineData("__msBridge", """{"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0","webdriver":false}}""", "envelope")]
    [InlineData("__msBridge", """{"v":1,"type":"hello","id":"7","payload":{"service":"netflix","driverVersion":"0","webdriver":false}}""", "id")]
    [InlineData("__msBridge", "junk", "unparsable")]
    [InlineData("__other", """{"v":1,"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0","webdriver":false}}""", "binding")]
    public async Task Bad_page_message_is_logged_as_rejected_and_the_session_continues(string binding, string payload, string reason)
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.PushBinding(binding, payload);
        var rejected = await h.WaitLog("pageMessageRejected");
        h.Transport.PushBinding("__msBridge", """{"v":1,"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0","webdriver":false}}""");
        await h.WaitLog("pageHello");

        Assert.Equal(LogLevel.Warn, rejected.Level);
        Assert.Equal(AppId, rejected.Data!["appId"]);
        Assert.Equal(reason, rejected.Data["reason"]);
        Assert.Equal(2, h.Posted.Count);
    }

    private const string HelloV2 = """{"v":2,"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0","webdriver":false}}""";

    // AD-7, DW-54: każda koperta z innym major `v` to wpis w logu, a fakt dla Orchestratora idzie raz na sesję.
    [Fact]
    public async Task Other_major_version_twice_logs_twice_and_posts_page_version_rejected_once()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.PushBinding("__msBridge", HelloV2);
        h.Transport.PushBinding("__msBridge", """{"v":2,"type":"screen","id":null,"payload":null}""");
        await Eventually(() => h.Log.Entries.Count(e => e.Msg == "pageMessageRejected") == 2);
        h.Transport.PushBinding("__msBridge", """{"v":1,"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0","webdriver":false}}""");
        await h.WaitLog("pageHello");

        var rejected = h.Log.Entries.Where(e => e.Msg == "pageMessageRejected").ToArray();
        Assert.All(rejected, entry =>
        {
            Assert.Equal(LogLevel.Warn, entry.Level);
            Assert.Equal("browser", entry.Src);
            Assert.Equal(AppId, entry.Data!["appId"]);
            Assert.Equal("version", entry.Data["reason"]);
            Assert.Equal(2, entry.Data["v"]);
        });
        Assert.Equal(
            [new AppWindowAttached(AppId, 0x1234, 4242), new CommandCompleted(7), new PageVersionRejected(AppId, 2)],
            h.Posted);
    }

    [Fact]
    public async Task Other_major_version_after_stop_posts_nothing()
    {
        using var h = new Harness();
        h.Transport.DeliverAfterDispose = true;
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);
        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(4);

        var delivered = h.Transport.Delivered;
        h.Transport.PushBinding("__msBridge", HelloV2);
        await Eventually(() => h.Transport.Delivered > delivered);
        await Task.Delay(200, Ct);

        Assert.DoesNotContain(h.Posted, message => message is PageVersionRejected);
        Assert.Equal(4, h.Posted.Count);
        h.Transport.Close();
    }

    [Fact]
    public async Task Other_major_version_from_a_replaced_session_posts_nothing_and_the_new_session_still_posts_once()
    {
        var transports = new ConcurrentQueue<FakeTransport>();
        using var h = new Harness(connect: (_, _) =>
        {
            var transport = new FakeTransport { DeliverAfterDispose = true };
            transports.Enqueue(transport);
            return Task.FromResult<ICdpTransport?>(transport);
        });
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);
        h.Host.Start(new StartApp(8, AppId));
        await h.WaitPosted(4);
        var (stale, current) = (transports.First(), transports.Last());

        var delivered = stale.Delivered;
        stale.PushBinding("__msBridge", HelloV2);
        await Eventually(() => stale.Delivered > delivered);
        await Task.Delay(200, Ct);
        Assert.Equal(4, h.Posted.Count);

        current.PushBinding("__msBridge", HelloV2);
        await h.WaitPosted(5);
        Assert.Equal(new PageVersionRejected(AppId, 2), h.Posted[4]);
        stale.Close();
    }

    [Fact]
    public async Task Page_version_rejected_while_foreground_fails_the_tile_and_home_still_shows_the_shell()
    {
        var (orchestrator, _) = OrchestratorTestSupport.Started();
        OrchestratorTestSupport.Foreground(orchestrator, AppId);
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.PushBinding("__msBridge", HelloV2);
        await h.WaitPosted(3);
        var failed = orchestrator.Handle(Assert.IsType<PageVersionRejected>(h.Posted[2]));

        Assert.Equal(AppLifecycleState.Failed, OrchestratorTestSupport.StateOf(failed.Snapshot!, AppId));
        Assert.Contains(failed.Commands, command => command is ShowShell);
        var home = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1));
        Assert.Contains(home.Commands, command => command is ShowShell);
        Assert.Equal(AppLifecycleState.Failed, OrchestratorTestSupport.StateOf(home.Snapshot!, AppId));
    }

    [Fact]
    public async Task Binding_is_re_added_only_after_main_frame_navigation()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"child","parentId":"main","url":"https://x/"},"type":"Navigation"}}""");
        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://www.netflix.com/browse"},"type":"Navigation"}}""");
        await Eventually(() => h.Transport.Methods().Length == 6);
        await Task.Delay(100, Ct);

        Assert.Equal([.. StartSetup, "Runtime.addBinding"], h.Transport.Methods());
        Assert.DoesNotContain("Runtime.enable", h.Transport.Methods());
    }

    [Fact]
    public async Task Failed_binding_refresh_is_only_a_warning()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.FailMethod = "Runtime.addBinding";
        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://x/"}}}""");
        var warn = await h.WaitLog("bindingRefreshFailed");

        Assert.Equal(LogLevel.Warn, warn.Level);
        Assert.Equal(2, h.Posted.Count);
    }

    [Fact]
    public async Task Cdp_that_never_answers_fails_with_cdp_unavailable_after_the_deadline_and_keeps_the_process()
    {
        using var h = new Harness(connect: async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);
        h.Time.Advance(TimeSpan.FromSeconds(14));
        await Task.Delay(100, Ct);
        Assert.Single(h.Posted);

        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.WaitPosted(2);

        // Rozróżnienie AD-13: okno najpierw, potem CdpUnavailable; bez ProcessExited i bez zabicia procesu.
        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandFailed(7, ErrorCode.CdpUnavailable)], h.Posted);
        var warn = h.Log.Entries.Single(e => e.Msg == "cdpUnavailable");
        Assert.Equal(LogLevel.Warn, warn.Level);
        Assert.Equal(9222, warn.Data!["cdpPort"]);
        Assert.Equal("notConnected", warn.Data["reason"]);
        Assert.Equal(CurrentVersion, warn.Data["browserVersion"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "processExited" or "cdpAbandoned" or "browserStartFailed");
        Assert.Equal(0, h.Process.KillCalls);
        Assert.False(h.Process.HasExited);
    }

    [Fact]
    public async Task Connector_returning_no_transport_is_cdp_unavailable()
    {
        using var h = new Harness(connect: (_, _) => Task.FromResult<ICdpTransport?>(null));

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal(new CommandFailed(7, ErrorCode.CdpUnavailable), h.Posted[1]);
    }

    [Fact]
    public async Task Missing_browser_fails_without_launching()
    {
        using var h = new Harness(resolve: _ => null);

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);

        Assert.Equal([new CommandFailed(7, ErrorCode.Unexpected)], h.Posted);
        Assert.Empty(h.Launcher.Starts);
        Assert.Equal("browserNotFound", StartFailure(h).Data!["reason"]);
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("manifest")]
    public async Task Missing_page_asset_fails_naming_the_file_without_launching(string missing)
    {
        var file = missing == "runtime" ? RuntimePath : ManifestPath;
        using var h = new Harness();
        h.Files.TryRemove(file, out _);

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);

        Assert.Equal([new CommandFailed(7, ErrorCode.Unexpected)], h.Posted);
        Assert.Empty(h.Launcher.Starts);
        var failure = StartFailure(h);
        Assert.Equal("pageAssetMissing", failure.Data!["reason"]);
        Assert.Equal(file, failure.Data["file"]);
    }

    [Fact]
    public async Task Invalid_manifest_fails_naming_the_file_without_launching()
    {
        using var h = new Harness();
        h.Files[ManifestPath] = """{ "service": "prime", "matches": ["https://a.example/*"], "startUrl": "https://a.example/", "driverVersion": "1" }""";

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);

        Assert.Empty(h.Launcher.Starts);
        var failure = StartFailure(h);
        Assert.Equal("pageAssetInvalid", failure.Data!["reason"]);
        Assert.Equal(ManifestPath, failure.Data["file"]);
    }

    [Fact]
    public async Task Launcher_exit_before_a_window_fails_and_kills_the_tree()
    {
        using var h = new Harness(findWindow: _ => null);
        h.Process.Exit(5);

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);

        Assert.Equal([new CommandFailed(7, ErrorCode.Unexpected)], h.Posted);
        var failure = StartFailure(h);
        Assert.Equal("launcherExited", failure.Data!["reason"]);
        Assert.Equal(5, failure.Data["exitCode"]);
        Assert.Equal(1, h.Process.KillCalls);
        Assert.Equal(0, h.ConnectCalls);
    }

    [Fact]
    public async Task No_window_within_20_seconds_fails_and_kills_the_tree()
    {
        var polls = 0;
        using var h = new Harness(findWindow: _ =>
        {
            Interlocked.Increment(ref polls);
            return null;
        });

        h.Host.Start(new StartApp(7, AppId));
        await Eventually(() => Volatile.Read(ref polls) >= 1);
        h.Time.Advance(TimeSpan.FromMilliseconds(19_900));
        await Task.Delay(100, Ct);
        Assert.Empty(h.Posted);

        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.WaitPosted(1);

        // Rozróżnienie AD-13: bez okna nie ma AppWindowAttached ani CdpUnavailable, tylko Unexpected z windowTimeout.
        Assert.Equal([new CommandFailed(7, ErrorCode.Unexpected)], h.Posted);
        Assert.Equal("windowTimeout", StartFailure(h).Data!["reason"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "appWindowAttached" or "cdpUnavailable" or "processExited");
        Assert.Equal(1, h.Process.KillCalls);
        Assert.Equal(TimeSpan.FromSeconds(20), h.Time.GetUtcNow() - Harness.T0);
    }

    [Fact]
    public async Task Stop_closes_cdp_window_and_tree_then_reports_exit_and_completion()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(4);
        await Task.Delay(100, Ct);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted.Skip(2));
        Assert.True(h.Transport.Disposed);
        Assert.Equal(1, h.Process.CloseCalls);
        Assert.Equal(1, h.Process.KillCalls);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "processExited" or "cdpClosed");
        Assert.Equal(4, h.Posted.Count);
    }

    [Fact]
    public async Task Stop_without_a_session_still_reports_exit_and_completion()
    {
        using var h = new Harness();

        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(2);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted);
    }

    [Fact]
    public async Task Process_exit_not_caused_by_stop_reports_process_exited()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Process.Exit(0);
        await h.WaitPosted(3);

        Assert.Equal(new ProcessExited(AppId), h.Posted[2]);
        Assert.Contains(h.Log.Entries, e => e.Msg == "processExited");
    }

    [Fact]
    public async Task Closed_cdp_socket_logs_cdp_closed_with_the_reconnect_timeout_and_posts_nothing_yet()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.Close();
        var closed = await h.WaitLog("cdpClosed");

        Assert.Equal(LogLevel.Info, closed.Level);
        Assert.Equal(AppId, closed.Data!["appId"]);
        Assert.Equal(30d, closed.Data["timeoutSeconds"]);
        Assert.True(h.Transport.Disposed);
        Assert.Equal(2, h.Posted.Count);
    }

    [Fact]
    public async Task Probe_and_renew_without_any_process_are_silent()
    {
        using var h = new Harness();

        h.Host.Probe(new ProbeExisting(1, AppId));
        h.Host.Renew(new RenewSession(2, AppId));
        var probe = await h.WaitLog("probeExisting");
        var renew = await h.WaitLog("renewSession");

        Assert.Equal("notRunning", probe.Data!["outcome"]);
        Assert.Equal("noSession", renew.Data!["outcome"]);
        Assert.Empty(h.Posted);
        Assert.Empty(h.Launcher.Starts);
        Assert.Equal(0, h.ConnectCalls);
    }

    [Fact]
    public async Task Process_exit_while_connecting_reports_exit_first_then_abandons_cdp_without_warning()
    {
        using var h = new Harness(connect: async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);

        h.Process.Exit(0);
        await h.WaitPosted(3);

        // Rozróżnienie AD-13: wyjście procesu w trakcie czekania na CDP to ProcessExited, a nie CdpUnavailable.
        Assert.Equal(
            [new AppWindowAttached(AppId, 0x1234, 4242), new ProcessExited(AppId), new CommandFailed(7, ErrorCode.Unexpected)],
            h.Posted);
        Assert.Equal(LogLevel.Info, (await h.WaitLog("cdpAbandoned")).Level);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpUnavailable");
    }

    [Fact]
    public async Task Error_reply_during_setup_is_cdp_unavailable_with_setup_failed()
    {
        using var h = new Harness();
        h.Transport.FailMethod = "Page.enable";

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        AssertSetupFailed(h);
        Assert.Equal(["Runtime.addBinding", "Page.enable"], h.Transport.Methods());
    }

    [Fact]
    public async Task Send_on_a_closed_socket_during_setup_is_cdp_unavailable_with_setup_failed()
    {
        using var h = new Harness();
        h.Transport.SendThrows = new ObjectDisposedException("ClientWebSocket");

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        AssertSetupFailed(h);
        Assert.DoesNotContain(h.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Second_start_closes_the_previous_session_silently_and_reports_its_own_window()
    {
        var transports = new ConcurrentQueue<FakeTransport>();
        using var h = new Harness(connect: (_, _) =>
        {
            var transport = new FakeTransport();
            transports.Enqueue(transport);
            return Task.FromResult<ICdpTransport?>(transport);
        });
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Host.Start(new StartApp(8, AppId));
        await h.WaitPosted(4);
        await Task.Delay(100, Ct);

        Assert.Equal(
            [
                new AppWindowAttached(AppId, 0x1234, 4242),
                new CommandCompleted(7),
                new AppWindowAttached(AppId, 0x1235, 4243),
                new CommandCompleted(8),
            ],
            h.Posted);
        Assert.Equal(2, h.Launcher.Starts.Count);
        Assert.Equal(1, h.Process.CloseCalls);
        Assert.Equal(1, h.Process.KillCalls);
        Assert.True(transports.First().Disposed);
        Assert.Equal(0, h.Launcher.Processes[1].KillCalls);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "processExited");
    }

    [Fact]
    public async Task Stop_during_window_wait_fails_the_start_and_kills_the_tree()
    {
        using var h = new Harness(findWindow: _ => null);
        h.Host.Start(new StartApp(7, AppId));
        await Eventually(() => h.Launcher.Starts.Count == 1);

        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(3);
        await Task.Delay(100, Ct);

        AssertStoppedDuringStart(h, expectedPosted: 3);
        Assert.Equal(0, h.ConnectCalls);
    }

    [Fact]
    public async Task Stop_during_cdp_connect_fails_the_start_without_cdp_unavailable()
    {
        using var h = new Harness(connect: async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);

        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(4);
        await Task.Delay(100, Ct);

        Assert.Equal(new AppWindowAttached(AppId, 0x1234, 4242), h.Posted[0]);
        AssertStoppedDuringStart(h, expectedPosted: 4);
    }

    [Fact]
    public async Task Connector_on_an_unused_loopback_port_returns_null_when_cancelled()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(1));
        var started = DateTime.UtcNow;

        var transport = await CdpConnector.ConnectAsync(port, cts.Token);

        Assert.Null(transport);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task Navigate_error_text_is_a_warning_with_the_safe_url_and_the_start_still_completes()
    {
        using var h = new Harness();
        h.Files[ManifestPath] = """{ "service": "netflix", "matches": ["https://www.netflix.com/*"], "startUrl": "https://www.netflix.com/browse?token=secret#x", "driverVersion": "0.0.0" }""";
        h.Transport.NavigateResult = """{"frameId":"F1","errorText":"net::ERR_NAME_NOT_RESOLVED"}""";

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal(new CommandCompleted(7), h.Posted[1]);
        var warn = h.Log.Entries.Single(e => e.Msg == "pageNavigateFailed");
        Assert.Equal(LogLevel.Warn, warn.Level);
        Assert.Equal(StartUrl, warn.Data!["url"]);
        Assert.Equal("net::ERR_NAME_NOT_RESOLVED", warn.Data["errorText"]);
    }

    [Fact]
    public async Task Launcher_returning_no_process_fails_with_process_start_failed()
    {
        using var h = new Harness();
        h.Launcher.ReturnNull = true;

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);

        Assert.Equal([new CommandFailed(7, ErrorCode.Unexpected)], h.Posted);
        Assert.Equal("processStartFailed", StartFailure(h).Data!["reason"]);
        Assert.Equal(0, h.ConnectCalls);
    }

    [Fact]
    public async Task Unknown_app_fails_without_launching()
    {
        using var h = new Harness();

        h.Host.Start(new StartApp(7, "prime"));
        await h.WaitPosted(1);

        Assert.Equal([new CommandFailed(7, ErrorCode.Unexpected)], h.Posted);
        Assert.Empty(h.Launcher.Starts);
        Assert.Equal("unknownApp", StartFailure(h).Data!["reason"]);
    }

    [Fact]
    public async Task Blank_runtime_is_invalid_and_fails_naming_the_file_without_launching()
    {
        using var h = new Harness();
        h.Files[RuntimePath] = " \n\t";

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);

        Assert.Empty(h.Launcher.Starts);
        var failure = StartFailure(h);
        Assert.Equal("pageAssetInvalid", failure.Data!["reason"]);
        Assert.Equal(RuntimePath, failure.Data["file"]);
    }

    [Fact]
    public async Task Slow_navigation_reply_does_not_count_against_the_cdp_deadline()
    {
        using var h = new Harness();
        h.Transport.HoldMethod = "Page.navigate";

        h.Host.Start(new StartApp(7, AppId));
        await Eventually(() => h.Transport.Methods().Contains("Page.navigate"));
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(100, Ct);
        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242)], h.Posted);

        h.Transport.ReleaseHeld();
        await h.WaitPosted(2);

        Assert.Equal(new CommandCompleted(7), h.Posted[1]);
        Assert.Contains(h.Log.Entries, e => e.Msg == "cdpConnected");
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpUnavailable");
    }

    [Fact]
    public async Task Setup_reply_missing_at_the_deadline_is_cdp_unavailable_with_setup_failed()
    {
        using var h = new Harness();
        h.Transport.HoldMethod = "Page.enable";

        h.Host.Start(new StartApp(7, AppId));
        await Eventually(() => h.Transport.Methods().Contains("Page.enable"));
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await h.WaitPosted(2);

        AssertSetupFailed(h);
        Assert.Equal(["Runtime.addBinding", "Page.enable"], h.Transport.Methods());
    }

    [Fact]
    public async Task Stop_while_a_setup_reply_is_outstanding_fails_the_start_as_stopped()
    {
        using var h = new Harness();
        h.Transport.HoldMethod = "Page.enable";
        h.Host.Start(new StartApp(7, AppId));
        await Eventually(() => h.Transport.Methods().Contains("Page.enable"));

        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(4);
        await Task.Delay(100, Ct);

        Assert.Equal(new AppWindowAttached(AppId, 0x1234, 4242), h.Posted[0]);
        AssertStoppedDuringStart(h, expectedPosted: 4);
        Assert.True(h.Transport.Disposed);
    }

    [Fact]
    public async Task Process_exit_during_cdp_setup_reports_exit_first_then_abandons_cdp_without_warning()
    {
        using var h = new Harness();
        h.Transport.HoldMethod = "Page.enable";
        h.Host.Start(new StartApp(7, AppId));
        await Eventually(() => h.Transport.Methods().Contains("Page.enable"));

        h.Process.Exit(0);
        await h.WaitPosted(3);

        Assert.Equal(
            [new AppWindowAttached(AppId, 0x1234, 4242), new ProcessExited(AppId), new CommandFailed(7, ErrorCode.Unexpected)],
            h.Posted);
        Assert.Equal(LogLevel.Info, (await h.WaitLog("cdpAbandoned")).Level);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpUnavailable");
        Assert.True(h.Transport.Disposed);
    }

    [Fact]
    public async Task Cdp_deadline_counts_from_launch_including_the_window_search()
    {
        var windowUp = false;
        var polls = 0;
        using var h = new Harness(
            findWindow: _ =>
            {
                Interlocked.Increment(ref polls);
                return Volatile.Read(ref windowUp) ? Window : null;
            },
            connect: async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return null;
            });

        h.Host.Start(new StartApp(7, AppId));
        await Eventually(() => Volatile.Read(ref polls) >= 1);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        Volatile.Write(ref windowUp, true);
        for (var i = 0; i < 40 && h.Posted.Count == 0; i++)
        {
            await Task.Delay(5, Ct);
            h.Time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242)], h.Posted);
        h.Time.Advance(Harness.T0 + TimeSpan.FromMilliseconds(14_900) - h.Time.GetUtcNow());
        await Task.Delay(100, Ct);
        Assert.Single(h.Posted);

        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.WaitPosted(2);

        Assert.Equal(new CommandFailed(7, ErrorCode.CdpUnavailable), h.Posted[1]);
    }

    [Fact]
    public async Task Stop_kills_the_tree_when_the_window_ignores_close_for_3_seconds()
    {
        using var h = new Harness();
        h.Process.IgnoreClose = true;
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Host.Stop(new StopApp(8, AppId));
        await Eventually(() => h.Process.TimedWaits == 1);
        h.Time.Advance(TimeSpan.FromMilliseconds(2_900));
        await Task.Delay(100, Ct);
        Assert.Equal(2, h.Posted.Count);
        Assert.Equal(0, h.Process.KillCalls);

        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.WaitPosted(4);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted.Skip(2));
        Assert.Equal(1, h.Process.CloseCalls);
        Assert.Equal(1, h.Process.KillCalls);
        Assert.True(h.Process.HasExited);
    }

    // ── Story 6.4: wersja przeglądarki i rozróżnialne błędy startu ────────────

    [Fact]
    public async Task Changed_version_is_logged_before_app_launch_and_confirmed_by_get_version()
    {
        using var h = new Harness();
        h.Files[LastVersionPath] = "155.0.4283.45";

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandCompleted(7)], h.Posted);
        Assert.Equal([Exe], h.VersionReads);
        var messages = h.Log.Messages();
        Assert.True(Array.IndexOf(messages, "browserVersion") < Array.IndexOf(messages, "browserVersionChanged"));
        Assert.True(Array.IndexOf(messages, "browserVersionChanged") < Array.IndexOf(messages, "appLaunch"));

        var version = h.Log.Entries.Single(e => e.Msg == "browserVersion");
        Assert.Equal(LogLevel.Info, version.Level);
        Assert.Equal("browser", version.Src);
        Assert.Equal(AppId, version.Data!["appId"]);
        Assert.Equal(CurrentVersion, version.Data["version"]);
        Assert.Equal("155.0.4283.45", version.Data["previous"]);

        var changed = h.Log.Entries.Single(e => e.Msg == "browserVersionChanged");
        Assert.Equal(LogLevel.Info, changed.Level);
        Assert.Equal(AppId, changed.Data!["appId"]);
        Assert.Equal("155.0.4283.45", changed.Data["from"]);
        Assert.Equal(CurrentVersion, changed.Data["to"]);

        Assert.Equal(CurrentVersion, h.Log.Entries.Single(e => e.Msg == "cdpConnected").Data!["browserVersion"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Level is LogLevel.Warn or LogLevel.Error);
    }

    [Theory]
    [InlineData("156.0.4314.50", "156.0.4314.50")]
    [InlineData("156.0.4314.050", "156.0.4314.50")]
    [InlineData("156.0.4314", "156.0.4314.0")]
    [InlineData("156.0.4314.0", "156.0.4314")]
    [InlineData("156.0.4314.50\r\n", "156.0.4314.50")]
    public async Task Same_version_logs_only_browser_version_before_app_launch(string lastVersion, string exeVersion)
    {
        using var h = new Harness();
        h.Files[LastVersionPath] = lastVersion;
        h.ExeVersion = exeVersion;

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal(new CommandCompleted(7), h.Posted[1]);
        var messages = h.Log.Messages();
        Assert.True(Array.IndexOf(messages, "browserVersion") < Array.IndexOf(messages, "appLaunch"));
        var version = h.Log.Entries.Single(e => e.Msg == "browserVersion");
        Assert.Equal(version.Data!["previous"], version.Data["version"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "browserVersionChanged");
    }

    [Theory]
    [InlineData(null, CurrentVersion, CurrentVersion, "unknown")]
    [InlineData("", CurrentVersion, CurrentVersion, "unknown")]
    [InlineData("  \r\n", CurrentVersion, CurrentVersion, "unknown")]
    [InlineData("garbage", CurrentVersion, CurrentVersion, "unknown")]
    [InlineData("155.0.4283.45", null, "unknown", "155.0.4283.45")]
    [InlineData(null, null, "unknown", "unknown")]
    public async Task Unknown_version_is_logged_as_unknown_without_a_change_entry_and_the_start_continues(
        string? lastVersion, string? exeVersion, string expectedVersion, string expectedPrevious)
    {
        using var h = new Harness();
        if (lastVersion is not null)
        {
            h.Files[LastVersionPath] = lastVersion;
        }

        h.ExeVersion = exeVersion;

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandCompleted(7)], h.Posted);
        var version = h.Log.Entries.Single(e => e.Msg == "browserVersion");
        Assert.Equal(expectedVersion, version.Data!["version"]);
        Assert.Equal(expectedPrevious, version.Data["previous"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "browserVersionChanged" or "browserVersionMismatch");

        // Nieznana wersja binarki nie blokuje potwierdzenia: cdpConnected niesie wersję z product.
        Assert.Equal(CurrentVersion, h.Log.Entries.Single(e => e.Msg == "cdpConnected").Data!["browserVersion"]);
    }

    [Fact]
    public async Task Cdp_unavailable_carries_unknown_version_when_the_exe_version_is_unreadable()
    {
        using var h = new Harness(connect: async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        h.ExeVersion = null;

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await h.WaitPosted(2);

        Assert.Equal(new CommandFailed(7, ErrorCode.CdpUnavailable), h.Posted[1]);
        Assert.Equal("unknown", h.Log.Entries.Single(e => e.Msg == "cdpUnavailable").Data!["browserVersion"]);
    }

    [Fact]
    public async Task Version_change_is_logged_before_launch_even_when_cdp_then_never_answers()
    {
        using var h = new Harness(connect: async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        h.Files[LastVersionPath] = "155.0.4283.45";

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await h.WaitPosted(2);

        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandFailed(7, ErrorCode.CdpUnavailable)], h.Posted);
        var messages = h.Log.Messages();
        Assert.True(Array.IndexOf(messages, "browserVersionChanged") < Array.IndexOf(messages, "appLaunch"));
        var changed = h.Log.Entries.Single(e => e.Msg == "browserVersionChanged");
        Assert.Equal("155.0.4283.45", changed.Data!["from"]);
        Assert.Equal(CurrentVersion, changed.Data["to"]);
        Assert.Equal(CurrentVersion, h.Log.Entries.Single(e => e.Msg == "cdpUnavailable").Data!["browserVersion"]);
    }

    [Theory]
    [InlineData(true, false, ErrorCode.ProfileInUse)]
    [InlineData(false, true, ErrorCode.PortInUse)]
    [InlineData(true, true, ErrorCode.ProfileInUse)]
    public async Task Held_profile_or_port_fails_without_launching_and_a_later_start_works(bool profileHeld, bool portHeld, ErrorCode expected)
    {
        using var h = new Harness();
        h.ProfileHeld = profileHeld;
        h.PortHeld = portHeld;

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);
        await Task.Delay(100, Ct);

        Assert.Equal([new CommandFailed(7, expected)], h.Posted);
        Assert.Empty(h.Launcher.Starts);
        Assert.Equal(0, h.ConnectCalls);
        Assert.Empty(h.VersionReads);
        var failure = StartFailure(h);
        if (expected == ErrorCode.ProfileInUse)
        {
            Assert.Equal("profileInUse", failure.Data!["reason"]);
            Assert.Equal(ProfileDir, failure.Data["profileDir"]);
            Assert.Empty(h.PortChecks);
        }
        else
        {
            Assert.Equal("portInUse", failure.Data!["reason"]);
            Assert.Equal(9222, failure.Data["cdpPort"]);
            Assert.Equal([9222], h.PortChecks);
        }

        Assert.Equal(ProfileDir, Assert.Single(h.ProfileChecks).Dir);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "appLaunch" or "browserVersion");

        // Sesja jest wyrejestrowana: kolejny start po zwolnieniu zasobów działa normalnie.
        h.ProfileHeld = false;
        h.PortHeld = false;
        h.Host.Start(new StartApp(8, AppId));
        await h.WaitPosted(3);

        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandCompleted(8)], h.Posted.Skip(1));
        Assert.Single(h.Launcher.Starts);
        Assert.Equal(0, h.Process.CloseCalls);
    }

    [Fact]
    public async Task Get_version_mismatch_is_a_warning_and_the_start_completes()
    {
        using var h = new Harness();
        h.Transport.VersionResult = """{"product":"Edg/157.0.4400.10"}""";

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandCompleted(7)], h.Posted);
        var mismatch = h.Log.Entries.Single(e => e.Msg == "browserVersionMismatch");
        Assert.Equal(LogLevel.Warn, mismatch.Level);
        Assert.Equal(AppId, mismatch.Data!["appId"]);
        Assert.Equal(CurrentVersion, mismatch.Data["expected"]);
        Assert.Equal("157.0.4400.10", mismatch.Data["actual"]);
        Assert.Equal("157.0.4400.10", h.Log.Entries.Single(e => e.Msg == "cdpConnected").Data!["browserVersion"]);
        Assert.Equal(StartSetup, h.Transport.Methods());
    }

    [Fact]
    public async Task Chrome_product_is_confirmed_like_edge()
    {
        using var h = new Harness();
        h.Transport.VersionResult = "{\"product\":\"Chrome/" + CurrentVersion + "\"}";

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal(CurrentVersion, h.Log.Entries.Single(e => e.Msg == "cdpConnected").Data!["browserVersion"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Level is LogLevel.Warn or LogLevel.Error);
    }

    [Fact]
    public async Task Get_version_error_reply_is_unconfirmed_and_the_start_completes()
    {
        using var h = new Harness();
        h.Transport.FailMethod = "Browser.getVersion";

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandCompleted(7)], h.Posted);
        var unconfirmed = h.Log.Entries.Single(e => e.Msg == "browserVersionUnconfirmed");
        Assert.Equal(LogLevel.Warn, unconfirmed.Level);
        Assert.Equal(AppId, unconfirmed.Data!["appId"]);
        Assert.Contains("nope", (string)unconfirmed.Data["reason"]!, StringComparison.Ordinal);
        Assert.Equal("unknown", h.Log.Entries.Single(e => e.Msg == "cdpConnected").Data!["browserVersion"]);
        Assert.Equal(StartSetup, h.Transport.Methods());
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpUnavailable" or "browserVersionMismatch");
    }

    [Theory]
    [InlineData("""{"product":"Edge"}""")]
    [InlineData("""{"product":"Edg/garbage"}""")]
    [InlineData("""{"product":null}""")]
    [InlineData("{}")]
    public async Task Unparsable_product_is_unconfirmed_and_the_start_completes(string result)
    {
        using var h = new Harness();
        h.Transport.VersionResult = result;

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        Assert.Equal(new CommandCompleted(7), h.Posted[1]);
        Assert.Equal("unparsableProduct", h.Log.Entries.Single(e => e.Msg == "browserVersionUnconfirmed").Data!["reason"]);
        Assert.Equal("unknown", h.Log.Entries.Single(e => e.Msg == "cdpConnected").Data!["browserVersion"]);
    }

    [Fact]
    public async Task Get_version_reply_missing_at_the_deadline_is_cdp_unavailable_with_setup_failed()
    {
        using var h = new Harness();
        h.Transport.HoldMethod = "Browser.getVersion";

        h.Host.Start(new StartApp(7, AppId));
        await Eventually(() => h.Transport.Methods().Contains("Browser.getVersion"));
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await h.WaitPosted(2);

        AssertSetupFailed(h);
        Assert.Equal(StartSetup[..4], h.Transport.Methods());
        Assert.Equal(CurrentVersion, h.Log.Entries.Single(e => e.Msg == "cdpUnavailable").Data!["browserVersion"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "browserVersionUnconfirmed");
    }

    [Fact]
    public async Task Stop_waits_for_the_root_to_exit_after_killing_the_tree()
    {
        using var h = new Harness();
        h.Process.IgnoreClose = true;
        h.Process.IgnoreKill = true;
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Host.Stop(new StopApp(8, AppId));
        await Eventually(() => h.Process.TimedWaits == 1);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await Eventually(() => h.Process.TimedWaits == 2);
        Assert.Equal(1, h.Process.KillCalls);
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(100, Ct);
        Assert.Equal(2, h.Posted.Count);

        h.Process.Exit(1);
        await h.WaitPosted(4);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted.Skip(2));
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "processKillTimedOut");
    }

    [Fact]
    public async Task Stop_gives_up_waiting_for_the_root_after_5_seconds()
    {
        using var h = new Harness();
        h.Process.IgnoreClose = true;
        h.Process.IgnoreKill = true;
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Host.Stop(new StopApp(8, AppId));
        await Eventually(() => h.Process.TimedWaits == 1);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await Eventually(() => h.Process.TimedWaits == 2);
        h.Time.Advance(TimeSpan.FromMilliseconds(4_900));
        await Task.Delay(100, Ct);
        Assert.Equal(2, h.Posted.Count);

        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.WaitPosted(4);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted.Skip(2));
        Assert.False(h.Process.HasExited);
        var timedOut = h.Log.Entries.Single(e => e.Msg == "processKillTimedOut");
        Assert.Equal(LogLevel.Warn, timedOut.Level);
        Assert.Equal(4242, timedOut.Data!["pid"]);
    }

    [Fact]
    public async Task Restart_checks_profile_and_port_only_after_the_old_root_has_exited()
    {
        using var h = new Harness(connect: (_, _) => Task.FromResult<ICdpTransport?>(new FakeTransport()));
        h.Process.IgnoreClose = true;
        h.Process.IgnoreKill = true;
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Host.Start(new StartApp(8, AppId));
        await Eventually(() => h.Process.TimedWaits == 1);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await Eventually(() => h.Process.TimedWaits == 2);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(100, Ct);
        Assert.Single(h.ProfileChecks);

        // Stary korzeń kończy się dopiero po zabiciu drzewa: kontrola nowego startu idzie po jego wyjściu.
        h.Process.Exit(1);
        await h.WaitPosted(4);

        Assert.Equal([new AppWindowAttached(AppId, 0x1235, 4243), new CommandCompleted(8)], h.Posted.Skip(2));
        Assert.Equal(2, h.ProfileChecks.Count);
        Assert.False(h.ProfileChecks[0].FirstExited);
        Assert.True(h.ProfileChecks[1].FirstExited);
        Assert.Equal([9222, 9222], h.PortChecks);
        Assert.Equal(2, h.Launcher.Starts.Count);
    }

    // ── Story 6.3: binding po nawigacji i reconnect CDP ───────────────────────

    [Fact]
    public async Task Each_main_frame_navigation_re_adds_only_the_binding_and_never_the_script()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://www.netflix.com/browse"}}}""");
        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://www.netflix.com/title/1"}}}""");
        await Eventually(() => h.Transport.Methods().Length == 7);
        await Task.Delay(100, Ct);

        Assert.Equal([.. StartSetup, "Runtime.addBinding", "Runtime.addBinding"], h.Transport.Methods());
    }

    [Fact]
    public async Task Binding_refresh_cut_by_a_closing_socket_is_silent()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.HoldMethod = "Runtime.addBinding";
        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://x/"}}}""");
        await Eventually(() => h.Transport.Methods().Length == 6);
        h.Transport.Close();
        await h.WaitLog("cdpClosed");
        await Task.Delay(100, Ct);

        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "bindingRefreshFailed");
    }

    [Fact]
    public async Task Drop_with_quick_recovery_reinstalls_on_the_same_target_without_navigate()
    {
        using var h = new Harness();
        // Inny cel w transporcie reconnectu: próby muszą iść na adres zapamiętany z pierwszego połączenia.
        var second = new FakeTransport { Endpoint = OtherTarget };
        h.Reconnects.Enqueue(null);
        h.Reconnects.Enqueue(second);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        await StepToAttempt(h, closedAt, 750, 2);
        var reconnected = await h.WaitLog("cdpReconnected");

        Assert.Equal(LogLevel.Info, reconnected.Level);
        Assert.Equal(AppId, reconnected.Data!["appId"]);
        Assert.Equal(2, reconnected.Data["attempts"]);
        Assert.Equal(750L, reconnected.Data["elapsedMs"]);
        Assert.Equal([closedAt.AddMilliseconds(250), closedAt.AddMilliseconds(750)], h.ReconnectAt);
        Assert.All(h.ReconnectEndpoints, endpoint => Assert.Equal(h.Transport.Endpoint, endpoint));

        // Dokładnie trzy wywołania setupu na nowej sesji: bez Page.navigate, bez Runtime.enable, nic z przerwy.
        Assert.Equal(["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument"], second.Methods());
        Assert.Equal("__msBridge", second.Sent[0].GetProperty("params").GetProperty("name").GetString());
        Assert.StartsWith(WrappedRuntime, second.Sent[2].GetProperty("params").GetProperty("source").GetString(), StringComparison.Ordinal);
        Assert.Equal(StartSetup, h.Transport.Methods());
        Assert.DoesNotContain("Browser.getVersion", second.Methods());
        Assert.Equal(2, h.Posted.Count);
        Assert.DoesNotContain(h.Log.Entries, e => e.Level is LogLevel.Warn or LogLevel.Error);

        // Nowa sesja działa jak pierwsza: binding po nawigacji i hello strony.
        second.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://www.netflix.com/browse"}}}""");
        second.PushBinding("__msBridge", """{"v":1,"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0.0.0","webdriver":false}}""");
        await h.WaitLog("pageHello");
        await Eventually(() => second.Methods().Length == 4);
        Assert.Equal("Runtime.addBinding", second.Methods()[3]);
    }

    [Fact]
    public async Task Drop_without_recovery_posts_cdp_lost_exactly_at_the_reconnect_timeout()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        var schedule = NeverBackScheduleMs;
        for (var i = 0; i < schedule.Length; i++)
        {
            await StepToAttempt(h, closedAt, schedule[i], i + 1);
        }

        Assert.Equal(29_750, schedule[^1]);
        Assert.Equal(schedule.Select(ms => closedAt.AddMilliseconds(ms)), h.ReconnectAt);
        h.Time.Advance(closedAt.AddMilliseconds(29_900) - h.Time.GetUtcNow());
        await Task.Delay(100, Ct);
        Assert.Equal(2, h.Posted.Count);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpLost");

        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.WaitPosted(3);

        Assert.Equal(new CdpLost(AppId), h.Posted[2]);
        var lost = await h.WaitLog("cdpLost");
        Assert.Equal(LogLevel.Warn, lost.Level);
        Assert.Equal(AppId, lost.Data!["appId"]);
        Assert.Equal(30d, lost.Data["timeoutSeconds"]);
        Assert.Equal(17, lost.Data["attempts"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpReconnected");

        h.Time.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(100, Ct);
        Assert.Equal(3, h.Posted.Count);
        Assert.Equal(17, h.ReconnectAttempts);
        Assert.Equal(0, h.Process.KillCalls);
    }

    [Theory]
    [InlineData("Runtime.addBinding")]
    [InlineData("Page.enable")]
    [InlineData("Page.addScriptToEvaluateOnNewDocument")]
    public async Task Setup_failure_on_reconnect_disposes_the_transport_and_keeps_trying(string failMethod)
    {
        string[] setup = ["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument"];
        using var h = new Harness();
        var failing = new FakeTransport { FailMethod = failMethod };
        var good = new FakeTransport();
        h.Reconnects.Enqueue(failing);
        h.Reconnects.Enqueue(good);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        await Eventually(() => failing.Disposed);
        await StepToAttempt(h, closedAt, 750, 2);
        var reconnected = await h.WaitLog("cdpReconnected");

        Assert.Equal(setup[..(Array.IndexOf(setup, failMethod) + 1)], failing.Methods());
        Assert.Equal(2, reconnected.Data!["attempts"]);
        Assert.Equal(["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument"], good.Methods());
        Assert.Equal(2, h.Posted.Count);
    }

    [Fact]
    public async Task Late_transport_after_the_deadline_is_disposed_without_a_second_cdp_lost()
    {
        var pending = new TaskCompletionSource<ICdpTransport?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var h = new Harness(reconnect: (_, _) => pending.Task);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        h.Time.Advance(closedAt.AddSeconds(30) - h.Time.GetUtcNow());
        await h.WaitPosted(3);
        Assert.Equal(new CdpLost(AppId), h.Posted[2]);

        var late = new FakeTransport();
        pending.SetResult(late);
        await Eventually(() => late.Disposed);
        await Task.Delay(100, Ct);

        Assert.Empty(late.Methods());
        Assert.Equal(3, h.Posted.Count);
        Assert.Equal(1, h.ReconnectAttempts);
        Assert.Single(h.Log.Entries, e => e.Msg == "cdpLost");
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpReconnected");
    }

    [Fact]
    public async Task Stop_during_reconnect_cancels_it_and_a_late_transport_is_disposed()
    {
        var pending = new TaskCompletionSource<ICdpTransport?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new ConcurrentQueue<CancellationToken>();
        using var h = new Harness(reconnect: (_, token) =>
        {
            tokens.Enqueue(token);
            return pending.Task;
        });
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(4);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted.Skip(2));
        Assert.True(Assert.Single(tokens).IsCancellationRequested);
        var late = new FakeTransport();
        pending.SetResult(late);
        await Eventually(() => late.Disposed);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100, Ct);

        Assert.Empty(late.Methods());
        Assert.Equal(4, h.Posted.Count);
        Assert.Equal(1, h.ReconnectAttempts);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpLost" or "cdpReconnected" or "processExited");
    }

    [Fact]
    public async Task Stop_between_reconnect_attempts_ends_the_loop_with_only_the_stop_results()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(4);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100, Ct);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted.Skip(2));
        Assert.Equal(4, h.Posted.Count);
        Assert.Equal(1, h.ReconnectAttempts);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpLost");
    }

    [Fact]
    public async Task Process_exit_during_reconnect_ends_the_loop_without_cdp_lost()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        h.Process.Exit(0);
        await h.WaitPosted(3);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100, Ct);

        Assert.Equal(new ProcessExited(AppId), h.Posted[2]);
        Assert.Equal(3, h.Posted.Count);
        Assert.Equal(1, h.ReconnectAttempts);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpLost");
    }

    [Fact]
    public async Task Stale_session_traffic_after_replacement_is_ignored()
    {
        using var h = new Harness();
        // Sesja z nieudanym setupem zostaje zwolniona, ale jej pętla odbioru dalej dostarcza zdarzenia.
        var stale = new FakeTransport { FailMethod = "Page.enable", DeliverAfterDispose = true };
        var current = new FakeTransport();
        h.Reconnects.Enqueue(stale);
        h.Reconnects.Enqueue(current);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        await Eventually(() => stale.Disposed);
        await StepToAttempt(h, closedAt, 750, 2);
        await h.WaitLog("cdpReconnected");

        // Stara sesja po podmianie: nawigacja głównej ramki i hello nie mają skutku.
        var delivered = stale.Delivered;
        stale.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://x/"}}}""");
        stale.PushBinding("__msBridge", """{"v":1,"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0.0.0","webdriver":false}}""");
        stale.Push("""{"id":1,"result":{}}""");
        await Eventually(() => stale.Delivered >= delivered + 3);
        await Task.Delay(200, Ct);

        Assert.Equal(["Runtime.addBinding", "Page.enable"], stale.Methods());
        Assert.Equal(["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument"], current.Methods());
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "pageHello" or "pageMessageRejected" or "bindingRefreshFailed");
        Assert.Equal(2, h.Posted.Count);
        stale.Close();
    }

    [Fact]
    public async Task Setup_reply_held_on_a_reconnected_transport_is_bounded_by_the_deadline()
    {
        using var h = new Harness();
        var held = new FakeTransport { HoldMethod = "Runtime.addBinding" };
        h.Reconnects.Enqueue(held);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        await Eventually(() => held.Methods().Length == 1);
        h.Time.Advance(closedAt.AddMilliseconds(29_900) - h.Time.GetUtcNow());
        await Task.Delay(100, Ct);
        Assert.Equal(2, h.Posted.Count);
        Assert.False(held.Disposed);

        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.WaitPosted(3);

        Assert.Equal(new CdpLost(AppId), h.Posted[2]);
        Assert.Single(h.Log.Entries, e => e.Msg == "cdpLost");
        await Eventually(() => held.Disposed);
        Assert.Equal(["Runtime.addBinding"], held.Methods());
        Assert.Equal(1, h.ReconnectAttempts);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpReconnected");
    }

    [Fact]
    public async Task Faulting_reconnect_seam_is_a_failed_attempt_and_ends_in_cdp_lost()
    {
        using var h = new Harness(reconnect: (_, _) => Task.FromException<ICdpTransport?>(new InvalidOperationException("x")));
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        var schedule = NeverBackScheduleMs;
        for (var i = 0; i < schedule.Length; i++)
        {
            await StepToAttempt(h, closedAt, schedule[i], i + 1);
        }

        h.Time.Advance(closedAt.AddSeconds(30) - h.Time.GetUtcNow());
        await h.WaitPosted(3);
        await Task.Delay(100, Ct);

        Assert.Equal(schedule.Select(ms => closedAt.AddMilliseconds(ms)), h.ReconnectAt);
        Assert.Equal(17, h.ReconnectAttempts);
        Assert.Equal(3, h.Posted.Count);
        Assert.Equal(new CdpLost(AppId), h.Posted[2]);
        Assert.Single(h.Log.Entries, e => e.Msg == "cdpLost");
        Assert.DoesNotContain(h.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Main_frame_navigation_during_reconnect_setup_re_adds_the_binding_on_the_new_session()
    {
        using var h = new Harness();
        var held = new FakeTransport { HoldMethod = "Page.enable" };
        h.Reconnects.Enqueue(held);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        await Eventually(() => held.Methods().Length == 2);

        // Nowa sesja jest bieżąca już w trakcie setupu, więc nawigacja głównej ramki odświeża na niej binding.
        held.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://www.netflix.com/browse"}}}""");
        await Eventually(() => held.Methods().Length == 3);
        Assert.Equal("Runtime.addBinding", held.Methods()[2]);

        held.ReleaseHeld();
        await h.WaitLog("cdpReconnected");
        await Eventually(() => held.Methods().Length == 4);

        var methods = held.Methods();
        Assert.Equal(["Runtime.addBinding", "Page.enable"], methods[..2]);
        Assert.Equal(["Page.addScriptToEvaluateOnNewDocument", "Runtime.addBinding"], methods[2..].Order(StringComparer.Ordinal));
        Assert.Equal(2, h.Posted.Count);
    }

    [Fact]
    public async Task Next_backoff_counts_from_the_start_of_a_slow_attempt()
    {
        var pending = new TaskCompletionSource<ICdpTransport?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var h = new Harness(reconnect: (_, _) =>
            Interlocked.Increment(ref calls) == 1 ? pending.Task : Task.FromResult<ICdpTransport?>(null));
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);

        // Pierwsza próba trwa do 850 ms: przerwa 500 ms od jej startu (250 ms) już minęła, więc druga idzie od razu.
        h.Time.Advance(closedAt.AddMilliseconds(850) - h.Time.GetUtcNow());
        pending.SetResult(null);
        await Eventually(() => h.ReconnectAttempts == 2);

        Assert.Equal(closedAt.AddMilliseconds(850), h.ReconnectAt[1]);
    }

    [Fact]
    public async Task Stop_while_a_reconnected_transport_holds_its_setup_reply_leaves_no_live_channel()
    {
        using var h = new Harness();
        var held = new FakeTransport { HoldMethod = "Runtime.addBinding" };
        h.Reconnects.Enqueue(held);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        await Eventually(() => held.Methods().Length == 1);

        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(4);
        await Eventually(() => held.Disposed);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100, Ct);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted.Skip(2));
        Assert.Equal(4, h.Posted.Count);
        Assert.True(held.Disposed);
        Assert.Equal(["Runtime.addBinding"], held.Methods());
        Assert.Equal(1, h.ReconnectAttempts);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpReconnected" or "cdpLost");
        Assert.DoesNotContain(h.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Late_transport_after_process_exit_is_disposed_and_posts_nothing_more()
    {
        var pending = new TaskCompletionSource<ICdpTransport?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var h = new Harness(reconnect: (_, _) => pending.Task);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        h.Process.Exit(0);
        await h.WaitPosted(3);
        Assert.Equal(new ProcessExited(AppId), h.Posted[2]);

        var late = new FakeTransport();
        pending.SetResult(late);
        await Eventually(() => late.Disposed);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100, Ct);

        Assert.Empty(late.Methods());
        Assert.Equal(3, h.Posted.Count);
        Assert.Equal(1, h.ReconnectAttempts);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpLost" or "cdpReconnected");
    }

    [Fact]
    public async Task Two_successive_drops_both_recover_on_the_same_endpoint()
    {
        using var h = new Harness();
        // Inne cele w transportach reconnectu: obie próby muszą wrócić do adresu z pierwszego połączenia.
        var second = new FakeTransport { Endpoint = OtherTarget };
        var third = new FakeTransport { Endpoint = OtherTarget };
        h.Reconnects.Enqueue(second);
        h.Reconnects.Enqueue(third);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var firstDrop = await Drop(h, h.Transport);
        await StepToAttempt(h, firstDrop, 250, 1);
        await Eventually(() => h.Log.Entries.Count(e => e.Msg == "cdpReconnected") == 1);

        var secondDrop = await Drop(h, second);
        await StepToAttempt(h, secondDrop, 250, 2);
        await Eventually(() => h.Log.Entries.Count(e => e.Msg == "cdpReconnected") == 2);

        Assert.True(second.Disposed);
        Assert.Equal(["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument"], third.Methods());
        Assert.Equal([h.Transport.Endpoint, h.Transport.Endpoint], h.ReconnectEndpoints);
        Assert.Equal(2, h.Log.Entries.Count(e => e.Msg == "cdpClosed"));
        Assert.Equal(2, h.Posted.Count);
    }

    [Fact]
    public async Task Socket_closing_during_setup_keeps_the_cdp_unavailable_path_without_reconnect()
    {
        using var h = new Harness();
        h.Transport.HoldMethod = "Page.enable";
        h.Host.Start(new StartApp(7, AppId));
        await Eventually(() => h.Transport.Methods().Contains("Page.enable"));

        h.Transport.Close();
        await h.WaitPosted(2);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100, Ct);

        AssertSetupFailed(h);
        Assert.Equal(0, h.ReconnectAttempts);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpClosed" or "cdpLost");
    }

    [Fact]
    public async Task Target_outside_the_app_loopback_port_is_never_reconnected_and_ends_in_cdp_lost()
    {
        var foreign = new FakeTransport { Endpoint = new Uri("ws://127.0.0.1:9333/devtools/page/X") };
        using var h = new Harness(connect: (_, _) => Task.FromResult<ICdpTransport?>(foreign));
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        foreign.Close();
        await h.WaitLog("cdpClosed");
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.WaitPosted(3);

        Assert.Equal(new CdpLost(AppId), h.Posted[2]);
        Assert.Equal(0, h.ReconnectAttempts);
        foreign.Dispose();
    }

    [Fact]
    public async Task After_cdp_lost_stop_still_stops_the_live_tree()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        await Drop(h, h.Transport);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.WaitPosted(3);
        Assert.False(h.Process.HasExited);

        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(5);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted.Skip(3));
        Assert.Equal(1, h.Process.CloseCalls);
        Assert.True(h.Process.HasExited);
    }

    [Fact]
    public async Task Home_during_the_outage_shows_the_shell_and_cdp_lost_from_the_host_fails_the_tile()
    {
        var (orchestrator, _) = OrchestratorTestSupport.Started();
        OrchestratorTestSupport.Foreground(orchestrator, AppId);
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);
        await Drop(h, h.Transport);

        var home = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1));
        Assert.Contains(home.Commands, command => command is ShowShell);
        Assert.Equal(AppLifecycleState.Background, OrchestratorTestSupport.StateOf(home.Snapshot!, AppId));

        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.WaitPosted(3);
        var lost = Assert.IsType<CdpLost>(h.Posted[2]);
        var failed = orchestrator.Handle(lost);
        Assert.Equal(AppLifecycleState.Failed, OrchestratorTestSupport.StateOf(failed.Snapshot!, AppId));

        var homeAfter = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));
        Assert.Contains(homeAfter.Commands, command => command is ShowShell);
        Assert.Equal(AppLifecycleState.Failed, OrchestratorTestSupport.StateOf(homeAfter.Snapshot!, AppId));
    }

    [Fact]
    public async Task Cdp_lost_while_foreground_fails_the_tile_and_shows_the_shell()
    {
        var (orchestrator, _) = OrchestratorTestSupport.Started();
        OrchestratorTestSupport.Foreground(orchestrator, AppId);
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);
        await Drop(h, h.Transport);

        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.WaitPosted(3);
        var failed = orchestrator.Handle(Assert.IsType<CdpLost>(h.Posted[2]));

        Assert.Equal(AppLifecycleState.Failed, OrchestratorTestSupport.StateOf(failed.Snapshot!, AppId));
        Assert.Contains(failed.Commands, command => command is ShowShell);
        var home = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1));
        Assert.Contains(home.Commands, command => command is ShowShell);
    }

    [Theory]
    [InlineData("ws://0.0.0.0:9222/devtools/page/X")]
    [InlineData("ws://192.168.1.10:9222/devtools/page/X")]
    [InlineData("ws://localhost:9222/devtools/page/X")]
    [InlineData("ws://127.0.0.1:9333/devtools/page/X")]
    [InlineData("wss://127.0.0.1:9222/devtools/page/X")]
    public async Task Reconnect_rejects_an_endpoint_outside_the_app_loopback_port(string url)
    {
        var started = DateTime.UtcNow;

        var transport = await CdpConnector.ReconnectAsync(new Uri(url), 9222, Ct);

        Assert.Null(transport);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Reconnect_on_an_unused_loopback_port_returns_null()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var transport = await CdpConnector.ReconnectAsync(new Uri($"ws://127.0.0.1:{port}/devtools/page/X"), port, cts.Token);

        Assert.Null(transport);
    }

    [Fact]
    public async Task Reconnect_opens_the_same_target_path_on_a_real_loopback_socket()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var endpoint = new Uri($"ws://127.0.0.1:{port}/devtools/page/{FakeTransport.TargetId}");
            var server = Task.Run(() => ServeOneWebSocketEchoAsync(listener), Ct);

            using var transport = await CdpConnector.ReconnectAsync(endpoint, port, Ct);

            Assert.NotNull(transport);
            Assert.Equal(endpoint, transport.Endpoint);
            await transport.SendAsync("""{"id":1,"method":"Page.enable","params":{}}""", Ct);
            Assert.Equal("""{"id":1,"method":"Page.enable","params":{}}""", await transport.ReceiveAsync().WaitAsync(Wait, Ct));
            var request = await server.WaitAsync(Wait, Ct);
            Assert.StartsWith($"GET /devtools/page/{FakeTransport.TargetId} HTTP/1.1", request, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\nOrigin:", request, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Minimalny serwer WebSocket na loopbacku: handshake RFC 6455, jedna wiadomość odesłana echem.</summary>
    private static async Task<string> ServeOneWebSocketEchoAsync(System.Net.Sockets.TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync(Ct);
        var stream = client.GetStream();
        var header = new List<byte>();
        var one = new byte[1];
        while (header.Count < 4 || !(header[^4] == '\r' && header[^3] == '\n' && header[^2] == '\r' && header[^1] == '\n'))
        {
            if (await stream.ReadAsync(one, Ct) == 0)
            {
                throw new IOException("Klient zamknął połączenie przed końcem nagłówków.");
            }

            header.Add(one[0]);
        }

        var request = System.Text.Encoding.ASCII.GetString([.. header]);
        var key = System.Text.RegularExpressions.Regex.Match(
            request,
            "^Sec-WebSocket-Key:\\s*(\\S+)\\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value;
        var accept = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), Ct);

        using var socket = System.Net.WebSockets.WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
        var buffer = new byte[4096];
        var received = await socket.ReceiveAsync(buffer.AsMemory(), Ct);
        await socket.SendAsync(buffer.AsMemory(0, received.Count), System.Net.WebSockets.WebSocketMessageType.Text, endOfMessage: true, Ct);
        return request;
    }

    /// <summary>Chwile prób (ms od zamknięcia) celu, który nie wraca: 250, 750, 1750, potem co 2 s do 29 750 (17 prób).</summary>
    private static readonly int[] NeverBackScheduleMs = [250, 750, 1750, .. Enumerable.Range(0, 14).Select(i => 3750 + (i * 2000))];

    /// <summary>Ten sam port co <see cref="FakeTransport.Endpoint"/>, inny cel strony.</summary>
    private static readonly Uri OtherTarget = new("ws://127.0.0.1:9222/devtools/page/0THER0");

    /// <summary>Zamyka transport i czeka na kolejny wpis <c>cdpClosed</c>; zwraca chwilę zamknięcia na zegarze testu.</summary>
    private static async Task<DateTimeOffset> Drop(Harness h, FakeTransport transport)
    {
        var before = h.Log.Entries.Count(e => e.Msg == "cdpClosed");
        transport.Close();
        await Eventually(() => h.Log.Entries.Count(e => e.Msg == "cdpClosed") == before + 1);
        return h.Time.GetUtcNow();
    }

    /// <summary>
    /// Przesuwa zegar tuż przed <paramref name="offsetMs"/> od zamknięcia (próba jeszcze nie może paść), potem dokładnie
    /// na tę chwilę i czeka na próbę numer <paramref name="attempt"/>.
    /// </summary>
    private static async Task StepToAttempt(Harness h, DateTimeOffset closedAt, int offsetMs, int attempt)
    {
        var target = closedAt.AddMilliseconds(offsetMs);
        h.Time.Advance(target - TimeSpan.FromMilliseconds(1) - h.Time.GetUtcNow());
        await Task.Delay(30, Ct);
        Assert.Equal(attempt - 1, h.ReconnectAttempts);

        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        await Eventually(() => h.ReconnectAttempts == attempt);
    }

    private static void AssertSetupFailed(Harness h)
    {
        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandFailed(7, ErrorCode.CdpUnavailable)], h.Posted);
        var warn = h.Log.Entries.Single(e => e.Msg == "cdpUnavailable");
        Assert.Equal(LogLevel.Warn, warn.Level);
        Assert.Equal("setupFailed", warn.Data!["reason"]);
        Assert.True(h.Transport.Disposed);
        Assert.Equal(0, h.Process.KillCalls);
        Assert.False(h.Process.HasExited);
    }

    private static void AssertStoppedDuringStart(Harness h, int expectedPosted)
    {
        var posted = h.Posted;
        Assert.Equal(expectedPosted, posted.Count);
        Assert.Contains(new CommandFailed(7, ErrorCode.Unexpected), posted);
        Assert.Contains(new ProcessExited(AppId), posted);
        Assert.Contains(new CommandCompleted(8), posted);
        Assert.Equal("stoppedDuringStart", StartFailure(h).Data!["reason"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpUnavailable" or "processExited");
        Assert.True(h.Process.KillCalls >= 1);
        Assert.True(h.Process.HasExited);
    }

    // ── Story 6.5: ProbeExisting i RenewSession ───────────────────────────────

    /// <summary>Jedyne wywołania CDP po adopcji i na odświeżonej albo odzyskanej sesji.</summary>
    private static readonly string[] InstallSetup = ["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument"];

    private const string HelloPayload = """{"v":1,"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0.0.0","webdriver":false}}""";

    private const string MainFrameNavigated = """{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://www.netflix.com/browse"}}}""";

    private static readonly AppWindowAttached OrphanAttached = new(AppId, Harness.OrphanHwnd, Harness.OrphanPid);

    /// <summary>Sonda z osieroconym korzeniem (profil i port aplikacji, okno); czeka na wpis końca sondy.</summary>
    private static async Task<FakeProcess> Adopt(Harness h)
    {
        var orphan = h.AddOrphan();
        h.Host.Probe(new ProbeExisting(1, AppId));
        await h.WaitLog("probeExisting");
        return orphan;
    }

    [Fact]
    public async Task Probe_adopts_the_live_root_without_a_launch_and_installs_only_binding_page_enable_and_script()
    {
        using var h = new Harness();
        // Proces pomocniczy z oknem przed korzeniem: `--type=` wyklucza go mimo okna.
        h.AddOrphan(Harness.OrphanPid - 1, Harness.BrowserLine() + " --type=renderer");
        h.AddOrphan();

        h.Host.Probe(new ProbeExisting(1, AppId));
        var probe = await h.WaitLog("probeExisting");

        Assert.Equal("adopted", probe.Data!["outcome"]);
        Assert.Equal(Harness.OrphanPid, probe.Data["rootPid"]);
        Assert.Equal([OrphanAttached], h.Posted);
        Assert.Equal(InstallSetup, h.Transport.Methods());
        Assert.Equal("__msBridge", h.Transport.Sent[0].GetProperty("params").GetProperty("name").GetString());
        Assert.StartsWith(WrappedRuntime, h.Transport.Sent[2].GetProperty("params").GetProperty("source").GetString(), StringComparison.Ordinal);
        Assert.Equal(9222, h.ConnectedPort);
        Assert.Equal([Harness.OrphanPid], h.Opened);

        // Bez drugiego procesu i bez kontroli startu albo odczytu wersji (AD-13: „nie przy adopcji”).
        Assert.Empty(h.Launcher.Starts);
        Assert.Empty(h.ProfileChecks);
        Assert.Empty(h.PortChecks);
        Assert.Empty(h.VersionReads);

        var adopted = Assert.Single(h.Log.Entries, e => e.Msg == "appAdopted");
        Assert.Equal(LogLevel.Info, adopted.Level);
        Assert.Equal(AppId, adopted.Data!["appId"]);
        Assert.Equal(Harness.OrphanPid, adopted.Data["rootPid"]);
        Assert.Equal(Harness.OrphanHwnd, adopted.Data["hwnd"]);
        Assert.Equal("Chrome_WidgetWin_1", adopted.Data["class"]);
        var connected = Assert.Single(h.Log.Entries, e => e.Msg == "cdpConnected");
        Assert.Equal(true, connected.Data!["adopted"]);
        Assert.Equal(9222, connected.Data["cdpPort"]);
        var messages = h.Log.Entries.Select(e => e.Msg).ToList();
        Assert.True(messages.IndexOf("appAdopted") < messages.IndexOf("cdpConnected"));
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "appLaunch" or "browserVersion" or "appWindowAttached");
        Assert.DoesNotContain(h.Log.Entries, e => e.Level is LogLevel.Warn or LogLevel.Error);

        // Most działa jak po starcie: binding po nawigacji głównej ramki i hello strony.
        h.Transport.Push(MainFrameNavigated);
        h.Transport.PushBinding("__msBridge", HelloPayload);
        await h.WaitLog("pageHello");
        await Eventually(() => h.Transport.Methods().Length == 4);
        Assert.Equal("Runtime.addBinding", h.Transport.Methods()[3]);
    }

    [Theory]
    [InlineData("none", "notRunning")]
    [InlineData("helperOnly", "notRunning")]
    [InlineData("siblingProfile", "notRunning")]
    [InlineData("otherProfile", "notRunning")]
    [InlineData("rootGone", "notRunning")]
    [InlineData("rootWithoutWindow", "noWindow")]
    public async Task Probe_without_an_adoptable_root_is_silent_and_never_retried(string scenario, string outcome)
    {
        using var h = new Harness();
        switch (scenario)
        {
            case "helperOnly":
                h.AddOrphan(commandLine: Harness.BrowserLine() + " --type=gpu-process");
                break;
            case "siblingProfile":
                h.AddOrphan(commandLine: Harness.BrowserLine(profileDir: ProfileDir + "2"));
                break;
            case "otherProfile":
                h.AddCommandLine(Harness.OrphanPid, Harness.BrowserLine(profileDir: Path.Combine(ProfilesDir, "other")));
                break;
            case "rootGone":
                h.AddOrphan().Exit(0);
                break;
            case "rootWithoutWindow":
                h.AddOrphan(window: false);
                break;
        }

        h.Host.Probe(new ProbeExisting(1, AppId));
        var probe = await h.WaitLog("probeExisting");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(100, Ct);

        Assert.Equal(LogLevel.Info, probe.Level);
        Assert.Equal(outcome, probe.Data!["outcome"]);
        Assert.False(probe.Data.ContainsKey("rootPid"));
        Assert.Empty(h.Posted);
        Assert.Empty(h.Launcher.Starts);
        Assert.Equal(0, h.ConnectCalls);
        Assert.Equal(1, h.CommandLineScans);
        Assert.Single(h.Log.Entries, e => e.Msg == "probeExisting");
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "appAdopted" || e.Level is LogLevel.Warn or LogLevel.Error);
        Assert.All(h.Orphans.Values, orphan => Assert.Equal(0, orphan.KillCalls + orphan.CloseCalls));
    }

    [Fact]
    public async Task Failing_command_line_scan_logs_probe_failed_then_probe_existing_failed_and_posts_nothing()
    {
        using var h = new Harness();
        h.AddOrphan();
        h.CommandLinesThrow = new InvalidOperationException("WMI down");

        h.Host.Probe(new ProbeExisting(1, AppId));
        var probe = await h.WaitLog("probeExisting");

        Assert.Equal("failed", probe.Data!["outcome"]);
        Assert.False(probe.Data.ContainsKey("rootPid"));
        var messages = h.Log.Entries.Select(e => e.Msg).ToList();
        Assert.True(messages.IndexOf("browserProbeFailed") >= 0 && messages.IndexOf("browserProbeFailed") < messages.IndexOf("probeExisting"));
        Assert.Equal(LogLevel.Error, h.Log.Entries.Single(e => e.Msg == "browserProbeFailed").Level);
        Assert.Empty(h.Posted);
        Assert.Equal(0, h.ConnectCalls);
        Assert.Empty(h.Launcher.Starts);
    }

    [Fact]
    public async Task Probe_takes_the_first_root_that_has_a_window()
    {
        using var h = new Harness();
        h.AddOrphan(Harness.OrphanPid - 5, window: false);
        h.AddOrphan();

        h.Host.Probe(new ProbeExisting(1, AppId));
        var probe = await h.WaitLog("probeExisting");

        Assert.Equal("adopted", probe.Data!["outcome"]);
        Assert.Equal(Harness.OrphanPid, probe.Data["rootPid"]);
        Assert.Equal([OrphanAttached], h.Posted);
        Assert.Equal([Harness.OrphanPid], h.Opened);
    }

    [Fact]
    public async Task Probe_stands_down_when_the_app_already_has_a_session()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);
        h.AddOrphan();

        h.Host.Probe(new ProbeExisting(1, AppId));
        var probe = await h.WaitLog("probeExisting");

        Assert.Equal("sessionExists", probe.Data!["outcome"]);
        Assert.Equal(2, h.Posted.Count);
        Assert.Empty(h.Opened);
        Assert.Equal(1, h.ConnectCalls);
    }

    [Fact]
    public async Task Probe_of_an_unknown_app_logs_unknown_app()
    {
        using var h = new Harness();
        h.AddOrphan();

        h.Host.Probe(new ProbeExisting(1, "plex"));
        var probe = await h.WaitLog("probeExisting");

        Assert.Equal("unknownApp", probe.Data!["outcome"]);
        Assert.Equal("plex", probe.Data["appId"]);
        Assert.Empty(h.Posted);
        Assert.Equal(0, h.CommandLineScans);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(92220)]
    [InlineData(9223)]
    public async Task Adopted_root_without_the_app_debugging_port_is_cdp_lost_and_stop_still_kills_its_tree(int? port)
    {
        using var h = new Harness();
        var orphan = h.AddOrphan(commandLine: Harness.BrowserLine(port));
        orphan.IgnoreClose = true;

        h.Host.Probe(new ProbeExisting(1, AppId));
        var probe = await h.WaitLog("probeExisting");

        Assert.Equal("adopted", probe.Data!["outcome"]);
        Assert.Equal([OrphanAttached, new CdpLost(AppId)], h.Posted);
        var warn = Assert.Single(h.Log.Entries, e => e.Msg == "cdpUnavailable");
        Assert.Equal(LogLevel.Warn, warn.Level);
        Assert.Equal(AppId, warn.Data!["appId"]);
        Assert.Equal("portMismatch", warn.Data["reason"]);
        Assert.Equal(true, warn.Data["adopted"]);
        Assert.Equal(9222, warn.Data["cdpPort"]);
        Assert.Equal(0, h.ConnectCalls);
        Assert.False(orphan.HasExited);
        Assert.Equal(0, orphan.KillCalls);

        // Sesja zostaje zarejestrowana: Close z AppMenu zamyka okno (3 s), potem zabija drzewo sieroty.
        h.Host.Stop(new StopApp(8, AppId));
        await Eventually(() => orphan.TimedWaits == 1);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await h.WaitPosted(4);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(8)], h.Posted.Skip(2));
        Assert.Equal(1, orphan.CloseCalls);
        Assert.Equal(1, orphan.KillCalls);
        Assert.True(orphan.HasExited);
        Assert.Empty(h.Launcher.Starts);
    }

    [Fact]
    public async Task Adopted_cdp_that_never_answers_is_cdp_lost_exactly_at_the_connect_deadline()
    {
        using var h = new Harness(connect: async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        var orphan = h.AddOrphan();

        h.Host.Probe(new ProbeExisting(1, AppId));
        await h.WaitPosted(1);
        await Eventually(() => h.ConnectCalls == 1);
        Assert.Equal([OrphanAttached], h.Posted);

        h.Time.Advance(TimeSpan.FromMilliseconds(14_900));
        await Task.Delay(100, Ct);
        Assert.Single(h.Posted);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpUnavailable" or "probeExisting");

        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.WaitPosted(2);
        var probe = await h.WaitLog("probeExisting");

        Assert.Equal(new CdpLost(AppId), h.Posted[1]);
        var warn = Assert.Single(h.Log.Entries, e => e.Msg == "cdpUnavailable");
        Assert.Equal("notConnected", warn.Data!["reason"]);
        Assert.Equal(true, warn.Data["adopted"]);
        Assert.Equal("adopted", probe.Data!["outcome"]);
        Assert.False(orphan.HasExited);

        h.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100, Ct);
        Assert.Equal(2, h.Posted.Count);
        Assert.Equal(0, orphan.KillCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Adopted_cdp_cut_by_stop_or_process_exit_posts_no_cdp_lost(bool stop)
    {
        using var h = new Harness(connect: async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        var orphan = h.AddOrphan();
        h.Host.Probe(new ProbeExisting(1, AppId));
        await h.WaitPosted(1);
        await Eventually(() => h.ConnectCalls == 1);

        if (stop)
        {
            h.Host.Stop(new StopApp(8, AppId));
        }
        else
        {
            orphan.Exit(0);
        }

        await h.WaitLog("probeExisting");
        h.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100, Ct);

        IOrchestratorMessage[] expected = stop
            ? [OrphanAttached, new ProcessExited(AppId), new CommandCompleted(8)]
            : [OrphanAttached, new ProcessExited(AppId)];
        Assert.Equal(expected, h.Posted);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpUnavailable" or "cdpLost");
        var abandoned = Assert.Single(h.Log.Entries, e => e.Msg == "cdpAbandoned");
        Assert.Equal(true, abandoned.Data!["adopted"]);
    }

    [Fact]
    public async Task Adopted_setup_error_is_cdp_unavailable_and_cdp_lost()
    {
        using var h = new Harness();
        h.Transport.FailMethod = "Page.enable";

        await Adopt(h);

        Assert.Equal([OrphanAttached, new CdpLost(AppId)], h.Posted);
        var warn = Assert.Single(h.Log.Entries, e => e.Msg == "cdpUnavailable");
        Assert.Equal("setupFailed", warn.Data!["reason"]);
        Assert.Equal(true, warn.Data["adopted"]);
        Assert.Equal(["Runtime.addBinding", "Page.enable"], h.Transport.Methods());
        Assert.True(h.Transport.Disposed);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpConnected");
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("manifest")]
    public async Task Missing_page_asset_on_adoption_is_cdp_lost_without_connecting(string missing)
    {
        using var h = new Harness();
        var path = missing == "runtime" ? RuntimePath : ManifestPath;
        h.Files.TryRemove(path, out _);

        await Adopt(h);

        Assert.Equal([OrphanAttached, new CdpLost(AppId)], h.Posted);
        var warn = Assert.Single(h.Log.Entries, e => e.Msg == "cdpUnavailable");
        Assert.Equal("pageAssetMissing", warn.Data!["reason"]);
        Assert.Equal(path, warn.Data["file"]);
        Assert.Equal(0, h.ConnectCalls);
    }

    [Fact]
    public async Task Adopted_session_reconnects_after_a_drop_to_the_same_endpoint()
    {
        using var h = new Harness();
        var second = new FakeTransport { Endpoint = OtherTarget };
        h.Reconnects.Enqueue(second);
        await Adopt(h);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        var reconnected = await h.WaitLog("cdpReconnected");

        Assert.Equal("cdpClosed", reconnected.Data!["trigger"]);
        Assert.Equal(1, reconnected.Data["attempts"]);
        Assert.Equal([h.Transport.Endpoint], h.ReconnectEndpoints);
        Assert.Equal(InstallSetup, second.Methods());
        Assert.Equal([OrphanAttached], h.Posted);
    }

    [Fact]
    public async Task Adopted_session_drop_without_recovery_is_cdp_lost_at_30_seconds()
    {
        using var h = new Harness();
        var orphan = await Adopt(h);

        await Drop(h, h.Transport);
        h.Time.Advance(TimeSpan.FromMilliseconds(29_900));
        await Task.Delay(100, Ct);
        Assert.Single(h.Posted);

        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.WaitPosted(2);

        Assert.Equal(new CdpLost(AppId), h.Posted[1]);
        var lost = await h.WaitLog("cdpLost");
        Assert.Equal("cdpClosed", lost.Data!["trigger"]);
        Assert.False(orphan.HasExited);
    }

    [Fact]
    public async Task Stop_of_an_adopted_session_closes_the_window_kills_the_tree_and_reports_exit()
    {
        using var h = new Harness();
        var orphan = h.AddOrphan();
        orphan.IgnoreClose = true;
        h.Host.Probe(new ProbeExisting(1, AppId));
        await h.WaitLog("probeExisting");

        h.Host.Stop(new StopApp(8, AppId));
        await Eventually(() => orphan.TimedWaits == 1);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await h.WaitPosted(3);
        await Task.Delay(100, Ct);

        Assert.Equal([OrphanAttached, new ProcessExited(AppId), new CommandCompleted(8)], h.Posted);
        Assert.Equal(1, orphan.CloseCalls);
        Assert.Equal(1, orphan.KillCalls);
        Assert.True(orphan.HasExited);
        Assert.True(h.Transport.Disposed);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpClosed" or "processExited");
    }

    [Fact]
    public async Task Start_during_the_probe_waits_for_it_and_reuses_the_adopted_session_without_a_launch()
    {
        var connect = new TaskCompletionSource<ICdpTransport?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var h = new Harness(connect: (_, token) => connect.Task.WaitAsync(token));
        h.AddOrphan();
        h.Host.Probe(new ProbeExisting(1, AppId));
        await h.WaitPosted(1);
        await Eventually(() => h.ConnectCalls == 1);

        h.Host.Start(new StartApp(7, AppId));
        await Task.Delay(200, Ct);
        Assert.Equal([OrphanAttached], h.Posted);

        connect.SetResult(h.Transport);
        await h.WaitPosted(3);
        await Task.Delay(100, Ct);

        Assert.Equal([OrphanAttached, OrphanAttached, new CommandCompleted(7)], h.Posted);
        Assert.Empty(h.Launcher.Starts);
        Assert.Empty(h.ProfileChecks);
        Assert.Empty(h.PortChecks);
        Assert.Equal(InstallSetup, h.Transport.Methods());
        var reused = Assert.Single(h.Log.Entries, e => e.Msg == "startReusedAdopted");
        Assert.Equal(AppId, reused.Data!["appId"]);
        Assert.Equal(Harness.OrphanPid, reused.Data["rootPid"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "appLaunch" or "browserStartFailed");
    }

    [Fact]
    public async Task Start_after_an_adoption_without_cdp_stops_the_orphan_and_launches_as_before()
    {
        using var h = new Harness();
        var orphan = h.AddOrphan(commandLine: Harness.BrowserLine(cdpPort: null));
        h.Host.Probe(new ProbeExisting(1, AppId));
        await h.WaitLog("probeExisting");

        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(4);

        Assert.Equal(
            [OrphanAttached, new CdpLost(AppId), new AppWindowAttached(AppId, 0x1234, 4242), new CommandCompleted(7)],
            h.Posted);
        Assert.True(orphan.HasExited);
        Assert.Equal(1, orphan.CloseCalls);
        Assert.Single(h.Launcher.Starts);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "startReusedAdopted" or "processExited");
    }

    [Fact]
    public async Task Start_after_an_adopted_session_lost_cdp_stops_the_orphan_and_launches_instead_of_reusing()
    {
        // Pierwsze połączenie (adopcja) dostaje Harness.Transport, start po CdpLost świeży transport.
        var fresh = new FakeTransport();
        Harness? harness = null;
        var connects = 0;
        using var h = harness = new Harness(connect: (_, _) =>
            Task.FromResult<ICdpTransport?>(Interlocked.Increment(ref connects) == 1 ? harness!.Transport : fresh));
        var orphan = await Adopt(h);
        await Drop(h, h.Transport);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.WaitPosted(2);
        Assert.Equal(new CdpLost(AppId), h.Posted[1]);

        // Sesja jest gotowa i ma żywy korzeń, ale nie ma kanału CDP: Start idzie ścieżką 6.4.
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(4);

        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandCompleted(7)], h.Posted.Skip(2));
        Assert.True(orphan.HasExited);
        Assert.Equal(1, orphan.CloseCalls);
        Assert.Single(h.Launcher.Starts);
        Assert.Equal(StartSetup, fresh.Methods());
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "startReusedAdopted");
        fresh.Dispose();
    }

    [Fact]
    public async Task Second_renew_after_a_successful_renew_renews_again()
    {
        using var h = new Harness();
        var second = new FakeTransport();
        var third = new FakeTransport { Endpoint = OtherTarget };
        h.Reconnects.Enqueue(second);
        h.Reconnects.Enqueue(third);
        await Adopt(h);

        var firstAt = h.Time.GetUtcNow();
        h.Host.Renew(new RenewSession(2, AppId));
        await h.WaitLog("renewSession");
        await StepToAttempt(h, firstAt, 250, 1);
        await Eventually(() => h.Log.Entries.Count(e => e.Msg == "cdpReconnected") == 1);

        await AssertRenewsAgain(h, second, third, attemptsSoFar: 1);
    }

    [Fact]
    public async Task Renew_after_a_recovered_drop_renews_again()
    {
        using var h = new Harness();
        var second = new FakeTransport();
        var third = new FakeTransport { Endpoint = OtherTarget };
        h.Reconnects.Enqueue(second);
        h.Reconnects.Enqueue(third);
        await Adopt(h);

        var closedAt = await Drop(h, h.Transport);
        await StepToAttempt(h, closedAt, 250, 1);
        Assert.Equal("cdpClosed", (await h.WaitLog("cdpReconnected")).Data!["trigger"]);

        await AssertRenewsAgain(h, second, third, attemptsSoFar: 1);
    }

    /// <summary>Po udanym reconnekcie flaga <c>Reconnecting</c> jest zdjęta: kolejne odświeżenie zamyka bieżący kanał i łączy trzeci.</summary>
    private static async Task AssertRenewsAgain(Harness h, FakeTransport current, FakeTransport next, int attemptsSoFar)
    {
        var renewsBefore = h.Log.Entries.Count(e => e.Msg == "renewSession");
        var reconnectsBefore = h.Log.Entries.Count(e => e.Msg == "cdpReconnected");
        var renewedAt = h.Time.GetUtcNow();

        h.Host.Renew(new RenewSession(3, AppId));
        await Eventually(() => h.Log.Entries.Count(e => e.Msg == "renewSession") == renewsBefore + 1);
        Assert.Equal("renewing", h.Log.Entries.Last(e => e.Msg == "renewSession").Data!["outcome"]);
        Assert.True(current.Disposed);

        await StepToAttempt(h, renewedAt, 250, attemptsSoFar + 1);
        await Eventually(() => h.Log.Entries.Count(e => e.Msg == "cdpReconnected") == reconnectsBefore + 1);

        Assert.Equal("renewSession", h.Log.Entries.Last(e => e.Msg == "cdpReconnected").Data!["trigger"]);
        Assert.Equal(InstallSetup, next.Methods());
        Assert.All(h.ReconnectEndpoints, endpoint => Assert.Equal(h.Transport.Endpoint, endpoint));
        Assert.Equal([OrphanAttached], h.Posted);
    }

    [Fact]
    public async Task Renew_reconnects_the_adopted_session_to_its_endpoint_on_the_reconnect_schedule()
    {
        using var h = new Harness();
        var second = new FakeTransport { Endpoint = OtherTarget };
        h.Reconnects.Enqueue(null);
        h.Reconnects.Enqueue(second);
        await Adopt(h);

        var renewedAt = h.Time.GetUtcNow();
        h.Host.Renew(new RenewSession(2, AppId));
        var renew = await h.WaitLog("renewSession");
        Assert.Equal(LogLevel.Info, renew.Level);
        Assert.Equal("renewing", renew.Data!["outcome"]);
        Assert.True(h.Transport.Disposed);

        await StepToAttempt(h, renewedAt, 250, 1);
        await StepToAttempt(h, renewedAt, 750, 2);
        var reconnected = await h.WaitLog("cdpReconnected");

        Assert.Equal("renewSession", reconnected.Data!["trigger"]);
        Assert.Equal(2, reconnected.Data["attempts"]);
        Assert.Equal(750L, reconnected.Data["elapsedMs"]);
        Assert.Equal([renewedAt.AddMilliseconds(250), renewedAt.AddMilliseconds(750)], h.ReconnectAt);
        Assert.All(h.ReconnectEndpoints, endpoint => Assert.Equal(h.Transport.Endpoint, endpoint));
        Assert.Equal(InstallSetup, second.Methods());
        Assert.Equal(InstallSetup, h.Transport.Methods());
        Assert.Equal([OrphanAttached], h.Posted);

        // Zamknięty stary kanał nie zaczyna drugiej pętli (Detach zwraca dla niego false).
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpClosed");
        Assert.Equal(2, h.ReconnectAttempts);
        Assert.DoesNotContain(h.Log.Entries, e => e.Level is LogLevel.Warn or LogLevel.Error);

        second.Push(MainFrameNavigated);
        second.PushBinding("__msBridge", HelloPayload);
        await h.WaitLog("pageHello");
        await Eventually(() => second.Methods().Length == 4);
    }

    [Fact]
    public async Task Renew_of_a_started_session_reconnects_with_the_renew_trigger()
    {
        using var h = new Harness();
        var second = new FakeTransport();
        h.Reconnects.Enqueue(second);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var renewedAt = h.Time.GetUtcNow();
        h.Host.Renew(new RenewSession(2, AppId));
        Assert.Equal("renewing", (await h.WaitLog("renewSession")).Data!["outcome"]);
        await StepToAttempt(h, renewedAt, 250, 1);
        var reconnected = await h.WaitLog("cdpReconnected");

        Assert.Equal("renewSession", reconnected.Data!["trigger"]);
        Assert.Equal(StartSetup, h.Transport.Methods());
        Assert.Equal(InstallSetup, second.Methods());
        Assert.Equal(2, h.Posted.Count);
    }

    [Fact]
    public async Task Renew_that_never_reconnects_is_cdp_lost_exactly_at_the_reconnect_timeout()
    {
        using var h = new Harness();
        await Adopt(h);

        var renewedAt = h.Time.GetUtcNow();
        h.Host.Renew(new RenewSession(2, AppId));
        await h.WaitLog("renewSession");
        var schedule = NeverBackScheduleMs;
        for (var i = 0; i < schedule.Length; i++)
        {
            await StepToAttempt(h, renewedAt, schedule[i], i + 1);
        }

        Assert.Equal(schedule.Select(ms => renewedAt.AddMilliseconds(ms)), h.ReconnectAt);
        h.Time.Advance(renewedAt.AddMilliseconds(29_900) - h.Time.GetUtcNow());
        await Task.Delay(100, Ct);
        Assert.Single(h.Posted);

        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.WaitPosted(2);

        Assert.Equal(new CdpLost(AppId), h.Posted[1]);
        var lost = await h.WaitLog("cdpLost");
        Assert.Equal("renewSession", lost.Data!["trigger"]);
        Assert.Equal(17, lost.Data["attempts"]);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "cdpReconnected" or "cdpClosed");
    }

    [Fact]
    public async Task Renew_while_the_start_is_in_progress_is_not_ready()
    {
        using var h = new Harness(connect: async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(1);
        await Eventually(() => h.ConnectCalls == 1);

        await AssertRenewSkipped(h, "notReady", posted: 1);
    }

    [Fact]
    public async Task Renew_during_a_reconnect_is_skipped_and_starts_no_second_loop()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);
        var closedAt = await Drop(h, h.Transport);

        await AssertRenewSkipped(h, "reconnecting", posted: 2);
        await StepToAttempt(h, closedAt, 250, 1);
        await StepToAttempt(h, closedAt, 750, 2);
    }

    [Fact]
    public async Task Renew_after_cdp_lost_has_no_channel_and_after_stop_no_session()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);
        await Drop(h, h.Transport);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.WaitPosted(3);
        await h.WaitLog("cdpLost");

        await AssertRenewSkipped(h, "noCdp", posted: 3);

        h.Host.Stop(new StopApp(8, AppId));
        await h.WaitPosted(5);
        h.Host.Renew(new RenewSession(3, AppId));
        await Eventually(() => h.Log.Entries.Count(e => e.Msg == "renewSession") == 2);
        Assert.Equal("noSession", h.Log.Entries.Last(e => e.Msg == "renewSession").Data!["outcome"]);
        Assert.Equal(5, h.Posted.Count);
    }

    [Fact]
    public async Task Stale_traffic_from_the_channel_replaced_by_renew_is_ignored()
    {
        using var h = new Harness();
        // Zwolniony stary kanał dalej dostarcza zdarzenia (wiadomość w drodze w chwili odświeżenia).
        h.Transport.DeliverAfterDispose = true;
        var second = new FakeTransport();
        h.Reconnects.Enqueue(second);
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        var renewedAt = h.Time.GetUtcNow();
        h.Host.Renew(new RenewSession(2, AppId));
        await h.WaitLog("renewSession");
        Assert.True(h.Transport.Disposed);
        await StepToAttempt(h, renewedAt, 250, 1);
        await h.WaitLog("cdpReconnected");

        var delivered = h.Transport.Delivered;
        h.Transport.Push(MainFrameNavigated);
        h.Transport.PushBinding("__msBridge", HelloPayload);
        await Eventually(() => h.Transport.Delivered >= delivered + 2);
        await Task.Delay(200, Ct);

        Assert.Equal(StartSetup, h.Transport.Methods());
        Assert.Equal(InstallSetup, second.Methods());
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg is "pageHello" or "pageMessageRejected" or "bindingRefreshFailed" or "cdpClosed");
        Assert.Equal(2, h.Posted.Count);
        h.Transport.Close();
        await Task.Delay(100, Ct);
        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "cdpClosed");
    }

    /// <summary>Odświeżenie pominięte z danym powodem: nic nie wysłane, nic nie zgłoszone, żadnej nowej próby.</summary>
    private static async Task AssertRenewSkipped(Harness h, string outcome, int posted)
    {
        var methods = h.Transport.Methods();
        var attempts = h.ReconnectAttempts;
        var before = h.Log.Entries.Count(e => e.Msg == "renewSession");

        h.Host.Renew(new RenewSession(2, AppId));
        await Eventually(() => h.Log.Entries.Count(e => e.Msg == "renewSession") == before + 1);
        await Task.Delay(100, Ct);

        var renew = h.Log.Entries.Last(e => e.Msg == "renewSession");
        Assert.Equal(LogLevel.Info, renew.Level);
        Assert.Equal(outcome, renew.Data!["outcome"]);
        Assert.Equal(methods, h.Transport.Methods());
        Assert.Equal(attempts, h.ReconnectAttempts);
        Assert.Equal(posted, h.Posted.Count);
    }

    // ── Story 6.5: spóźnione fakty hosta na prawdziwym Orchestratorze ────────

    [Fact]
    public async Task Probe_adoption_on_the_orchestrator_is_background_with_mute_minimize_and_shell()
    {
        var orchestrator = OrchestratorTestSupport.Create();
        var probe = orchestrator.Handle(new OrchestratorStarted()).Commands.OfType<ProbeExisting>().Single(p => p.AppId == AppId);
        using var h = new Harness();
        h.AddOrphan();
        h.Host.Probe(probe);
        await h.WaitLog("probeExisting");

        var adopted = orchestrator.Handle(Assert.Single(h.Posted));

        Assert.Equal(AppLifecycleState.Background, OrchestratorTestSupport.StateOf(adopted.Snapshot!, AppId));
        var commands = adopted.Commands.ToList();
        var mute = commands.FindIndex(c => c is MuteProcessTree { RootPid: Harness.OrphanPid });
        var minimize = commands.FindIndex(c => c is MinimizeWindow m && m.Hwnd == Harness.OrphanHwnd);
        var shell = commands.FindIndex(c => c is ShowShell);
        Assert.True(mute >= 0 && mute < minimize && minimize < shell);
        Assert.DoesNotContain(commands, c => c is ActivateWindow);
    }

    [Fact]
    public async Task Adoption_after_another_app_was_launched_mutes_and_minimizes_without_showing_the_shell()
    {
        var (orchestrator, _) = OrchestratorTestSupport.Started();
        Assert.Contains(orchestrator.Handle(new LaunchApp("plex")).Commands, c => c is StartApp);
        using var h = new Harness();
        await Adopt(h);

        var adopted = orchestrator.Handle(Assert.Single(h.Posted));

        Assert.Equal(AppLifecycleState.Background, OrchestratorTestSupport.StateOf(adopted.Snapshot!, AppId));
        Assert.Contains(adopted.Commands, c => c is MuteProcessTree { RootPid: Harness.OrphanPid });
        Assert.Contains(adopted.Commands, c => c is MinimizeWindow m && m.Hwnd == Harness.OrphanHwnd);
        Assert.DoesNotContain(adopted.Commands, c => c is ShowShell or ActivateWindow);
    }

    [Fact]
    public async Task Adoption_after_a_wake_is_background_without_activation()
    {
        var (orchestrator, _) = OrchestratorTestSupport.Started();
        orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));
        using var h = new Harness();
        await Adopt(h);

        var adopted = orchestrator.Handle(Assert.Single(h.Posted));

        Assert.Equal(AppLifecycleState.Background, OrchestratorTestSupport.StateOf(adopted.Snapshot!, AppId));
        Assert.Contains(adopted.Commands, c => c is MuteProcessTree);
        Assert.DoesNotContain(adopted.Commands, c => c is ActivateWindow);
    }

    [Fact]
    public async Task Start_during_the_probe_on_the_orchestrator_gives_one_foreground_with_the_adopted_window()
    {
        var orchestrator = OrchestratorTestSupport.Create();
        var probe = orchestrator.Handle(new OrchestratorStarted()).Commands.OfType<ProbeExisting>().Single(p => p.AppId == AppId);
        var connect = new TaskCompletionSource<ICdpTransport?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var h = new Harness(connect: (_, token) => connect.Task.WaitAsync(token));
        h.AddOrphan();
        h.Host.Probe(probe);
        await h.WaitPosted(1);
        await Eventually(() => h.ConnectCalls == 1);

        // Kafelek wybrany, zanim Orchestrator zobaczył adopcję: StartApp idzie do hosta w trakcie sondy.
        var start = Assert.Single(orchestrator.Handle(new LaunchApp(AppId)).Commands.OfType<StartApp>());
        h.Host.Start(start);
        connect.SetResult(h.Transport);
        await h.WaitPosted(3);

        var results = h.Posted.Select(orchestrator.Handle).ToList();

        Assert.Equal([OrphanAttached, OrphanAttached, new CommandCompleted(start.CmdId)], h.Posted);
        var activations = results.SelectMany(r => r.Commands).OfType<ActivateWindow>().ToList();
        Assert.Equal(Harness.OrphanHwnd, Assert.Single(activations).Hwnd);
        Assert.Equal(AppLifecycleState.Foreground, OrchestratorTestSupport.StateOf(results[0].Snapshot!, AppId));
        Assert.Empty(results[1].Commands);
        Assert.Null(results[1].Snapshot);
        Assert.Empty(h.Launcher.Starts);
    }

    [Fact]
    public async Task Wake_renews_the_adopted_session_and_a_later_cdp_lost_fails_the_tile()
    {
        var orchestrator = OrchestratorTestSupport.Create();
        var probe = orchestrator.Handle(new OrchestratorStarted()).Commands.OfType<ProbeExisting>().Single(p => p.AppId == AppId);
        using var h = new Harness();
        h.AddOrphan();
        h.Host.Probe(probe);
        await h.WaitLog("probeExisting");
        orchestrator.Handle(Assert.Single(h.Posted));

        var resumed = orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));
        var renew = Assert.Single(resumed.Commands.OfType<RenewSession>());
        Assert.Equal(AppId, renew.AppId);
        h.Host.Renew(renew);
        Assert.Equal("renewing", (await h.WaitLog("renewSession")).Data!["outcome"]);

        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.WaitPosted(2);
        var failed = orchestrator.Handle(Assert.IsType<CdpLost>(h.Posted[1]));

        Assert.Equal(AppLifecycleState.Failed, OrchestratorTestSupport.StateOf(failed.Snapshot!, AppId));
        Assert.Equal("renewSession", (await h.WaitLog("cdpLost")).Data!["trigger"]);
    }

    [Fact]
    public async Task Starting_window_attached_after_home_gets_compensation_not_foreground()
    {
        var (orchestrator, _) = OrchestratorTestSupport.Started();
        var start = Assert.Single(orchestrator.Handle(new LaunchApp(AppId)).Commands.OfType<StartApp>());
        var home = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1));
        Assert.Contains(home.Commands, c => c is ShowShell);
        using var h = new Harness();

        h.Host.Start(start);
        await h.WaitPosted(2);
        var attached = orchestrator.Handle(h.Posted[0]);

        Assert.Equal(AppLifecycleState.Background, OrchestratorTestSupport.StateOf(attached.Snapshot!, AppId));
        Assert.Contains(attached.Commands, c => c is MuteProcessTree { RootPid: 4242 });
        Assert.Contains(attached.Commands, c => c is MinimizeWindow { Hwnd: 0x1234 });
        Assert.DoesNotContain(attached.Commands, c => c is ActivateWindow);
    }

    // ── Story 6.7: J4 — restart aplikacji Failed jednym OK ────────────────────

    [Theory]
    [InlineData("cdpLost")]
    [InlineData("pageVersionRejected")]
    public async Task J4_restart_from_failed_kills_the_old_tree_and_brings_a_new_root_to_the_foreground(string failure)
    {
        var transports = new ConcurrentQueue<FakeTransport>();
        using var h = new Harness(connect: FreshTransports(transports));
        var pump = new J4Pump(h);
        await pump.Launch();
        await FailLive(h, pump, transports.Last(), failure);
        Assert.Equal(AppLifecycleState.Failed, pump.State);

        var restart = pump.Handle(new RestartApp(AppId));

        Assert.Null(restart.IgnoredIntent);
        Assert.Collection(
            restart.Commands,
            command => Assert.Equal(4242, Assert.IsType<UnmuteProcessTree>(command).RootPid),
            command => Assert.Equal(AppId, Assert.IsType<StopApp>(command).AppId),
            command => Assert.IsType<ScheduleTimer>(command));
        var stop = restart.Commands.OfType<StopApp>().Single();
        var results = await pump.Feed(7);

        // Stop hosta: ProcessExited i CommandCompleted; dokładnie jeden StartApp, potem nowy korzeń na pierwszym planie.
        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(stop.CmdId)], h.Posted.Skip(3).Take(2));
        var start = Assert.Single(results.SelectMany(result => result.Commands).OfType<StartApp>());
        Assert.Equal([new AppWindowAttached(AppId, 0x1235, 4243), new CommandCompleted(start.CmdId)], h.Posted.Skip(5));
        Assert.Contains(results[2].Commands, command => command is ActivateWindow { Hwnd: 0x1235 });
        Assert.Equal(
            [
                AppLifecycleState.Starting, AppLifecycleState.Foreground, AppLifecycleState.Failed,
                AppLifecycleState.Stopping, AppLifecycleState.Starting, AppLifecycleState.Foreground,
            ],
            pump.States);
        Assert.True(h.Process.HasExited);
        Assert.Equal(1, h.Process.KillCalls);
        Assert.Equal(2, h.Launcher.Starts.Count);
        Assert.False(h.Launcher.Processes[1].HasExited);
        Assert.True(h.ProfileChecks[^1].FirstExited);
        await pump.AssertQuiet();
    }

    [Fact]
    public async Task J4_restart_refused_by_a_held_profile_fails_again_without_a_new_launch()
    {
        var transports = new ConcurrentQueue<FakeTransport>();
        using var h = new Harness(connect: FreshTransports(transports));
        var pump = new J4Pump(h);
        await pump.Launch();
        await FailLive(h, pump, transports.Last(), "pageVersionRejected");

        h.ProfileHeld = true;
        pump.Handle(new RestartApp(AppId));
        var results = await pump.Feed(6);

        var start = Assert.Single(results.SelectMany(result => result.Commands).OfType<StartApp>());
        Assert.Equal(new CommandFailed(start.CmdId, ErrorCode.ProfileInUse), h.Posted[5]);
        Assert.Equal("profileInUse", StartFailure(h).Data!["reason"]);
        Assert.Equal(
            [
                AppLifecycleState.Starting, AppLifecycleState.Foreground, AppLifecycleState.Failed,
                AppLifecycleState.Stopping, AppLifecycleState.Starting, AppLifecycleState.Failed,
            ],
            pump.States);
        Assert.Single(h.Launcher.Starts);
        Assert.True(h.Process.HasExited);
        await pump.AssertQuiet();
    }

    /// <summary>
    /// Nowy proces restartu ginie. Przed oknem prawdziwy host zgłasza to jako porażkę startu (<c>launcherExited</c>,
    /// bez <see cref="ProcessExited"/>, bo nie ma jeszcze obserwatora wyjścia); po oknie, w trakcie łączenia CDP,
    /// jako <see cref="ProcessExited"/>. Oba fakty kończą się w <c>Failed</c> bez kolejnego uruchomienia.
    /// </summary>
    [Theory]
    [InlineData("beforeWindow")]
    [InlineData("afterWindow")]
    public async Task J4_restarted_process_that_dies_fails_the_tile_without_another_launch(string when)
    {
        var transports = new ConcurrentQueue<FakeTransport>();
        var fresh = FreshTransports(transports);
        Harness? harness = null;
        using var h = harness = new Harness(
            findWindow: when == "beforeWindow"
                ? pid =>
                {
                    if (pid == Window.Pid)
                    {
                        return Window;
                    }

                    // Nowy korzeń restartu kończy się, zanim pokaże okno.
                    harness!.Launcher.Processes.Single(process => process.Id == pid).Exit(3);
                    return null;
                }
                : null,
            connect: when == "beforeWindow"
                ? fresh
                : async (port, token) =>
                {
                    if (transports.IsEmpty)
                    {
                        return await fresh(port, token);
                    }

                    // Drugie łączenie wisi, aż wyjście procesu je anuluje.
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return null;
                });
        var pump = new J4Pump(h);
        await pump.Launch();
        await FailLive(h, pump, transports.Last(), "pageVersionRejected");

        pump.Handle(new RestartApp(AppId));
        if (when == "beforeWindow")
        {
            await pump.Feed(6);

            Assert.IsType<CommandFailed>(h.Posted[5]);
            var failure = StartFailure(h);
            Assert.Equal("launcherExited", failure.Data!["reason"]);
            Assert.Equal(
                [
                    AppLifecycleState.Starting, AppLifecycleState.Foreground, AppLifecycleState.Failed,
                    AppLifecycleState.Stopping, AppLifecycleState.Starting, AppLifecycleState.Failed,
                ],
                pump.States);
        }
        else
        {
            await pump.Feed(6);
            Assert.Equal(new AppWindowAttached(AppId, 0x1235, 4243), h.Posted[5]);
            h.Launcher.Processes[1].Exit(3);
            var results = await pump.Feed(8);

            Assert.Equal(new ProcessExited(AppId), h.Posted[6]);
            Assert.IsType<CommandFailed>(h.Posted[7]);
            Assert.Contains(results[0].Commands, command => command is ForgetProcessTree { RootPid: 4243 });
            Assert.Contains(results[0].Commands, command => command is ShowShell);
            Assert.Equal(
                [
                    AppLifecycleState.Starting, AppLifecycleState.Foreground, AppLifecycleState.Failed,
                    AppLifecycleState.Stopping, AppLifecycleState.Starting, AppLifecycleState.Foreground,
                    AppLifecycleState.Failed,
                ],
                pump.States);
        }

        Assert.Equal(2, h.Launcher.Starts.Count);
        Assert.True(h.Launcher.Processes[1].HasExited);
        await pump.AssertQuiet();
    }

    [Fact]
    public async Task J4_restarted_app_that_fails_again_hides_behind_the_shell_with_focus_on_its_tile()
    {
        var transports = new ConcurrentQueue<FakeTransport>();
        using var h = new Harness(connect: FreshTransports(transports));
        var pump = new J4Pump(h);
        await pump.Launch();
        await FailLive(h, pump, transports.Last(), "pageVersionRejected");
        pump.Handle(new RestartApp(AppId));
        await pump.Feed(7);
        Assert.Equal(AppLifecycleState.Foreground, pump.State);

        var failed = await FailLive(h, pump, transports.Last(), "pageVersionRejected");

        Assert.Equal(new PageVersionRejected(AppId, 2), h.Posted[7]);
        Assert.Contains(failed.Commands, command => command is MuteProcessTree { RootPid: 4243 });
        Assert.Contains(failed.Commands, command => command is MinimizeWindow { Hwnd: 0x1235 });
        Assert.Contains(failed.Commands, command => command is ShowShell);
        Assert.Equal(AppId, failed.Snapshot!.FocusRequest!.Target);
        Assert.Equal(
            [
                AppLifecycleState.Starting, AppLifecycleState.Foreground, AppLifecycleState.Failed,
                AppLifecycleState.Stopping, AppLifecycleState.Starting, AppLifecycleState.Foreground,
                AppLifecycleState.Failed,
            ],
            pump.States);

        // Proces żyje (to J4 „Nie odpowiada”): o zamknięciu zdecyduje dopiero AppMenu.
        Assert.False(h.Launcher.Processes[1].HasExited);
        await pump.AssertQuiet();
    }

    [Fact]
    public async Task J4_three_failed_restarts_never_escalate_and_a_fourth_reaches_the_foreground()
    {
        var transports = new ConcurrentQueue<FakeTransport>();
        using var h = new Harness(connect: FreshTransports(transports));
        var pump = new J4Pump(h);
        await pump.Launch();
        await FailLive(h, pump, transports.Last(), "pageVersionRejected");

        for (var i = 0; i < 3; i++)
        {
            var restart = pump.Handle(new RestartApp(AppId));
            Assert.Null(restart.IgnoredIntent);
            Assert.Equal(AppLifecycleState.Stopping, OrchestratorTestSupport.StateOf(restart.Snapshot!, AppId));
            await pump.Feed(pump.Fed + 4);
            Assert.Equal(AppLifecycleState.Foreground, pump.State);

            var failed = await FailLive(h, pump, transports.Last(), "pageVersionRejected");
            var tile = Assert.Single(failed.Snapshot!.Apps, app => app.AppId == AppId);
            Assert.Equal(AppLifecycleState.Failed, tile.State);
        }

        var last = pump.Handle(new RestartApp(AppId));
        Assert.Null(last.IgnoredIntent);
        await pump.Feed(pump.Fed + 4);

        Assert.Equal(AppLifecycleState.Foreground, pump.State);
        Assert.Equal(5, h.Launcher.Starts.Count);
        Assert.Equal([4242, 4243, 4244, 4245, 4246], h.Launcher.Processes.Select(process => process.Id));
        Assert.All(h.Launcher.Processes.Take(4), process =>
        {
            Assert.True(process.HasExited);
            Assert.Equal(1, process.KillCalls);
        });
        Assert.False(h.Launcher.Processes[4].HasExited);
        Assert.Equal(new AppWindowAttached(AppId, 0x1234 + 4, 4246), h.Posted[^2]);
        AppLifecycleState[] cycle = [AppLifecycleState.Stopping, AppLifecycleState.Starting, AppLifecycleState.Foreground];
        Assert.Equal(
            [
                AppLifecycleState.Starting, AppLifecycleState.Foreground, AppLifecycleState.Failed,
                .. cycle, AppLifecycleState.Failed,
                .. cycle, AppLifecycleState.Failed,
                .. cycle, AppLifecycleState.Failed,
                .. cycle,
            ],
            pump.States);
        await pump.AssertQuiet();
    }

    [Fact]
    public async Task J4_close_after_a_failed_restart_kills_the_tree_and_launches_nothing()
    {
        var transports = new ConcurrentQueue<FakeTransport>();
        using var h = new Harness(connect: FreshTransports(transports));
        var pump = new J4Pump(h);
        await pump.Launch();
        await FailLive(h, pump, transports.Last(), "pageVersionRejected");
        pump.Handle(new RestartApp(AppId));
        await pump.Feed(7);
        await FailLive(h, pump, transports.Last(), "pageVersionRejected");

        var close = pump.Handle(new CloseApp(AppId));
        var stop = Assert.Single(close.Commands.OfType<StopApp>());
        var results = await pump.Feed(10);

        Assert.Equal([new ProcessExited(AppId), new CommandCompleted(stop.CmdId)], h.Posted.Skip(8));
        Assert.DoesNotContain(results.SelectMany(result => result.Commands), command => command is StartApp);
        Assert.Equal(
            [
                AppLifecycleState.Starting, AppLifecycleState.Foreground, AppLifecycleState.Failed,
                AppLifecycleState.Stopping, AppLifecycleState.Starting, AppLifecycleState.Foreground,
                AppLifecycleState.Failed, AppLifecycleState.Stopping, AppLifecycleState.NotRunning,
            ],
            pump.States);
        Assert.True(h.Launcher.Processes[1].HasExited);
        Assert.Equal(1, h.Launcher.Processes[1].KillCalls);
        Assert.Equal(2, h.Launcher.Starts.Count);
        await pump.AssertQuiet();
    }

    /// <summary>Connector, który przy każdym łączeniu daje świeży transport (sesja restartu ma własny kanał CDP).</summary>
    private static Func<int, CancellationToken, Task<ICdpTransport?>> FreshTransports(ConcurrentQueue<FakeTransport> transports) =>
        (_, _) =>
        {
            var transport = new FakeTransport();
            transports.Enqueue(transport);
            return Task.FromResult<ICdpTransport?>(transport);
        };

    /// <summary>
    /// Awaria żywej sesji (proces zostaje): zerwane CDP bez powrotu aż do terminu reconnectu (<see cref="CdpLost"/>)
    /// albo <c>hello</c> z innym major <c>v</c> (<see cref="PageVersionRejected"/>). Zwraca wynik Orchestratora dla faktu.
    /// </summary>
    private static async Task<OrchestratorResult> FailLive(Harness h, J4Pump pump, FakeTransport transport, string failure)
    {
        var posted = pump.Fed;
        if (failure == "cdpLost")
        {
            var attempts = h.ReconnectAttempts;
            var closedAt = await Drop(h, transport);
            var schedule = NeverBackScheduleMs;
            for (var i = 0; i < schedule.Length; i++)
            {
                await StepToAttempt(h, closedAt, schedule[i], attempts + i + 1);
            }

            h.Time.Advance(closedAt.AddSeconds(30) - h.Time.GetUtcNow());
        }
        else
        {
            transport.PushBinding("__msBridge", HelloV2);
        }

        var results = await pump.Feed(posted + 1);
        Assert.Equal(failure == "cdpLost" ? new CdpLost(AppId) : (IOrchestratorMessage)new PageVersionRejected(AppId, 2), h.Posted[posted]);
        Assert.Equal(AppLifecycleState.Failed, pump.State);
        return Assert.Single(results);
    }

    // ── Granice AD-1 w repo ───────────────────────────────────────────────────

    [Fact]
    public void No_playwright_package_in_any_project_or_web_package()
    {
        var root = RepoRoot();
        var projects = new[] { "src", "tests", "tools" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*.csproj", SearchOption.AllDirectories))
            .Append(Path.Combine(root, "web", "package.json"))
            .ToList();

        Assert.Contains(projects, p => p.EndsWith("MediaStation.App.csproj", StringComparison.Ordinal));
        foreach (var project in projects)
        {
            Assert.DoesNotContain("playwright", File.ReadAllText(project), StringComparison.OrdinalIgnoreCase);
        }
    }

    // `--remote-allow-origins` nie może wystąpić nigdzie (także w komentarzu); pozostałe zakazy AD-1 sprawdzamy jako
    // literał tekstowy w kodzie (z otwierającym cudzysłowem), bo komentarze je wymieniają.
    [Theory]
    [InlineData("--remote-allow-origins")]
    [InlineData("\"--remote-debugging-address")]
    [InlineData("\"--enable-automation")]
    [InlineData("\"Runtime.evaluate\"")]
    public void Forbidden_cdp_literal_is_absent_from_app_sources(string literal)
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains(literal, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    // ── Pomocnicze ────────────────────────────────────────────────────────────

    private static LogEntry StartFailure(Harness h)
    {
        var entry = h.Log.Entries.Single(e => e.Msg == "browserStartFailed");
        Assert.Equal(LogLevel.Warn, entry.Level);
        return entry;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Warunek nie spełnił się w czasie.");
            }

            await Task.Delay(10, Ct);
        }
    }

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

    private sealed class Harness : IDisposable
    {
        public static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        private readonly ConcurrentQueue<IOrchestratorMessage> _posted = new();
        private readonly ConcurrentQueue<Uri> _reconnectEndpoints = new();
        private readonly ConcurrentQueue<DateTimeOffset> _reconnectAt = new();
        private readonly ConcurrentQueue<(string, bool)> _profileChecks = new();
        private readonly ConcurrentQueue<int> _portChecks = new();
        private readonly ConcurrentQueue<string> _versionReads = new();
        private readonly ConcurrentQueue<KeyValuePair<int, string>> _commandLines = new();
        private readonly ConcurrentDictionary<int, bool> _windowless = new();
        private readonly ConcurrentQueue<int> _opened = new();
        private int _connectCalls;
        private int _commandLineScans;

        public Harness(
            Func<BrowserKind, string?>? resolve = null,
            Func<int, WindowInfo?>? findWindow = null,
            Func<int, CancellationToken, Task<ICdpTransport?>>? connect = null,
            Func<Uri, CancellationToken, Task<ICdpTransport?>>? reconnect = null)
        {
            Files[RuntimePath] = "(config) => {}";
            Files[ManifestPath] = ManifestJson;
            Launcher = new FakeLauncher(Process);
            bool HasWindow(int pid) =>
                Launcher.Processes.Any(p => p.Id == pid) || (Orphans.ContainsKey(pid) && !_windowless.ContainsKey(pid));
            Host = new BrowserHost(
                Log,
                _posted.Enqueue,
                [Netflix],
                ProfilesDir,
                PageDir,
                TimeSpan.FromSeconds(15),
                // Jak domyślne `cdpReconnectTimeout` z Settings.
                TimeSpan.FromSeconds(30),
                resolve ?? (_ => Exe),
                Launcher,
                // Każdy uruchomiony (i osierocony z oknem) proces ma własne okno: 4242 → 0x1234, 4243 → 0x1235…
                findWindow ?? (pid => HasWindow(pid) ? Window with { Hwnd = Window.Hwnd + pid - Process.Id, Pid = pid } : null),
                (port, token) =>
                {
                    Interlocked.Increment(ref _connectCalls);
                    ConnectedPort = port;
                    return connect is null ? Task.FromResult<ICdpTransport?>(Transport) : connect(port, token);
                },
                (endpoint, token) =>
                {
                    // Próba jest liczona przed powrotem: test może przesunąć zegar, gdy tylko widzi jej numer.
                    _reconnectEndpoints.Enqueue(endpoint);
                    _reconnectAt.Enqueue(Time.GetUtcNow());
                    if (reconnect is not null)
                    {
                        return reconnect(endpoint, token);
                    }

                    return Task.FromResult<ICdpTransport?>(Reconnects.TryDequeue(out var next) ? next : null);
                },
                path => Files.TryGetValue(path, out var text) ? text : null,
                path =>
                {
                    _versionReads.Enqueue(path);
                    return ExeVersion;
                },
                dir =>
                {
                    _profileChecks.Enqueue((dir, Process.HasExited));
                    return ProfileHeld;
                },
                port =>
                {
                    _portChecks.Enqueue(port);
                    return PortHeld;
                },
                () =>
                {
                    Interlocked.Increment(ref _commandLineScans);
                    return CommandLinesThrow is { } thrown ? throw thrown : _commandLines.ToArray();
                },
                pid =>
                {
                    _opened.Enqueue(pid);
                    return Orphans.TryGetValue(pid, out var orphan) && !orphan.HasExited ? orphan : null;
                },
                Time);
        }

        /// <summary>PID korzenia przeglądarki, która przeżyła restart MediaStation (domyślnie: brak takich procesów).</summary>
        public const int OrphanPid = 7000;

        /// <summary>Okno osieroconego korzenia według reguły okien harnessu (0x1234 dla PID 4242); stała, bo pola statyczne obu klas odwołują się do siebie.</summary>
        public const long OrphanHwnd = 0x1234 + OrphanPid - 4242;

        /// <summary>Linia poleceń przeglądarki uruchomionej przez BrowserHost (AD-1) z danym portem CDP i profilem.</summary>
        public static string BrowserLine(int? cdpPort = 9222, string? profileDir = null) =>
            $"\"{Exe}\" \"--user-data-dir={profileDir ?? ProfileDir}\""
            + (cdpPort is { } port ? $" --remote-debugging-port={port}" : string.Empty)
            + " --no-first-run --no-default-browser-check --autoplay-policy=no-user-gesture-required --kiosk about:blank";

        /// <summary>Żywe procesy widoczne dla seamu otwierania procesu (adopcja).</summary>
        public ConcurrentDictionary<int, FakeProcess> Orphans { get; } = new();

        /// <summary>Wyjątek rzucany przez seam linii poleceń (padnięte WMI).</summary>
        public volatile Exception? CommandLinesThrow;

        /// <summary>Liczba odczytów linii poleceń (sonda nie jest ponawiana).</summary>
        public int CommandLineScans => Volatile.Read(ref _commandLineScans);

        public IReadOnlyList<int> Opened => _opened.ToArray();

        /// <summary>Dodaje żywy proces z linią poleceń (domyślnie korzeń z profilem i portem aplikacji) i oknem.</summary>
        public FakeProcess AddOrphan(int pid = OrphanPid, string? commandLine = null, bool window = true)
        {
            var process = new FakeProcess(pid);
            Orphans[pid] = process;
            if (!window)
            {
                _windowless[pid] = true;
            }

            _commandLines.Enqueue(new KeyValuePair<int, string>(pid, commandLine ?? BrowserLine()));
            return process;
        }

        /// <summary>Linia poleceń procesu bez obiektu procesu (np. cudzy profil).</summary>
        public void AddCommandLine(int pid, string commandLine) =>
            _commandLines.Enqueue(new KeyValuePair<int, string>(pid, commandLine));

        /// <summary><c>FileVersion</c> binarki zwracane przez seam (domyślnie zgodne z <c>product</c> transportu).</summary>
        public volatile string? ExeVersion = CurrentVersion;

        /// <summary>Profil trzymany przez inny proces (<c>lockfile</c>).</summary>
        public volatile bool ProfileHeld;

        /// <summary>Port CDP zajęty przez inny nasłuch.</summary>
        public volatile bool PortHeld;

        /// <summary>Kontrole profilu: katalog i to, czy pierwszy proces przeglądarki już się zakończył.</summary>
        public IReadOnlyList<(string Dir, bool FirstExited)> ProfileChecks => _profileChecks.ToArray();

        public IReadOnlyList<int> PortChecks => _portChecks.ToArray();

        public IReadOnlyList<string> VersionReads => _versionReads.ToArray();

        public FakeTimeProvider Time { get; } = new(T0);

        public RecordingLog Log { get; } = new();

        public ConcurrentDictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public FakeProcess Process { get; } = new();

        public FakeLauncher Launcher { get; }

        public FakeTransport Transport { get; } = new();

        public BrowserHost Host { get; }

        public int ConnectedPort { get; private set; }

        public int ConnectCalls => Volatile.Read(ref _connectCalls);

        /// <summary>Kolejne odpowiedzi seamu reconnectu: transport albo <c>null</c> (nieudana próba); pusta = <c>null</c>.</summary>
        public ConcurrentQueue<FakeTransport?> Reconnects { get; } = new();

        public IReadOnlyList<Uri> ReconnectEndpoints => _reconnectEndpoints.ToArray();

        public int ReconnectAttempts => _reconnectAt.Count;

        /// <summary>Chwile prób reconnectu na zegarze testu.</summary>
        public IReadOnlyList<DateTimeOffset> ReconnectAt => _reconnectAt.ToArray();

        public IReadOnlyList<IOrchestratorMessage> Posted => _posted.ToArray();

        public Task WaitPosted(int count) => Eventually(() => _posted.Count >= count);

        public async Task<LogEntry> WaitLog(string msg)
        {
            await Eventually(() => Log.Entries.Any(e => e.Msg == msg));
            return Log.Entries.First(e => e.Msg == msg);
        }

        public void Dispose()
        {
            Process.Exit(0);
            foreach (var process in Launcher.Processes)
            {
                process.Exit(0);
            }

            foreach (var orphan in Orphans.Values)
            {
                orphan.Exit(0);
            }

            Transport.Dispose();
            foreach (var transport in Reconnects)
            {
                transport?.Dispose();
            }
        }
    }

    /// <summary>
    /// Pompa J4 (story 6.7): prawdziwy Orchestrator i prawdziwy BrowserHost harnessu w jednej pętli testu. Wynik kroku
    /// wysyła <see cref="StartApp"/> i <see cref="StopApp"/> do hosta (pozostałe komendy nie mają tu wykonawcy), a fakty
    /// hosta wracają do Orchestratora w kolejności wysłania. Stan kafelka jest zbierany po każdej zmianie, tak jak
    /// <c>appStateChanged</c> w pętli App.
    /// </summary>
    private sealed class J4Pump(Harness h)
    {
        private readonly CoreOrchestrator _orchestrator = OrchestratorTestSupport.Started().Orchestrator;

        /// <summary>Kolejne różne stany kafelka z wyników kroków (bez stanu z zimnego startu).</summary>
        public List<AppLifecycleState> States { get; } = [];

        public AppLifecycleState State => States[^1];

        /// <summary>Liczba faktów hosta podanych już Orchestratorowi.</summary>
        public int Fed { get; private set; }

        public OrchestratorResult Handle(IOrchestratorMessage message)
        {
            var result = _orchestrator.Handle(message);
            if (result.Snapshot is { } snapshot)
            {
                var state = OrchestratorTestSupport.StateOf(snapshot, AppId);
                if (States.Count == 0 || States[^1] != state)
                {
                    States.Add(state);
                }
            }

            foreach (var command in result.Commands)
            {
                switch (command)
                {
                    case StartApp start:
                        h.Host.Start(start);
                        break;
                    case StopApp stop:
                        h.Host.Stop(stop);
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Podaje Orchestratorowi fakty hosta po kolei, aż łącznie <paramref name="total"/>; każdy czeka na swoje wysłanie,
        /// bo dopiero podany fakt (np. <see cref="ProcessExited"/> stopu) może zlecić hostowi następną komendę.
        /// </summary>
        public async Task<IReadOnlyList<OrchestratorResult>> Feed(int total)
        {
            var results = new List<OrchestratorResult>();
            while (Fed < total)
            {
                await h.WaitPosted(Fed + 1);
                results.Add(Handle(h.Posted[Fed]));
                Fed++;
            }

            return results;
        }

        /// <summary>LaunchApp z kafelka i dwa fakty startu (okno, potwierdzenie): kafelek w <c>Foreground</c>.</summary>
        public async Task Launch()
        {
            Handle(new LaunchApp(AppId));
            await Feed(Fed + 2);
            Assert.Equal(AppLifecycleState.Foreground, State);
        }

        /// <summary>
        /// Host nie wysyła już nic poza podanymi faktami (żadnego drugiego stopu, startu ani awarii). Zegar testu idzie
        /// najpierw 60 s naprzód, za każdy termin hosta (reconnect 30 s, łączenie CDP 15 s, czekanie stopu 3 s i 5 s),
        /// żeby spóźniony fakt na fałszywym czasie też został zauważony.
        /// </summary>
        public async Task AssertQuiet()
        {
            h.Time.Advance(TimeSpan.FromSeconds(60));
            await Task.Delay(200, Ct);
            Assert.Equal(Fed, h.Posted.Count);
        }
    }

    /// <summary>Pierwszy start dostaje <see cref="Harness.Process"/>, każdy kolejny świeży proces z następnym PID.</summary>
    private sealed class FakeLauncher(FakeProcess first) : IBrowserProcessLauncher
    {
        private readonly object _gate = new();
        private readonly List<(string Exe, IReadOnlyList<string> Args)> _starts = [];
        private readonly List<FakeProcess> _processes = [];

        public volatile bool ReturnNull;

        public IReadOnlyList<(string Exe, IReadOnlyList<string> Args)> Starts
        {
            get { lock (_gate) { return _starts.ToArray(); } }
        }

        public IReadOnlyList<FakeProcess> Processes
        {
            get { lock (_gate) { return _processes.ToArray(); } }
        }

        public IBrowserProcess? Start(string executable, IReadOnlyList<string> arguments)
        {
            lock (_gate)
            {
                _starts.Add((executable, arguments));
                if (ReturnNull)
                {
                    return null;
                }

                var process = _processes.Count == 0 ? first : new FakeProcess(first.Id + _processes.Count);
                _processes.Add(process);
                return process;
            }
        }
    }

    private sealed class FakeProcess(int id = 4242) : IBrowserProcess
    {
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _closeCalls;
        private int _killCalls;
        private int _timedWaits;
        private int? _exitCode;

        /// <summary>Okno ignoruje łagodne zamknięcie (zawieszony renderer, dialog <c>beforeunload</c>).</summary>
        public volatile bool IgnoreClose;

        /// <summary>Zabicie drzewa nie kończy korzenia od razu (wyjście dopiero przez <see cref="Exit"/>).</summary>
        public volatile bool IgnoreKill;

        public int Id => id;

        public bool HasExited => _exited.Task.IsCompleted;

        public int? ExitCode => HasExited ? _exitCode : null;

        public int CloseCalls => Volatile.Read(ref _closeCalls);

        public int KillCalls => Volatile.Read(ref _killCalls);

        /// <summary>Oczekiwania z tokenem, który da się anulować (okno łaski Stopu, nie watcher wyjścia).</summary>
        public int TimedWaits => Volatile.Read(ref _timedWaits);

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.CanBeCanceled)
            {
                Interlocked.Increment(ref _timedWaits);
            }

            return _exited.Task.WaitAsync(cancellationToken);
        }

        public void CloseMainWindow()
        {
            Interlocked.Increment(ref _closeCalls);
            if (!IgnoreClose)
            {
                Exit(0);
            }
        }

        public void KillTree()
        {
            Interlocked.Increment(ref _killCalls);
            if (!IgnoreKill)
            {
                Exit(1);
            }
        }

        public void Exit(int code)
        {
            if (!_exited.Task.IsCompleted)
            {
                _exitCode = code;
                _exited.TrySetResult();
            }
        }
    }

    /// <summary>Transport CDP w pamięci: zapisuje wysłane wiadomości i sam odpowiada na każde żądanie.</summary>
    private sealed class FakeTransport : ICdpTransport
    {
        public const string TargetId = "5F1C0A";

        private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();
        private readonly ConcurrentQueue<JsonElement> _sent = new();
        private string? _held;
        private int _delivered;

        public volatile string? FailMethod;

        /// <summary>Metoda, na którą odpowiedź czeka na <see cref="ReleaseHeld"/> (zawieszony cel albo wolna sieć).</summary>
        public volatile string? HoldMethod;

        /// <summary>Wyjątek rzucany przez wysyłkę (zamknięty socket), zanim cokolwiek zostanie zapisane.</summary>
        public volatile Exception? SendThrows;

        public volatile string NavigateResult = "{\"frameId\":\"F1\",\"loaderId\":\"L1\"}";

        /// <summary>Wynik <c>Browser.getVersion</c> (jak z Edge 156 na UM760).</summary>
        public volatile string VersionResult = "{\"protocolVersion\":\"1.3\",\"product\":\"Edg/" + CurrentVersion + "\",\"revision\":\"@x\",\"userAgent\":\"Mozilla/5.0\",\"jsVersion\":\"15.6\"}";

        public Uri Endpoint { get; init; } = new($"ws://127.0.0.1:9222/devtools/page/{TargetId}");

        /// <summary>
        /// Dispose nie kończy kanału: pętla odbioru starej sesji dalej dostarcza zdarzenia (wyścig zwolnienia z
        /// wiadomością w drodze). Kanał kończy dopiero <see cref="Close"/>.
        /// </summary>
        public volatile bool DeliverAfterDispose;

        public bool Disposed { get; private set; }

        public IReadOnlyList<JsonElement> Sent => _sent.ToArray();

        public string[] Methods() => Sent.Select(m => m.GetProperty("method").GetString()!).ToArray();

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            if (SendThrows is { } thrown)
            {
                throw thrown;
            }

            using var document = JsonDocument.Parse(message);
            var root = document.RootElement.Clone();
            _sent.Enqueue(root);
            var id = root.GetProperty("id").GetInt64();
            var method = root.GetProperty("method").GetString();
            var reply = method == FailMethod
                ? "\"error\":{\"code\":-32000,\"message\":\"nope\"}"
                : method == "Page.navigate"
                    ? "\"result\":" + NavigateResult
                    : method == "Browser.getVersion"
                        ? "\"result\":" + VersionResult
                        : "\"result\":{}";
            var response = "{\"id\":" + id + "," + reply + "}";
            if (method == HoldMethod)
            {
                Volatile.Write(ref _held, response);
            }
            else
            {
                Push(response);
            }

            return Task.CompletedTask;
        }

        /// <summary>Wysyła wstrzymaną odpowiedź na <see cref="HoldMethod"/>.</summary>
        public void ReleaseHeld()
        {
            if (Interlocked.Exchange(ref _held, null) is { } response)
            {
                Push(response);
            }
        }

        /// <summary>Wiadomości oddane pętli odbioru (po każdej poprzednia jest już w pełni obsłużona).</summary>
        public int Delivered => Volatile.Read(ref _delivered);

        public async Task<string?> ReceiveAsync()
        {
            while (await _incoming.Reader.WaitToReadAsync())
            {
                if (_incoming.Reader.TryRead(out var message))
                {
                    Interlocked.Increment(ref _delivered);
                    return message;
                }
            }

            return null;
        }

        public void Push(string json) => _incoming.Writer.TryWrite(json);

        public void PushBinding(string name, string payload) =>
            Push(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["method"] = "Runtime.bindingCalled",
                ["params"] = new Dictionary<string, object> { ["name"] = name, ["payload"] = payload, ["executionContextId"] = 1 },
            }));

        public void Close() => _incoming.Writer.TryComplete();

        public void Dispose()
        {
            Disposed = true;
            if (!DeliverAfterDispose)
            {
                Close();
            }
        }
    }
}
