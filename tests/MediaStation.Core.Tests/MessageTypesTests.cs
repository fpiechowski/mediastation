using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class MessageTypesTests
{
    private sealed record Snapshot;

    private sealed record Ready;

    [Fact]
    public void Build_index_maps_camel_case_names()
    {
        var index = MessageTypes.BuildIndex([typeof(Action)]);

        Assert.Equal(typeof(Action), index["action"]);
    }

    [Fact]
    public void Build_index_rejects_snapshot_name()
    {
        var error = Assert.Throws<InvalidOperationException>(() => MessageTypes.BuildIndex([typeof(Snapshot)]));

        Assert.Contains(Envelope.SnapshotType, error.Message);
    }

    [Fact]
    public void Build_index_rejects_ready_name()
    {
        var error = Assert.Throws<InvalidOperationException>(() => MessageTypes.BuildIndex([typeof(Ready)]));

        Assert.Contains(Envelope.ReadyType, error.Message);
    }
}
