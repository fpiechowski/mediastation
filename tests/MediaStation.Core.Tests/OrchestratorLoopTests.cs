using System.Collections.Concurrent;
using MediaStation.App.Hosts;
using MediaStation.App.Orchestrator;
using MediaStation.App.Platform.Input;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;

namespace MediaStation.Core.Tests;

/// <summary>
/// <see cref="OrchestratorLoop.Post"/>: jedyne wejście do kolejki dla adapterów oraz zachowanie po
/// <c>Dispose</c> i w wyścigu z zamknięciem.
/// </summary>
public class OrchestratorLoopTests
{
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
            new CoreOrchestrator(TwoApps()),
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
        WaitFor(() => dispatched.Count >= 2);
        WaitFor(() => published.Count >= 2);

        Assert.Equal(new StartApp(2, "netflix"), dispatched.ElementAt(1));
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
    public void Missing_host_answer_returns_to_the_queue_and_keeps_app_starting()
    {
        var log = new RecordingLog();
        var published = new ConcurrentQueue<ShellSnapshot>();

        // Jak w composition root: port „brak hosta” odpowiada w kolejce, a pętla powstaje po routerze.
        OrchestratorLoop? postsTo = null;
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
            log);
        using var loop = new OrchestratorLoop(new CoreOrchestrator(TwoApps()), router.Dispatch, published.Enqueue, log);
        postsTo = loop;
        loop.Start();
        WaitFor(() => published.Count >= 1);

        loop.Post(new LaunchApp("netflix"));
        WaitFor(() => log.Entries.Any(entry => entry.Msg == "commandFailed"));
        WaitFor(() => published.Count >= 2);

        var startWithoutHost = Assert.Single(log.Entries, entry => entry.Msg == "startWithoutHost");
        Assert.Equal(2L, startWithoutHost.Data!["cmdId"]);
        Assert.Equal("netflix", startWithoutHost.Data["appId"]);
        var commandFailed = Assert.Single(log.Entries, entry => entry.Msg == "commandFailed");
        Assert.Equal(LogLevel.Warn, commandFailed.Level);
        Assert.Equal(2L, commandFailed.Data!["cmdId"]);
        Assert.Equal(ErrorCode.Unexpected, commandFailed.Data["errorCode"]);
        Assert.Equal(
            AppLifecycleState.Starting,
            published.ElementAt(1).Apps.Single(app => app.AppId == "netflix").State);
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
            new CoreOrchestrator(TwoApps()),
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
}
