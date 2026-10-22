using System.Collections.Concurrent;
using MediaStation.App.Hosts;
using MediaStation.App.Orchestrator;
using MediaStation.App.Platform.Input;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Orchestrator;
using MediaStation.Core.Ports;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;

namespace MediaStation.Core.Tests;

/// <summary>
/// <see cref="OrchestratorLoop.Post"/>: jedyne wejście do kolejki dla adapterów oraz zachowanie po
/// <c>Dispose</c> i w wyścigu z zamknięciem.
/// </summary>
public class OrchestratorLoopTests
{
    /// <summary>Punkt startowy zegara; <c>startingTimeout</c> z domyślnych <see cref="Settings"/> to 30 s, a <c>stoppingTimeout</c> — 45 s.</summary>
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Posted_event_is_handled_and_publishes_next_snapshot()
    {
        using var loop = CreateLoop(out var published, out _);

        loop.Start();
        WaitFor(() => published.Count >= 1);

        loop.Post(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));
        WaitFor(() => published.Count >= 2);

        var first = published.ElementAt(0);
        var second = published.ElementAt(1);
        Assert.Equal(1, first.Rev);
        Assert.Equal(2, second.Rev);
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 2), second.FocusRequest);
    }

    [Fact]
    public void Startup_dispatches_shell_routing_before_publishing_snapshot()
    {
        var order = new List<string>();
        var published = new ManualResetEventSlim();
        using var loop = new OrchestratorLoop(
            new CoreOrchestrator(TwoApps(), new FakeTimeProvider(T0)),
            batch =>
            {
                lock (order)
                {
                    order.AddRange(batch.Select(command => "dispatch:" + command.GetType().Name));
                }
            },
            _ =>
            {
                lock (order)
                {
                    order.Add("publish");
                }

                published.Set();
            },
            new NullLog());

        loop.Start();
        Assert.True(published.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        lock (order)
        {
            Assert.Equal(["dispatch:SetInputRouting", "publish"], order);
        }
    }

    [Fact]
    public void Posted_launch_app_reaches_dispatcher_and_snapshot_shows_starting()
    {
        using var loop = CreateLoop(out var published, out var dispatched);
        loop.Start();
        WaitFor(() => published.Count >= 1);

        loop.Post(new LaunchApp("netflix"));
        WaitFor(() => dispatched.Count >= 3);
        WaitFor(() => published.Count >= 2);

        Assert.Equal(new StartApp(2, "netflix"), dispatched.ElementAt(1));
        Assert.Equal(new ScheduleTimer(3, 1, "netflix", T0.AddSeconds(30)), dispatched.ElementAt(2));
        Assert.Equal(
            AppLifecycleState.Starting,
            published.ElementAt(1).Apps.Single(app => app.AppId == "netflix").State);
    }

    [Fact]
    public void Command_failed_with_unknown_cmd_id_logs_warn_and_does_not_change_rev()
    {
        var log = new RecordingLog();
        using var loop = CreateLoop(out var published, out _, log);
        loop.Start();
        WaitFor(() => published.Count >= 1);

        loop.Post(new CommandFailed(999, ErrorCode.CdpUnavailable));
        WaitFor(() => log.Entries.Any(entry => entry.Msg == "commandFailed"));

        Assert.Single(published);
        var entry = Assert.Single(log.Entries, entry => entry.Msg == "commandFailed");
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("orchestrator", entry.Src);
        Assert.Equal(999L, entry.Data!["cmdId"]);
        Assert.Equal(ErrorCode.CdpUnavailable, entry.Data["errorCode"]);
    }

    [Fact]
    public void Foreground_changed_is_logged_without_changing_state()
    {
        var log = new RecordingLog();
        using var loop = CreateLoop(out var published, out _, log);
        loop.Start();
        WaitFor(() => published.Count >= 1);

        loop.Post(new ForegroundChanged(1234));
        WaitFor(() => log.Entries.Any(entry => entry.Msg == "foregroundChanged"));

        // Pętla loguje przed dispatch/publish, więc sam wpis nie dowodzi braku snapshotu: SystemResumed
        // publikuje kolejny snapshot z nowym `seq`, a gdyby `ForegroundChanged` też publikował, kolejka
        // miałaby o jeden wpis więcej (albo snapshot bez nowego `seq`).
        loop.Post(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));
        WaitFor(() => published.Any(snapshot => snapshot.FocusRequest?.Seq == 2));

        var snapshots = published.ToArray();
        Assert.Collection(
            snapshots,
            snapshot =>
            {
                Assert.Equal(1, snapshot.Rev);
                Assert.Equal(1, snapshot.FocusRequest!.Seq);
            },
            snapshot =>
            {
                Assert.Equal(2, snapshot.Rev);
                Assert.Equal(2, snapshot.FocusRequest!.Seq);
            });
        var entry = Assert.Single(log.Entries, entry => entry.Msg == "foregroundChanged");
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal("orchestrator", entry.Src);
        Assert.Equal(1234L, entry.Data!["hwnd"]);
    }

    [Fact]
    public void Missing_host_start_failure_returns_to_the_queue_as_failed()
    {
        var log = new RecordingLog();
        var published = new ConcurrentQueue<ShellSnapshot>();

        // Jak w composition root: port „brak hosta” odpowiada w kolejce, a pętla powstaje po routerze.
        OrchestratorLoop? postsTo = null;
        var time = new FakeTimeProvider(T0);
        using var timers = new TimerScheduler(time, message => postsTo?.Post(message));
        var hosts = new Dictionary<AppType, IAppHostPort>
        {
            [AppType.Web] = new MissingHostPort(log, message => postsTo?.Post(message)),
            [AppType.Moonlight] = new MissingHostPort(log, message => postsTo?.Post(message)),
            [AppType.Native] = new MissingHostPort(log, message => postsTo?.Post(message)),
        };
        var router = new CommandRouter(
            new Dictionary<string, AppType>(StringComparer.Ordinal)
            {
                ["netflix"] = AppType.Web,
                ["plex"] = AppType.Native,
            },
            hosts,
            new LogWindowManagerPort(log),
            new LogInputRoutingPort(log),
            timers,
            log);
        using var loop = new OrchestratorLoop(new CoreOrchestrator(TwoApps(), time), router.Dispatch, published.Enqueue, log);
        postsTo = loop;
        loop.Start();
        WaitFor(() => published.Count >= 1);

        loop.Post(new LaunchApp("netflix"));
        WaitFor(() => log.Entries.Any(entry => entry.Msg == "commandFailed"));
        WaitFor(() => published.Count >= 3);

        var startWithoutHost = Assert.Single(log.Entries, entry => entry.Msg == "startWithoutHost");
        Assert.Equal(2L, startWithoutHost.Data!["cmdId"]);
        Assert.Equal("netflix", startWithoutHost.Data["appId"]);
        var commandFailed = Assert.Single(log.Entries, entry => entry.Msg == "commandFailed");
        Assert.Equal(LogLevel.Warn, commandFailed.Level);
        Assert.Equal(2L, commandFailed.Data!["cmdId"]);
        Assert.Equal(ErrorCode.Unexpected, commandFailed.Data["errorCode"]);

        // StartApp → snapshot `Starting`, a CommandFailed z adaptera domyka przejście w `Failed`.
        var snapshots = published.ToArray();
        Assert.Equal(1, snapshots[0].Rev);
        Assert.Equal(AppLifecycleState.Starting, snapshots[1].Apps.Single(app => app.AppId == "netflix").State);
        Assert.Equal(3, snapshots[2].Rev);
        Assert.Equal(AppLifecycleState.Failed, snapshots[2].Apps.Single(app => app.AppId == "netflix").State);
        Assert.Equal(snapshots[1].FocusRequest, snapshots[2].FocusRequest);
    }

    [Fact]
    public void Start_timeout_without_host_response_publishes_failed_snapshot()
    {
        var time = new FakeTimeProvider(T0);
        var log = new RecordingLog();
        var published = new ConcurrentQueue<ShellSnapshot>();
        OrchestratorLoop? postsTo = null;
        var hosts = new Dictionary<AppType, IAppHostPort>
        {
            [AppType.Web] = new SilentHost(),
            [AppType.Moonlight] = new SilentHost(),
            [AppType.Native] = new SilentHost(),
        };
        using var timers = new TimerScheduler(time, message => postsTo?.Post(message));
        var router = new CommandRouter(
            new Dictionary<string, AppType>(StringComparer.Ordinal) { ["netflix"] = AppType.Web },
            hosts,
            new LogWindowManagerPort(log),
            new LogInputRoutingPort(log),
            timers,
            log);
        using var loop = new OrchestratorLoop(new CoreOrchestrator(TwoApps(), time), router.Dispatch, published.Enqueue, log);
        postsTo = loop;
        loop.Start();
        WaitFor(() => published.Count >= 1);

        // Host przyjmuje StartApp bez odpowiedzi: tylko zegar może wyprowadzić aplikację ze stanu Starting.
        loop.Post(new LaunchApp("netflix"));
        WaitFor(() => published.Count >= 2);
        Assert.Equal(
            AppLifecycleState.Starting,
            published.ElementAt(1).Apps.Single(app => app.AppId == "netflix").State);

        time.Advance(TimeSpan.FromSeconds(30));
        WaitFor(() => published.Count >= 3);

        var snapshots = published.ToArray();
        Assert.Equal(3, snapshots[2].Rev);
        Assert.Equal(AppLifecycleState.Failed, snapshots[2].Apps.Single(app => app.AppId == "netflix").State);
        Assert.Equal(snapshots[1].FocusRequest, snapshots[2].FocusRequest);
    }

    [Fact]
    public void Stop_timeout_without_host_response_publishes_failed_snapshot_without_new_focus()
    {
        var time = new FakeTimeProvider(T0);
        var log = new RecordingLog();
        var published = new ConcurrentQueue<ShellSnapshot>();
        OrchestratorLoop? postsTo = null;
        var hosts = new Dictionary<AppType, IAppHostPort>
        {
            [AppType.Web] = new SilentHost(),
            [AppType.Moonlight] = new SilentHost(),
            [AppType.Native] = new SilentHost(),
        };
        using var timers = new TimerScheduler(time, message => postsTo?.Post(message));
        var router = new CommandRouter(
            new Dictionary<string, AppType>(StringComparer.Ordinal) { ["netflix"] = AppType.Web },
            hosts,
            new LogWindowManagerPort(log),
            new LogInputRoutingPort(log),
            timers,
            log);
        using var loop = new OrchestratorLoop(new CoreOrchestrator(TwoApps(), time), router.Dispatch, published.Enqueue, log);
        postsTo = loop;
        loop.Start();
        WaitFor(() => published.Count >= 1);

        loop.Post(new LaunchApp("netflix"));
        WaitFor(() => published.Count >= 2);
        loop.Post(new AppWindowAttached("netflix", 0x1A2B, 4321));
        WaitFor(() => published.Count >= 3);
        loop.Post(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1));
        WaitFor(() => published.Count >= 4);
        loop.Post(new CloseApp("netflix"));
        WaitFor(() => published.Count >= 5);
        Assert.Equal(
            AppLifecycleState.Stopping,
            published.ElementAt(4).Apps.Single(app => app.AppId == "netflix").State);

        // Host przyjmuje StopApp bez odpowiedzi: tylko zegar może wyprowadzić aplikację ze stanu Stopping.
        time.Advance(TimeSpan.FromSeconds(45));
        WaitFor(() => published.Count >= 6);

        var snapshots = published.ToArray();
        Assert.Equal(6, snapshots[5].Rev);
        Assert.Equal(AppLifecycleState.Failed, snapshots[5].Apps.Single(app => app.AppId == "netflix").State);
        Assert.Equal(snapshots[4].FocusRequest, snapshots[5].FocusRequest);
    }

    /// <summary>
    /// Home w trakcie <c>Starting</c> dochodzi do wykonawców jako <c>ShowShell</c>, a spóźnione
    /// <c>AppWindowAttached</c> jako kompensujące <c>MinimizeWindow</c> z anulowaniem terminu startu:
    /// snapshot pokazuje <c>Background</c>, a minięty deadline nie wprowadza <c>Failed</c>.
    /// </summary>
    [Fact]
    public void Home_while_starting_minimizes_late_window_and_cancels_start_timeout()
    {
        var time = new FakeTimeProvider(T0);
        var log = new RecordingLog();
        var published = new ConcurrentQueue<ShellSnapshot>();
        var dispatched = new ConcurrentQueue<Command>();
        OrchestratorLoop? postsTo = null;
        var hosts = new Dictionary<AppType, IAppHostPort>
        {
            [AppType.Web] = new SilentHost(),
            [AppType.Moonlight] = new SilentHost(),
            [AppType.Native] = new SilentHost(),
        };
        using var timers = new TimerScheduler(time, message => postsTo?.Post(message));
        var router = new CommandRouter(
            new Dictionary<string, AppType>(StringComparer.Ordinal) { ["netflix"] = AppType.Web },
            hosts,
            new LogWindowManagerPort(log),
            new LogInputRoutingPort(log),
            timers,
            log);
        using var loop = new OrchestratorLoop(
            new CoreOrchestrator(TwoApps(), time),
            batch =>
            {
                foreach (var command in batch)
                {
                    dispatched.Enqueue(command);
                }

                router.Dispatch(batch);
            },
            published.Enqueue,
            log);
        postsTo = loop;
        loop.Start();
        WaitFor(() => published.Count >= 1);

        loop.Post(new LaunchApp("netflix"));
        WaitFor(() => published.Count >= 2);
        loop.Post(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1));
        WaitFor(() => published.Count >= 3);
        loop.Post(new AppWindowAttached("netflix", 0x1A2B, 4321));
        WaitFor(() => published.Count >= 4);

        var snapshots = published.ToArray();
        Assert.Equal(AppLifecycleState.Starting, snapshots[2].Apps.Single(app => app.AppId == "netflix").State);
        Assert.Equal(snapshots[2].FocusRequest, snapshots[3].FocusRequest);
        Assert.Contains(dispatched, command => command is MinimizeWindow minimize && minimize.Hwnd == 0x1A2B);
        Assert.Contains(dispatched, command => command is CancelTimer);
        Assert.DoesNotContain(dispatched, command => command is ActivateWindow);

        // Anulowany timeout nie odpala: po 30 s wybudzenie publikuje kolejny snapshot bez `Failed` pomiędzy.
        time.Advance(TimeSpan.FromSeconds(30));
        loop.Post(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));
        WaitFor(() => published.Count >= 5);

        var afterDeadline = published.ToArray();
        Assert.Equal(5, afterDeadline.Length);
        Assert.Equal(AppLifecycleState.Background, afterDeadline[4].Apps.Single(app => app.AppId == "netflix").State);
    }

    [Fact]
    public void Post_after_dispose_is_a_noop()
    {
        var loop = CreateLoop(out var published, out _);
        loop.Start();
        WaitFor(() => published.Count >= 1);
        loop.Dispose();

        var exception = Record.Exception(() => loop.Post(new SystemResumed("PBT_APMRESUMEAUTOMATIC")));

        Assert.Null(exception);
        Assert.Single(published);
    }

    [Fact]
    public void Post_racing_with_dispose_never_throws()
    {
        var loop = CreateLoop(out var published, out _);
        loop.Start();
        WaitFor(() => published.Count >= 1);

        var failures = new ConcurrentQueue<Exception>();
        using var go = new ManualResetEventSlim();
        var posters = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
        {
            go.Wait();
            for (var i = 0; i < 500; i++)
            {
                try
                {
                    loop.Post(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }
            }
        })).ToArray();

        foreach (var poster in posters)
        {
            poster.Start();
        }

        go.Set();
        loop.Dispose();
        foreach (var poster in posters)
        {
            poster.Join();
        }

        Assert.Empty(failures);
    }

    private static OrchestratorLoop CreateLoop(
        out ConcurrentQueue<ShellSnapshot> published,
        out ConcurrentQueue<Command> dispatched,
        ILog? log = null)
    {
        var snapshots = new ConcurrentQueue<ShellSnapshot>();
        var commands = new ConcurrentQueue<Command>();
        published = snapshots;
        dispatched = commands;
        return new OrchestratorLoop(
            new CoreOrchestrator(TwoApps(), new FakeTimeProvider(T0)),
            batch =>
            {
                foreach (var command in batch)
                {
                    commands.Enqueue(command);
                }
            },
            snapshots.Enqueue,
            log ?? new NullLog());
    }

    private static LoadedConfig TwoApps() => new(
        new AppsConfig([
            new WebApp("netflix", AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222),
            new NativeApp("plex", AppCategory.Game, "Plex", "plex.png", "C:\\Plex\\Plex.exe"),
        ]),
        new InputConfig([], []),
        new Settings());

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Pętla Orchestratora nie opublikowała snapshotu w czasie testu.");
            }

            Thread.Sleep(10);
        }
    }

    private sealed class NullLog : ILog
    {
        public void Write(LogLevel level, string src, string msg, IReadOnlyDictionary<string, object?>? data = null, Exception? error = null)
        {
        }
    }

    /// <summary>Host, który przyjmuje komendy i nigdy nie odpowiada: tylko timeout może zamknąć start.</summary>
    private sealed class SilentHost : IAppHostPort
    {
        public void Start(StartApp command)
        {
        }

        public void Stop(StopApp command)
        {
        }
    }
}
