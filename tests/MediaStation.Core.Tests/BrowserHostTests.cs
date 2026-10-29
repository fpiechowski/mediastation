using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MediaStation.App.Hosts.Browser;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// BrowserHost (story 6.2) na fake'ach procesu, okna i transportu CDP: kolejność Events, dokładne argumenty AD-1,
/// metody CDP bez <c>Runtime.enable</c>, <c>hello</c> i odrzucane komunikaty strony, binding po nawigacji głównej
/// ramki, termin CDP na <see cref="FakeTimeProvider"/>, porażki startu i Stop. Story 6.3: reconnect do tego samego
/// celu z backoffem, termin <c>cdpReconnectTimeout</c>, <see cref="CdpLost"/> i Home na prawdziwym Orchestratorze.
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
    public void Init_script_calls_the_runtime_expression_with_json_config_and_drops_a_trailing_semicolon()
    {
        var script = BrowserLaunch.BuildInitScript("// c\n(config) => { go(config); };\n", "__msBridge", "netflix", "0.0.0");

        Assert.Equal("(// c\n(config) => { go(config); }\n)({\"binding\":\"__msBridge\",\"service\":\"netflix\",\"driverVersion\":\"0.0.0\"});", script);
    }

    [Fact]
    public void Init_script_carries_null_driver_version()
    {
        var script = BrowserLaunch.BuildInitScript("(c) => {}", "__msBridge", "netflix", null);

        Assert.EndsWith("\"driverVersion\":null});", script, StringComparison.Ordinal);
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
    [InlineData("""{ "v": 2, "type": "hello", "id": null, "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", "version")]
    [InlineData("""{ "type": "hello", "id": null, "payload": { "service": "netflix", "driverVersion": "0", "webdriver": false } }""", "version")]
    [InlineData("""{ "v": 1, "type": "screen", "id": null, "payload": {} }""", "type")]
    [InlineData("""{ "v": 1, "type": "hello", "id": null, "payload": { "service": "netflix", "driverVersion": "0" } }""", "payload")]
    [InlineData("""{ "v": 1, "type": "hello", "id": null, "payload": "x" }""", "payload")]
    [InlineData("junk", "unparsable")]
    [InlineData("[1]", "unparsable")]
    [InlineData("", "unparsable")]
    public void Bad_page_messages_are_rejected_with_a_reason(string json, string reason)
    {
        var parsed = PageProtocol.Parse(json);

        Assert.Null(parsed.Hello);
        Assert.Equal(reason, parsed.Reason);
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
        Assert.Equal(
            ["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument", "Page.navigate"],
            h.Transport.Methods());
        Assert.Equal("__msBridge", h.Transport.Sent[0].GetProperty("params").GetProperty("name").GetString());
        var source = h.Transport.Sent[2].GetProperty("params").GetProperty("source").GetString()!;
        Assert.StartsWith("((config) => {}", source, StringComparison.Ordinal);
        Assert.Contains("\"binding\":\"__msBridge\",\"service\":\"netflix\",\"driverVersion\":\"0.0.0\"", source, StringComparison.Ordinal);
        Assert.Equal(StartUrl, h.Transport.Sent[3].GetProperty("params").GetProperty("url").GetString());
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
    [InlineData("__msBridge", """{"v":2,"type":"hello","id":null,"payload":{"service":"netflix","driverVersion":"0","webdriver":false}}""", "version")]
    [InlineData("__msBridge", """{"v":1,"type":"playback","id":null,"payload":{}}""", "type")]
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

    [Fact]
    public async Task Binding_is_re_added_only_after_main_frame_navigation()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"child","parentId":"main","url":"https://x/"},"type":"Navigation"}}""");
        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://www.netflix.com/browse"},"type":"Navigation"}}""");
        await Eventually(() => h.Transport.Methods().Length == 5);
        await Task.Delay(100, Ct);

        Assert.Equal(
            ["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument", "Page.navigate", "Runtime.addBinding"],
            h.Transport.Methods());
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

        Assert.Equal([new AppWindowAttached(AppId, 0x1234, 4242), new CommandFailed(7, ErrorCode.CdpUnavailable)], h.Posted);
        var warn = h.Log.Entries.Single(e => e.Msg == "cdpUnavailable");
        Assert.Equal(LogLevel.Warn, warn.Level);
        Assert.Equal(9222, warn.Data!["cdpPort"]);
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

        Assert.Equal([new CommandFailed(7, ErrorCode.Unexpected)], h.Posted);
        Assert.Equal("windowTimeout", StartFailure(h).Data!["reason"]);
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
    public void Probe_and_renew_are_silent()
    {
        using var h = new Harness();

        h.Host.Probe(new ProbeExisting(1, AppId));
        h.Host.Renew(new RenewSession(2, AppId));

        Assert.Empty(h.Posted);
        Assert.Empty(h.Launcher.Starts);
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

    // ── Story 6.3: binding po nawigacji i reconnect CDP ───────────────────────

    [Fact]
    public async Task Each_main_frame_navigation_re_adds_only_the_binding_and_never_the_script()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://www.netflix.com/browse"}}}""");
        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://www.netflix.com/title/1"}}}""");
        await Eventually(() => h.Transport.Methods().Length == 6);
        await Task.Delay(100, Ct);

        Assert.Equal(
            ["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument", "Page.navigate", "Runtime.addBinding", "Runtime.addBinding"],
            h.Transport.Methods());
    }

    [Fact]
    public async Task Binding_refresh_cut_by_a_closing_socket_is_silent()
    {
        using var h = new Harness();
        h.Host.Start(new StartApp(7, AppId));
        await h.WaitPosted(2);

        h.Transport.HoldMethod = "Runtime.addBinding";
        h.Transport.Push("""{"method":"Page.frameNavigated","params":{"frame":{"id":"main","url":"https://x/"}}}""");
        await Eventually(() => h.Transport.Methods().Length == 5);
        h.Transport.Close();
        await h.WaitLog("cdpClosed");
        await Task.Delay(100, Ct);

        Assert.DoesNotContain(h.Log.Entries, e => e.Msg == "bindingRefreshFailed");
    }

    [Fact]
    public async Task Drop_with_quick_recovery_reinstalls_on_the_same_target_without_navigate()
    {
        using var h = new Harness();
        var second = new FakeTransport();
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
        Assert.StartsWith("((config) => {}", second.Sent[2].GetProperty("params").GetProperty("source").GetString(), StringComparison.Ordinal);
        Assert.Equal(["Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument", "Page.navigate"], h.Transport.Methods());
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
        int[] schedule = [250, 750, 1750, .. Enumerable.Range(0, 14).Select(i => 3750 + (i * 2000))];
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
        await StepToAttempt(h, closedAt, 250, 1);
        await StepToAttempt(h, closedAt, 750, 2);
        await StepToAttempt(h, closedAt, 1750, 3);
        h.Time.Advance(closedAt.AddSeconds(30) - h.Time.GetUtcNow());
        await h.WaitPosted(3);

        Assert.Equal(new CdpLost(AppId), h.Posted[2]);
        Assert.True(h.ReconnectAttempts > 1);
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
        var second = new FakeTransport();
        var third = new FakeTransport();
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
        private int _connectCalls;

        public Harness(
            Func<BrowserKind, string?>? resolve = null,
            Func<int, WindowInfo?>? findWindow = null,
            Func<int, CancellationToken, Task<ICdpTransport?>>? connect = null,
            Func<Uri, CancellationToken, Task<ICdpTransport?>>? reconnect = null)
        {
            Files[RuntimePath] = "(config) => {}";
            Files[ManifestPath] = ManifestJson;
            Launcher = new FakeLauncher(Process);
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
                // Każdy uruchomiony proces ma własne okno: 4242 → 0x1234, 4243 → 0x1235…
                findWindow ?? (pid => Launcher.Processes.Any(p => p.Id == pid) ? Window with { Hwnd = Window.Hwnd + pid - Process.Id, Pid = pid } : null),
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
                Time);
        }

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

            Transport.Dispose();
            foreach (var transport in Reconnects)
            {
                transport?.Dispose();
            }
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
            Exit(1);
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
