using System.Collections.Concurrent;
using MediaStation.App.Orchestrator;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
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
        using var loop = CreateLoop(out var published);

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
    public void Post_after_dispose_is_a_noop()
    {
        var loop = CreateLoop(out var published);
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
        var loop = CreateLoop(out var published);
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

    private static OrchestratorLoop CreateLoop(out ConcurrentQueue<ShellSnapshot> published)
    {
        var snapshots = new ConcurrentQueue<ShellSnapshot>();
        published = snapshots;
        return new OrchestratorLoop(new CoreOrchestrator(TwoApps()), snapshots.Enqueue, new NullLog());
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
