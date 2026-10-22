using MediaStation.Core.Contracts;
using MediaStation.Core.Input;

namespace MediaStation.Core.Tests;

/// <summary>
/// Kolejka Actions (AD-2, AD-8): zapis bez czytelnika nigdy nie czeka i nie gubi Action, kolejność jest FIFO,
/// a <c>wake</c> jest wołany raz na zapis.
/// </summary>
public class ActionQueueTests
{
    private const int MessageCount = 10_000;

    private static Action Message(int index) =>
        new(ActionKind.Ok, ActionPhase.Press, ActionSource.Keyboard, index);

    [Fact]
    public async Task Enqueue_without_reader_never_blocks_and_keeps_every_action_in_fifo_order()
    {
        var queue = new ActionQueue();
        var canceled = TestContext.Current.CancellationToken;

        var producer = Task.Run(
            () =>
            {
                for (var i = 0; i < MessageCount; i++)
                {
                    queue.Enqueue(Message(i));
                }
            },
            canceled);

        await producer.WaitAsync(TimeSpan.FromSeconds(30), canceled);

        var received = new List<Action>();
        while (queue.TryDequeue(out var action))
        {
            received.Add(action);
        }

        Assert.Equal(MessageCount, received.Count);
        Assert.Equal(
            Enumerable.Range(0, MessageCount).Select(index => (long)index),
            received.Select(action => action.TsMono));
    }

    [Fact]
    public void Try_dequeue_on_empty_queue_returns_false()
    {
        var queue = new ActionQueue();

        Assert.False(queue.TryDequeue(out var action));
        Assert.Null(action);
    }

    [Fact]
    public void Wake_is_called_once_per_enqueue()
    {
        var wakes = 0;
        var queue = new ActionQueue(() => wakes++);

        queue.Enqueue(Message(1));
        queue.Enqueue(Message(2));

        Assert.Equal(2, wakes);
    }

    [Fact]
    public void Wake_runs_after_the_action_is_queued()
    {
        ActionQueue? queue = null;
        Action? dequeued = null;
        queue = new ActionQueue(() => Assert.True(queue!.TryDequeue(out dequeued)));

        var message = Message(1);
        queue.Enqueue(message);

        Assert.Equal(message, dequeued);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void Null_action_is_rejected()
    {
        var wakes = 0;
        var queue = new ActionQueue(() => wakes++);

        Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!));
        Assert.Equal(0, wakes);
        Assert.False(queue.TryDequeue(out _));
    }
}
