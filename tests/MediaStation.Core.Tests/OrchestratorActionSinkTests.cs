using System.Diagnostics;
using MediaStation.App.Platform.Input;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>
/// Konsument Actions z wątku Input (AD-8): jedno <c>action {kind, phase, source}</c> w logu, ten sam komunikat
/// w kolejce Orchestratora dokładnie raz i w tej samej kolejności, a <see cref="OrchestratorActionSink.Report"/>
/// zostaje wyłącznie w logu.
/// </summary>
public class OrchestratorActionSinkTests
{
    [Fact]
    public void Deliver_logs_one_action_and_posts_the_same_message_in_order()
    {
        var log = new RecordingLog();
        var posted = new List<IOrchestratorMessage>();

        // Liczba wpisów w chwili przekazania dowodzi, że log powstaje przed kolejką.
        var entriesAtPost = new List<int>();
        var sink = new OrchestratorActionSink(log, message =>
        {
            entriesAtPost.Add(log.Entries.Count);
            posted.Add(message);
        });

        var first = new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1);
        var second = new Action(ActionKind.Ok, ActionPhase.Long, ActionSource.Keyboard, 2);
        sink.Deliver(first);
        sink.Deliver(second);

        var entries = log.Entries;
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal(LogLevel.Info, entry.Level);
            Assert.Equal(LogActionSink.Src, entry.Src);
            Assert.Equal("action", entry.Msg);
            Assert.Equal(["kind", "phase", "source"], entry.Data!.Keys.ToArray());
        });
        Assert.Equal(ActionKind.Home, entries[0].Data!["kind"]);
        Assert.Equal(ActionKind.Ok, entries[1].Data!["kind"]);
        Assert.Equal(new IOrchestratorMessage[] { first, second }, posted);
        Assert.Equal(new[] { 1, 2 }, entriesAtPost);
    }

    [Fact]
    public void Action_from_the_pump_is_logged_once_and_posted_once()
    {
        var log = new RecordingLog();
        var posted = new List<IOrchestratorMessage>();
        var sink = new OrchestratorActionSink(log, posted.Add);
        var pump = new ActionPump(sink, sink);
        pump.Start();

        var action = new Action(ActionKind.Ok, ActionPhase.Press, ActionSource.Keyboard, 7);
        pump.Queue.Enqueue(action);

        Assert.True(WaitFor(() => log.Entries.Count >= 1));
        pump.Dispose();

        // Pompa domyka resztę kolejki w Dispose, więc po nim oba kanały są kompletne.
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal(LogActionSink.Src, entry.Src);
        Assert.Equal("action", entry.Msg);
        Assert.Equal(ActionKind.Ok, entry.Data!["kind"]);
        Assert.Equal(ActionPhase.Press, entry.Data["phase"]);
        Assert.Equal(ActionSource.Keyboard, entry.Data["source"]);
        Assert.Equal(new IOrchestratorMessage[] { action }, posted);
    }

    [Fact]
    public void Report_logs_the_hub_error_and_posts_nothing()
    {
        var log = new RecordingLog();
        var posted = new List<IOrchestratorMessage>();
        var sink = new OrchestratorActionSink(log, posted.Add);

        sink.Report(new InputHubError(InputHubOperations.ActionSink, null));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(LogActionSink.Src, entry.Src);
        Assert.Equal("inputHubError", entry.Msg);
        Assert.Equal(InputHubOperations.ActionSink, entry.Data!["operation"]);
        Assert.Empty(posted);
    }

    private static bool WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }
}
