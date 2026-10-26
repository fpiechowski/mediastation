using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Platform.Audio;

/// <summary>
/// Prawdziwy AudioRouter (AD-15): jedyny kod wyciszający sesje audio. <see cref="Execute"/> tylko kolejkuje komendę
/// i budzi wątek <c>audio-router</c> (MTA, AD-14), więc pętla Orchestratora nigdy nie woła COM. Wątek trzyma regułę
/// drzewa w <see cref="AudioRouterEngine"/>, odpowiada <see cref="CommandCompleted"/> albo
/// <see cref="CommandFailed"/> (<see cref="ErrorCode.AudioUnavailable"/>) i sam skanuje sesje po notyfikacji
/// nowej sesji lub urządzenia oraz co sekundę, dopóki jakieś drzewo jest wyciszone. Wyjątki są łapane na granicy (AD-4).
/// </summary>
internal sealed class Win32AudioRouterPort : IAudioRouterPort, IDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ILog _log;
    private readonly Action<IOrchestratorMessage> _post;
    private readonly ConcurrentQueue<AudioCommand> _queue = new();
    private readonly AutoResetEvent _wake = new(initialState: false);
    private readonly Thread _thread;
    private volatile bool _stopping;
    private int _sweepRequested;
    private volatile bool _dead;
    private volatile bool _started;
    private volatile bool _disposed;

    public Win32AudioRouterPort(ILog log, Action<IOrchestratorMessage> post)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(post);

        _log = log;
        _post = post;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "audio-router",
        };

        // COM Core Audio dostarcza notyfikacje sesji tylko do wątków MTA.
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    /// <summary>Startuje wątek AudioRoutera; kolejne wywołanie jest błędem wołającego.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("AudioRouter został już wystartowany.");
        }

        _started = true;
        _thread.Start();
    }

    /// <summary>
    /// Kolejkuje komendę i budzi wątek; nie blokuje się na COM i nie rzuca. Martwy, niewystartowany albo zwolniony
    /// port odpowiada <see cref="CommandFailed"/> (<see cref="ErrorCode.Unexpected"/>), nigdy ciszą.
    /// </summary>
    public void Execute(AudioCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            if (_dead || _disposed || !_started)
            {
                Fail(command);
                return;
            }

            _queue.Enqueue(command);
            if (_dead)
            {
                // Wątek skończył między sprawdzeniem a kolejką: nikt już nie opróżni kolejki.
                FailQueued();
            }
            else
            {
                _wake.Set();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(AudioRouterEngine.Src, "audioCommandQueueFailed", ex);
        }
    }

    /// <summary>Kończy wątek i czeka na niego; nieodebrane komendy są logowane.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_started)
        {
            return;
        }

        _stopping = true;
        _wake.Set();
        if (_thread.Join(ShutdownTimeout))
        {
            _wake.Dispose();
            return;
        }

        _log.Warn(AudioRouterEngine.Src, "audioRouterShutdownTimeout", new Dictionary<string, object?>
        {
            ["droppedCommands"] = _queue.Count,
        });
    }

    private void RequestSweep()
    {
        Volatile.Write(ref _sweepRequested, 1);
        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // Notyfikacja po zwolnieniu portu: nic do zrobienia.
        }
    }

    private void Fail(AudioCommand command)
    {
        _log.Warn(AudioRouterEngine.Src, "audioCommand", new Dictionary<string, object?>
        {
            ["type"] = command.GetType().Name,
            ["cmdId"] = command.CmdId,
            ["result"] = ErrorCode.Unexpected.ToString(),
        });
        _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
    }

    private void FailQueued()
    {
        while (_queue.TryDequeue(out var command))
        {
            try
            {
                Fail(command);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.Error(AudioRouterEngine.Src, "audioCommandQueueFailed", ex);
            }
        }
    }

    private void Run()
    {
        AudioRouterEngine? engine = null;
        try
        {
            engine = new AudioRouterEngine(
                () => new CoreAudioSessionBackend(new global::System.Action(RequestSweep)),
                () => Win32ProcessSnapshot.ParentMap(),
                _log);
            var schedule = new AudioSweepSchedule();
            long? sweepDue = null;
            while (!_stopping)
            {
                var wait = sweepDue is { } due
                    ? TimeSpan.FromMilliseconds(Math.Max(0, due - Environment.TickCount64))
                    : Timeout.InfiniteTimeSpan;
                _wake.WaitOne(wait);
                if (_stopping)
                {
                    break;
                }

                var sweep = Interlocked.Exchange(ref _sweepRequested, 0) == 1 || (sweepDue is { } deadline && Environment.TickCount64 >= deadline);
                var executed = false;
                while (_queue.TryDequeue(out var command))
                {
                    executed = true;
                    Execute(engine, command);
                }

                // Komenda już przeskanowała sesje; skan na żądanie dokłada się tylko bez komendy albo po notyfikacji.
                if (sweep)
                {
                    engine.Sweep();
                }
                else if (!executed)
                {
                    continue;
                }

                sweepDue = schedule.NextDelay(engine.LastSweepUnresolved, engine.AnyMuted || engine.HasMutedSessions) is { } delay
                    ? Environment.TickCount64 + (long)delay.TotalMilliseconds
                    : null;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(AudioRouterEngine.Src, "audioRouterThreadFailed", ex);
        }
        finally
        {
            _dead = true;
            engine?.Dispose();

            // Komendy, które nie zdążyły się wykonać, dostają porażkę zamiast ciszy.
            FailQueued();
        }
    }

    private void Execute(AudioRouterEngine engine, AudioCommand command)
    {
        ErrorCode? error;
        try
        {
            error = engine.Execute(command);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = ErrorCode.Unexpected;
            _log.Error(AudioRouterEngine.Src, "audioCommandFailed", ex, new Dictionary<string, object?> { ["cmdId"] = command.CmdId });
        }

        try
        {
            if (error is { } code)
            {
                _post(new CommandFailed(command.CmdId, code));
            }
            else
            {
                _post(new CommandCompleted(command.CmdId));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Raport nie może przepuścić wyjątku przez granicę portu (AD-4).
            _log.Error(AudioRouterEngine.Src, "audioCommandReportFailed", ex);
        }
    }
}
