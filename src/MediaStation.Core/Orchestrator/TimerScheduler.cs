using System;
using System.Collections.Generic;
using System.Threading;
using MediaStation.Core.Contracts;
using MediaStation.Core.Ports;

namespace MediaStation.Core.Orchestrator;

/// <summary>
/// Jedyny wykonawca timerów (AD-14): liczy termin wyłącznie na wstrzykniętym <see cref="TimeProvider"/>
/// i publikuje <see cref="TimerElapsed"/> do kolejki Orchestratora. Timer jest jednorazowy, a usunięcie wpisu
/// następuje przed publikacją, więc spóźniony callback anulowanego terminu nic nie wysyła.
/// Wątkowo bezpieczny (callback przychodzi z wątku <see cref="TimeProvider"/>); po <see cref="Dispose"/>
/// nie publikuje i zwalnia wszystkie timery.
/// </summary>
public sealed class TimerScheduler : ITimerPort, IDisposable
{
    private readonly TimeProvider _time;
    private readonly Action<IOrchestratorMessage> _post;
    private readonly Dictionary<long, Entry> _timers = [];
    private readonly object _gate = new();
    private bool _disposed;

    public TimerScheduler(TimeProvider time, Action<IOrchestratorMessage> post)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);

        _time = time;
        _post = post;
    }

    /// <summary>Planuje albo anuluje timer; duplikat <c>timerId</c> zastępuje poprzedni termin.</summary>
    public void Execute(TimerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (command)
        {
            case ScheduleTimer schedule:
                Schedule(schedule);
                break;
            case CancelTimer cancel:
                Cancel(cancel.TimerId);
                break;
        }
    }

    /// <summary>Zwalnia wszystkie timery i blokuje dalszą publikację; kolejne wywołanie jest no-opem.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _timers.Values)
            {
                entry.Timer?.Dispose();
            }

            _timers.Clear();
        }
    }

    /// <summary>
    /// Termin w przeszłości (albo równy teraz) liczy się jako zero, żeby timer odpalił natychmiast
    /// po najbliższym przebiegu zegara, a nie został odrzucony.
    /// </summary>
    private void Schedule(ScheduleTimer command)
    {
        var delay = command.Due - _time.GetUtcNow();
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_timers.Remove(command.TimerId, out var previous))
            {
                previous.Timer?.Dispose();
            }

            var entry = new Entry();
            _timers.Add(command.TimerId, entry);

            try
            {
                entry.Timer = _time.CreateTimer(
                    _ => OnDue(command.TimerId, entry),
                    state: null,
                    dueTime: delay,
                    period: Timeout.InfiniteTimeSpan);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Port nie przepuszcza wyjątku (AD-4): provider odrzucający termin (np. za daleki dla
                // TimeProvider.System) nie może zostawić wiecznie oczekującego wpisu ani wywrócić dispatchu.
                if (_timers.TryGetValue(command.TimerId, out var current) && ReferenceEquals(current, entry))
                {
                    _timers.Remove(command.TimerId);
                }
            }
        }
    }

    /// <summary>Usuwa termin przed publikacją: nieznany albo już zużyty <c>timerId</c> jest no-opem.</summary>
    private void Cancel(long timerId)
    {
        lock (_gate)
        {
            if (_timers.Remove(timerId, out var entry))
            {
                entry.Timer?.Dispose();
            }
        }
    }

    /// <summary>
    /// Callback timera: publikuje wyłącznie wtedy, gdy wpis wciąż jest tym samym terminem (nie został
    /// anulowany ani zastąpiony). Publikacja pod blokadą domyka wyścig z <see cref="Dispose"/>.
    /// </summary>
    private void OnDue(long timerId, Entry entry)
    {
        lock (_gate)
        {
            if (_disposed
                || !_timers.TryGetValue(timerId, out var current)
                || !ReferenceEquals(current, entry))
            {
                return;
            }

            _timers.Remove(timerId);
            entry.Timer?.Dispose();
            _post(new TimerElapsed(timerId));
        }
    }

    /// <summary>Wpis terminu; <see cref="Timer"/> jest przypisywany po <c>CreateTimer</c>, bo callback może przyjść wcześniej.</summary>
    private sealed class Entry
    {
        public ITimer? Timer { get; set; }
    }
}
