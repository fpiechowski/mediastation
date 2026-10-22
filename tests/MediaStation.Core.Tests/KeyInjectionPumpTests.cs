using System.Diagnostics;
using MediaStation.App.Platform.Input;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>
/// Pompa wstrzyknięć (AD-3, AD-4, AD-8, AD-14): <c>Execute</c> tylko kolejkuje, <c>SendInput</c> idzie na własnym
/// wątku <c>key-injection</c> w kolejności komend, a wpisy logu niosą wyłącznie <c>cmdId</c> (AD-13). Wyjątek
/// i kod spoza 1–255 kończą się <c>warn keyInjectionFailed</c> i nie zatrzymują wątku ani nie przechodzą przez port.
/// Testy nie dotykają Win32: wstrzyknięcie jest delegatem <c>Func&lt;int, bool&gt;</c>.
/// </summary>
public class KeyInjectionPumpTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Execute_only_queues_and_injects_on_a_different_thread()
    {
        var injector = new RecordingInjector();
        using var pump = new KeyInjectionPump(injector.Send, new RecordingLog());
        pump.Start();

        var callingThread = Environment.CurrentManagedThreadId;
        pump.Execute(new SendKey(1, 0xAC));

        Assert.True(injector.WaitForAttempts(1, Timeout));
        Assert.NotEqual(callingThread, injector.AttemptThread);
    }

    [Fact]
    public void Commands_are_injected_in_queue_order()
    {
        var injector = new RecordingInjector();
        var log = new RecordingLog();
        using var pump = new KeyInjectionPump(injector.Send, log);
        pump.Start();

        pump.Execute(new SendKey(11, 0x41));
        pump.Execute(new SendKey(12, 0x42));

        Assert.True(WaitFor(() => log.Entries.Count >= 2));
        Assert.Equal([0x41, 0x42], injector.Sent);
        Assert.Equal([11L, 12L], log.Entries.Select(entry => (long)entry.Data!["cmdId"]!));
    }

    [Fact]
    public void Successful_injection_logs_info_with_cmd_id_only()
    {
        var injector = new RecordingInjector();
        var log = new RecordingLog();
        using var pump = new KeyInjectionPump(injector.Send, log);
        pump.Start();

        pump.Execute(new SendKey(21, 0xAC));

        Assert.True(WaitFor(() => log.Entries.Count >= 1));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal(KeyInjectionPump.Src, entry.Src);
        Assert.Equal("keyInjection", entry.Msg);
        Assert.Equal(["cmdId"], entry.Data!.Keys);
        Assert.Equal(21L, entry.Data["cmdId"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(256)]
    [InlineData(int.MaxValue)]
    public void Out_of_range_key_logs_warn_without_calling_send_key(int virtualKey)
    {
        var injector = new RecordingInjector();
        var log = new RecordingLog();
        using var pump = new KeyInjectionPump(injector.Send, log);
        pump.Start();

        pump.Execute(new SendKey(31, virtualKey));
        pump.Execute(new SendKey(32, 0x41));

        Assert.True(WaitFor(() => log.Entries.Count >= 2));

        // Kolejność wpisów = kolejność komend: odrzucony zakres kończy się warnem, a nie zatrzymuje wątku,
        // więc następna poprawna komenda dochodzi i daje info.
        var failed = log.Entries[0];
        Assert.Equal(LogLevel.Warn, failed.Level);
        Assert.Equal(KeyInjectionPump.Src, failed.Src);
        Assert.Equal("keyInjectionFailed", failed.Msg);
        Assert.Equal(["cmdId"], failed.Data!.Keys);
        Assert.Equal(31L, failed.Data["cmdId"]);

        var injected = log.Entries[1];
        Assert.Equal(LogLevel.Info, injected.Level);
        Assert.Equal(KeyInjectionPump.Src, injected.Src);
        Assert.Equal("keyInjection", injected.Msg);
        Assert.Equal(["cmdId"], injected.Data!.Keys);
        Assert.Equal(32L, injected.Data["cmdId"]);
        Assert.Equal([0x41], injector.Sent);
    }

    [Fact]
    public void Send_key_returning_false_logs_warn()
    {
        var injector = new RecordingInjector { FailOnVk = 0xAC };
        var log = new RecordingLog();
        using var pump = new KeyInjectionPump(injector.Send, log);
        pump.Start();

        pump.Execute(new SendKey(41, 0xAC));

        Assert.True(injector.WaitForAttempts(1, Timeout));
        Assert.True(WaitFor(() => log.Entries.Count >= 1));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("keyInjectionFailed", entry.Msg);
        Assert.Equal(41L, entry.Data!["cmdId"]);
    }

    [Fact]
    public void Send_key_exception_logs_warn_and_the_thread_survives_the_next_command()
    {
        var injector = new RecordingInjector { ThrowOnVk = 0xAC };
        var log = new RecordingLog();
        using var pump = new KeyInjectionPump(injector.Send, log);
        pump.Start();

        pump.Execute(new SendKey(51, 0xAC));
        Assert.True(injector.WaitForAttempts(1, Timeout));

        pump.Execute(new SendKey(52, 0x41));

        Assert.True(injector.WaitForSent(1, Timeout));
        Assert.Equal([0x41], injector.Sent);
        Assert.True(WaitFor(() => log.Entries.Count >= 2));
        Assert.Equal(["keyInjectionFailed", "keyInjection"], log.Messages());
        Assert.Equal(51L, log.Entries[0].Data!["cmdId"]);
        Assert.Equal(52L, log.Entries[1].Data!["cmdId"]);
    }

    [Fact]
    public void Dispose_without_start_and_twice_does_not_throw_or_inject()
    {
        var injector = new RecordingInjector();
        var pump = new KeyInjectionPump(injector.Send, new RecordingLog());

        var exception = Record.Exception(() =>
        {
            pump.Dispose();
            pump.Dispose();
        });

        Assert.Null(exception);
        Assert.Empty(injector.Sent);
    }

    [Fact]
    public void Execute_after_dispose_is_a_no_op()
    {
        var injector = new RecordingInjector();
        var pump = new KeyInjectionPump(injector.Send, new RecordingLog());
        pump.Start();
        pump.Dispose();

        var exception = Record.Exception(() => pump.Execute(new SendKey(61, 0xAC)));

        Assert.Null(exception);
        Assert.Empty(injector.Sent);
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

    /// <summary>Delegat wstrzyknięcia zapisujący próby; wyjątek i porażkę wybiera VK, żeby sterować jedną komendą.</summary>
    private sealed class RecordingInjector
    {
        private readonly object _gate = new();
        private readonly List<int> _sent = [];
        private int _attempts;
        private int _attemptThread;

        /// <summary>VK, dla którego delegat rzuca (tak zachowa się przyszły wariant Unicode/Alt).</summary>
        public int? ThrowOnVk { get; init; }

        /// <summary>VK, dla którego delegat zwraca <c>false</c> (system nic nie wstawił).</summary>
        public int? FailOnVk { get; init; }

        public IReadOnlyList<int> Sent
        {
            get
            {
                lock (_gate)
                {
                    return _sent.ToArray();
                }
            }
        }

        public int Attempts => Volatile.Read(ref _attempts);

        public int AttemptThread => Volatile.Read(ref _attemptThread);

        public bool Send(int virtualKey)
        {
            lock (_gate)
            {
                _attemptThread = Environment.CurrentManagedThreadId;
            }

            Interlocked.Increment(ref _attempts);

            if (ThrowOnVk == virtualKey)
            {
                throw new InvalidOperationException("Wstrzyknięcie padło.");
            }

            if (FailOnVk == virtualKey)
            {
                return false;
            }

            lock (_gate)
            {
                _sent.Add(virtualKey);
            }

            return true;
        }

        public bool WaitForAttempts(int count, TimeSpan timeout) =>
            WaitFor(() => Volatile.Read(ref _attempts) >= count, timeout);

        public bool WaitForSent(int count, TimeSpan timeout) =>
            WaitFor(() => Sent.Count >= count, timeout);

        private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < timeout)
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
}
