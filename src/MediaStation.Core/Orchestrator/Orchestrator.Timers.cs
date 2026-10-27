using System;
using System.Collections.Generic;
using System.Linq;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Orchestrator;

public sealed partial class Orchestrator
{
    /// <summary>
    /// Timeout <c>Starting</c> albo <c>Stopping</c>: o żywotności terminu decyduje wyłącznie korelacja
    /// <c>timerId → appId</c>, więc spóźniony <see cref="TimerElapsed"/> po potwierdzonym starcie/stopie,
    /// po <see cref="AppWindowAttached"/> albo po wejściu w <c>Failed</c> nie daje komend ani snapshotu.
    /// Żywy termin kończy start albo stop w <c>Failed</c> bez shella i fokusu (nie ma okna na pierwszym planie).
    /// Korelacja znika przed <see cref="EnterFailed"/>, więc timeout nie anuluje sam siebie.
    /// </summary>
    private OrchestratorResult OnTimerElapsed(TimerElapsed elapsed)
    {
        if (_settleTimerId != 0 && elapsed.TimerId == _settleTimerId)
        {
            return OnSettled();
        }

        if (_startTimers.Remove(elapsed.TimerId, out var appId))
        {
            return _appsById.TryGetValue(appId, out var app) && app.State == AppLifecycleState.Starting
                ? EnterFailed(app, wasForeground: false, processAlive: true)
                : OrchestratorResult.None;
        }

        if (_stopTimers.Remove(elapsed.TimerId, out var stoppingAppId))
        {
            return _appsById.TryGetValue(stoppingAppId, out var app) && app.State == AppLifecycleState.Stopping
                ? EnterFailed(app, wasForeground: false, processAlive: true)
                : OrchestratorResult.None;
        }

        return OrchestratorResult.None;
    }

    /// <summary>
    /// Planuje timeout na wstrzykniętym zegarze: <c>timerId</c> nadaje wyłącznie Orchestrator, a korelacja
    /// <c>timerId → appId</c> żyje w przekazanej mapie, więc spóźniony <see cref="TimerElapsed"/> odpada bez
    /// zaglądania w stan schedulera. Komenda timera idzie na koniec batcha. Jeden licznik <c>timerId</c> dla obu
    /// timeoutów daje globalnie unikalne identyfikatory.
    /// </summary>
    private ScheduleTimer ScheduleTimeout(Dictionary<long, string> timers, string appId, TimeSpan timeout)
    {
        var timerId = ++_timerId;
        timers.Add(timerId, appId);
        return new ScheduleTimer(NextCmdId(), timerId, appId, _time.GetUtcNow() + timeout);
    }

    /// <summary>
    /// Zdejmuje korelację timeoutu aplikacji i zwraca komendę anulowania; <c>null</c>, gdy termin już nie żyje
    /// (np. po <see cref="AppWindowAttached"/>, potwierdzonym stopie albo po samym timeoucie).
    /// </summary>
    private CancelTimer? CancelTimeout(Dictionary<long, string> timers, string appId)
    {
        var timerId = FindTimeout(timers, appId);
        if (timerId is not { } id)
        {
            return null;
        }

        timers.Remove(id);
        return new CancelTimer(NextCmdId(), id);
    }

    /// <summary><c>timerId</c> timeoutu aplikacji w mapie; <c>null</c>, gdy termin już nie żyje.</summary>
    private static long? FindTimeout(Dictionary<long, string> timers, string appId)
    {
        foreach (var (timerId, owner) in timers)
        {
            if (owner == appId)
            {
                return timerId;
            }
        }

        return null;
    }

    /// <summary>Timeout <c>Starting</c> aplikacji na <c>startingTimeout</c>.</summary>
    private ScheduleTimer ScheduleStartTimeout(string appId) => ScheduleTimeout(_startTimers, appId, _startingTimeout);

    /// <summary>Anulowanie timeoutu <c>Starting</c>; <c>null</c>, gdy termin już nie żyje.</summary>
    private CancelTimer? CancelStartTimeout(string appId) => CancelTimeout(_startTimers, appId);
}
