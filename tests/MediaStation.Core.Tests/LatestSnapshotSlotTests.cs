using MediaStation.Core.Contracts;
using MediaStation.Core.Orchestrator;

namespace MediaStation.Core.Tests;

public class LatestSnapshotSlotTests
{
    private static ShellSnapshot Snapshot(long rev) =>
        new(rev, ShellScreen.Placeholder, [], [], null);

    [Fact]
    public void Initial_snapshot_is_latest_and_nothing_is_pending()
    {
        var slot = new LatestSnapshotSlot(Snapshot(0));

        Assert.Equal(0, slot.Latest.Rev);
        Assert.Null(slot.TakePending());
    }

    [Fact]
    public void Several_publishes_before_flush_schedule_once_and_send_only_the_newest()
    {
        var slot = new LatestSnapshotSlot(Snapshot(0));

        Assert.True(slot.Publish(Snapshot(1)));
        Assert.False(slot.Publish(Snapshot(2)));
        Assert.False(slot.Publish(Snapshot(3)));

        var pending = Assert.IsType<ShellSnapshot>(slot.TakePending());
        Assert.Equal(3, pending.Rev);
        Assert.Null(slot.TakePending());
        Assert.Equal(3, slot.Latest.Rev);
    }

    [Fact]
    public void Publish_after_flush_schedules_another_one()
    {
        var slot = new LatestSnapshotSlot(Snapshot(0));
        slot.Publish(Snapshot(1));
        Assert.NotNull(slot.TakePending());

        Assert.True(slot.Publish(Snapshot(2)));
        Assert.Equal(2, Assert.IsType<ShellSnapshot>(slot.TakePending()).Rev);
    }
}
