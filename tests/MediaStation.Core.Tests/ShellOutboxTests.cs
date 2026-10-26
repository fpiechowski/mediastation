using MediaStation.App.Shell;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class ShellOutboxTests
{
    private static ShellSnapshot Snap(long rev) => new(rev, ShellScreen.Placeholder, [], [], null);

    private static Action Act(long ts, ActionKind kind = ActionKind.Right) =>
        new(kind, ActionPhase.Press, ActionSource.Keyboard, ts);

    private static ShellOutbox Ready(Func<long> clock)
    {
        var outbox = new ShellOutbox(Snap(0), clock);
        outbox.MarkReady();
        return outbox;
    }

    [Fact]
    public void Snapshot_goes_before_action_of_the_same_step()
    {
        var outbox = Ready(() => 100);
        var action = Act(100);
        var snapshot = Snap(1);

        outbox.PublishSnapshot(snapshot);
        outbox.PublishAction(action);

        Assert.Equal(
            [new ShellDelivery.SnapshotDelivery(snapshot), new ShellDelivery.ActionDelivery(action)],
            outbox.Drain());
    }

    [Fact]
    public void Snapshot_is_latest_wins_and_actions_keep_fifo_order()
    {
        var outbox = Ready(() => 100);
        var first = Act(99, ActionKind.Left);
        var second = Act(100, ActionKind.Right);
        var latest = Snap(2);

        outbox.PublishSnapshot(Snap(1));
        outbox.PublishAction(first);
        outbox.PublishSnapshot(latest);
        outbox.PublishAction(second);

        Assert.Equal(
            [
                new ShellDelivery.SnapshotDelivery(latest),
                new ShellDelivery.ActionDelivery(first),
                new ShellDelivery.ActionDelivery(second),
            ],
            outbox.Drain());
        Assert.Empty(outbox.Drain());
    }

    [Fact]
    public void Publish_asks_for_one_flush_until_drained()
    {
        var outbox = Ready(() => 0);

        Assert.True(outbox.PublishAction(Act(0)));
        Assert.False(outbox.PublishAction(Act(0)));
        outbox.Drain();
        Assert.True(outbox.PublishAction(Act(0)));
    }

    [Fact]
    public void Action_before_ready_is_dropped_and_not_buffered()
    {
        var outbox = new ShellOutbox(Snap(0), () => 10);
        var action = Act(10);
        outbox.PublishAction(action);

        Assert.Equal([new ShellDelivery.DroppedAction(action, ShellOutbox.NotReady)], outbox.Drain());

        outbox.MarkReady();
        Assert.Empty(outbox.Drain());
    }

    [Fact]
    public void Action_after_navigation_is_dropped_until_next_ready()
    {
        var outbox = Ready(() => 10);
        outbox.MarkNavigated();
        var dropped = Act(10);
        outbox.PublishAction(dropped);

        Assert.Equal([new ShellDelivery.DroppedAction(dropped, ShellOutbox.NotReady)], outbox.Drain());

        outbox.MarkReady();
        var delivered = Act(10);
        outbox.PublishAction(delivered);
        Assert.Equal([new ShellDelivery.ActionDelivery(delivered)], outbox.Drain());
    }

    [Fact]
    public void Action_older_than_one_second_is_dropped_as_stale()
    {
        var outbox = Ready(() => 5000);
        var fresh = Act(4000);
        var stale = Act(3999);

        outbox.PublishAction(fresh);
        outbox.PublishAction(stale);

        Assert.Equal(
            [new ShellDelivery.ActionDelivery(fresh), new ShellDelivery.DroppedAction(stale, ShellOutbox.Stale)],
            outbox.Drain());
    }

    [Fact]
    public void Age_survives_32_bit_counter_scale_difference()
    {
        // TsMono z huba bez „obiegów” licznika, zegar hosta z pełną liczbą: wiek liczony modulo 2^32.
        var outbox = Ready(() => (3L << 32) + 500);
        var fresh = Act(400);
        var stale = Act(-700);

        outbox.PublishAction(fresh);
        outbox.PublishAction(stale);

        Assert.Equal(
            [new ShellDelivery.ActionDelivery(fresh), new ShellDelivery.DroppedAction(stale, ShellOutbox.Stale)],
            outbox.Drain());
    }

    [Fact]
    public void Action_from_the_future_is_not_stale()
    {
        var outbox = Ready(() => 100);
        var action = Act(105);

        outbox.PublishAction(action);

        Assert.Equal([new ShellDelivery.ActionDelivery(action)], outbox.Drain());
    }

    [Fact]
    public void Latest_returns_initial_then_published_snapshot()
    {
        var outbox = new ShellOutbox(Snap(0), () => 0);
        Assert.Equal(0, outbox.Latest.Rev);

        outbox.PublishSnapshot(Snap(3));

        Assert.Equal(3, outbox.Latest.Rev);
    }
}
