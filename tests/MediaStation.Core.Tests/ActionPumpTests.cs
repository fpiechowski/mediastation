using System.Diagnostics;
using MediaStation.App.Platform.Input;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;
using Microsoft.Extensions.Time.Testing;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Tests;

/// <summary>
/// Pompa Actions (AD-2, AD-8): opróżnia kolejkę na własnym wątku w kolejności FIFO, domyka resztę kolejki
/// przy <c>Dispose</c> i nie umiera po wyjątku sinka — porażka ląduje jako
/// <c>inputHubError {operation: actionSink}</c>.
/// </summary>
public class ActionPumpTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Delivers_actions_in_order_on_its_own_thread_and_dispose_ends_it()
    {
        var sink = new RecordingSink();
        var errors = new RecordingErrorSink();
        var pump = new ActionPump(sink, errors);
        pump.Start();

        pump.Queue.Enqueue(Message(0));
        pump.Queue.Enqueue(Message(1));
        pump.Queue.Enqueue(Message(2));

        Assert.True(sink.WaitForDelivered(3, Timeout));
        var pumpThread = sink.DeliveryThread;

        pump.Dispose();

        Assert.Equal([0L, 1L, 2L], sink.Delivered.Select(action => action.TsMono));
        Assert.NotNull(pumpThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, pumpThread!.ManagedThreadId);
        Assert.False(pumpThread.IsAlive);
        Assert.Empty(errors.Errors);
    }

    [Fact]
    public void Dispose_delivers_actions_left_in_the_queue()
    {
        var sink = new RecordingSink();
        var pump = new ActionPump(sink, new RecordingErrorSink());

        // Pompa nie wystartowała, więc jedyną drogą dostarczenia jest Drain wołany z Dispose.
        pump.Queue.Enqueue(Message(0));
        pump.Queue.Enqueue(Message(1));
        pump.Queue.Enqueue(Message(2));

        pump.Dispose();

        Assert.Equal([0L, 1L, 2L], sink.Delivered.Select(action => action.TsMono));
    }

    [Fact]
    public void Sink_exception_is_reported_and_the_next_action_is_still_delivered()
    {
        var sink = new RecordingSink { ThrowOnAttempt = 1 };
        var errors = new RecordingErrorSink();
        var pump = new ActionPump(sink, errors);
        pump.Start();

        pump.Queue.Enqueue(Message(1));
        Assert.True(sink.WaitForAttempts(1, Timeout));

        pump.Queue.Enqueue(Message(2));

        Assert.True(sink.WaitForDelivered(1, Timeout));
        Assert.True(errors.WaitFor(1, Timeout));

        Assert.Equal(2L, Assert.Single(sink.Delivered).TsMono);
        var error = Assert.Single(errors.Errors);
        Assert.Equal(InputHubOperations.ActionSink, error.Operation);
        Assert.Null(error.ErrorCode);

        pump.Dispose();
    }

    [Fact]
    public void Policy_decisions_from_the_queue_produce_one_action_entry_for_short_and_long_enter()
    {
        var log = new RecordingLog();
        var sink = new LogActionSink(log);
        var pump = new ActionPump(sink, sink);
        pump.Start();

        var time = new FakeTimeProvider(T0);
        var policy = new InputPolicy(Input(), time);

        // Krótkie naciśnięcie: `press` na key-up przed progiem long-press.
        EnqueueDecision(pump.Queue, policy.OnKey(new KeyStroke(0x0D, KeyTransition.Down, Injected: false, TimeMs: 1000)));
        EnqueueDecision(pump.Queue, policy.OnKey(new KeyStroke(0x0D, KeyTransition.Up, Injected: false, TimeMs: 1200)));

        // Długie naciśnięcie: samo `long` z terminu, key-up bez kolejnej Action.
        EnqueueDecision(pump.Queue, policy.OnKey(new KeyStroke(0x0D, KeyTransition.Down, Injected: false, TimeMs: 2000)));
        time.Advance(TimeSpan.FromMilliseconds(600));
        EnqueueDecision(pump.Queue, policy.OnTimer(time.GetUtcNow()));
        EnqueueDecision(pump.Queue, policy.OnKey(new KeyStroke(0x0D, KeyTransition.Up, Injected: false, TimeMs: 2700)));

        Assert.True(WaitFor(() => log.Entries.Count >= 2));
        pump.Dispose();

        var entries = log.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(ActionPhase.Press, entries[0].Data!["phase"]);
        Assert.Equal(ActionPhase.Long, entries[1].Data!["phase"]);

        foreach (var entry in entries)
        {
            Assert.Equal(LogActionSink.Src, entry.Src);
            Assert.Equal("action", entry.Msg);
            Assert.Equal(ActionKind.Ok, entry.Data!["kind"]);
            Assert.Equal(ActionSource.Keyboard, entry.Data["source"]);

            var line = JsonLinesLog.Format(T0, entry.Level, entry.Src, entry.Msg, entry.Data, null);
            Assert.DoesNotContain("tsMono", line);
            Assert.DoesNotContain("\"vk\"", line);
            Assert.DoesNotContain("\"scan\"", line);
            Assert.DoesNotContain("\"usage\"", line);
        }
    }

    [Fact]
    public void Dispose_without_start_is_a_no_op()
    {
        var sink = new RecordingSink();
        var pump = new ActionPump(sink, new RecordingErrorSink());

        pump.Dispose();
        pump.Dispose();

        Assert.Empty(sink.Delivered);
    }

    private static ActionMessage Message(int tsMono) =>
        new(ActionKind.Ok, ActionPhase.Press, ActionSource.Keyboard, tsMono);

    private static InputConfig Input() => new(
        [new InputDevice(
            "VID_1915&PID_1025",
            ActionSource.Remote,
            new Dictionary<string, ActionKind> { ["0x0D"] = ActionKind.Ok })],
        [GamepadButton.Back, GamepadButton.Start])
    {
        Timing = new InputTiming { LongPressMs = 600 },
    };

    private static void EnqueueDecision(ActionQueue queue, InputDecision decision)
    {
        foreach (var action in decision.Actions)
        {
            queue.Enqueue(action);
        }
    }

    private static bool WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < Timeout)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    /// <summary>Sink zapisujący dostarczone Actions; kolejne próby numeruje, żeby wyjątek trafił tylko w jedną.</summary>
    private sealed class RecordingSink : IActionSink
    {
        private readonly object _gate = new();
        private readonly List<ActionMessage> _delivered = [];
        private readonly ManualResetEventSlim _changed = new(initialState: false);
        private int _attempts;

        /// <summary>Numer próby (od 1), na której <see cref="Deliver"/> rzuca; <c>-1</c> wyłącza wyjątek.</summary>
        public int ThrowOnAttempt { get; init; } = -1;

        public Thread? DeliveryThread { get; private set; }

        public IReadOnlyList<ActionMessage> Delivered
        {
            get
            {
                lock (_gate)
                {
                    return _delivered.ToArray();
                }
            }
        }

        public void Deliver(ActionMessage action)
        {
            int attempt;
            lock (_gate)
            {
                attempt = ++_attempts;
            }

            try
            {
                if (attempt == ThrowOnAttempt)
                {
                    throw new InvalidOperationException("Sink padł na tej próbie.");
                }

                lock (_gate)
                {
                    DeliveryThread ??= Thread.CurrentThread;
                    _delivered.Add(action);
                }
            }
            finally
            {
                _changed.Set();
            }
        }

        public bool WaitForDelivered(int count, TimeSpan timeout) => WaitFor(() => Delivered.Count, count, timeout);

        public bool WaitForAttempts(int count, TimeSpan timeout) => WaitFor(() => Volatile.Read(ref _attempts), count, timeout);

        private bool WaitFor(Func<int> read, int count, TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < timeout)
            {
                if (read() >= count)
                {
                    return true;
                }

                _changed.Wait(TimeSpan.FromMilliseconds(10));
            }

            return read() >= count;
        }
    }

    private sealed class RecordingErrorSink : IInputHubErrorSink
    {
        private readonly object _gate = new();
        private readonly List<InputHubError> _errors = [];
        private readonly ManualResetEventSlim _changed = new(initialState: false);

        public IReadOnlyList<InputHubError> Errors
        {
            get
            {
                lock (_gate)
                {
                    return _errors.ToArray();
                }
            }
        }

        public void Report(InputHubError error)
        {
            lock (_gate)
            {
                _errors.Add(error);
            }

            _changed.Set();
        }

        public bool WaitFor(int count, TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < timeout)
            {
                lock (_gate)
                {
                    if (_errors.Count >= count)
                    {
                        return true;
                    }
                }

                _changed.Wait(TimeSpan.FromMilliseconds(10));
            }

            lock (_gate)
            {
                return _errors.Count >= count;
            }
        }
    }
}
