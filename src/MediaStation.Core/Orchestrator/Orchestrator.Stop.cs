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
    /// Zamknięcie albo restart aplikacji z <c>Background</c> albo <c>Failed</c>: Orchestrator przechodzi w <c>Stopping</c>
    /// i wysyła <c>StopApp</c> z <c>cmdId</c> w korelacji stopu, a na koniec batcha planuje timeout <c>Stopping</c>.
    /// Restart jest decyzją o pierwszym planie podjętą już tutaj: nowe <c>transitionId</c> powstaje w chwili decyzji
    /// i jedzie w <see cref="PendingStop"/>, więc Home albo wybudzenie w trakcie <c>Stopping</c> wygrywa z restartem.
    /// <c>Failed</c> wychodzi więc wyłącznie przez stop: potwierdzony stop kończy się <c>NotRunning</c> albo nowym
    /// <c>Starting</c>. Poza tymi stanami intencja jest ignorowana, podobnie gdy innemu zadaniu należy się pierwszy
    /// plan: restart skończyłby się w <c>Starting</c> → <c>Foreground</c>, czyli w dwóch aplikacjach naraz.
    /// </summary>
    private OrchestratorResult OnStop(string appId, bool restart)
    {
        var intent = restart ? nameof(RestartApp) : nameof(CloseApp);
        if (!_appsById.TryGetValue(appId, out var app))
        {
            return Ignored(intent, appId, "nieznany appId");
        }

        if (app.State is not (AppLifecycleState.Background or AppLifecycleState.Failed))
        {
            return Ignored(intent, appId, $"stan {app.State} nie pozwala na zatrzymanie");
        }

        if (AnotherAppIsActive(app))
        {
            return Ignored(intent, appId, "inna aplikacja jest aktywna");
        }

        // Odciszenie przed stopem: wyciszenie dziedziczone po zamkniętej aplikacji nie zostaje w jej następnej instancji.
        var commands = new List<Command>();
        if (app.RootPid > 0)
        {
            commands.Add(new UnmuteProcessTree(NextCmdId(), app.RootPid));
        }

        var cmdId = NextCmdId();
        var transitionId = restart ? NextTransitionId() : 0;
        app.LateWindowExpected = false;
        app.State = AppLifecycleState.Stopping;
        _pendingStops.Add(cmdId, new PendingStop(app.AppId, restart, transitionId));
        commands.Add(new StopApp(cmdId, app.AppId));
        commands.Add(ScheduleTimeout(_stopTimers, app.AppId, _stoppingTimeout));
        return new OrchestratorResult(commands, Snapshot());
    }

    /// <summary>
    /// Sukces komendy domyka najpierw stop (<c>Stopping</c> → <c>NotRunning</c> albo <c>Starting</c> przy restarcie),
    /// a dopiero potem korelację startu. Sukces startu nie zmienia stanu: <c>Foreground</c> daje
    /// <see cref="AppWindowAttached"/>.
    /// </summary>
    private OrchestratorResult OnCommandCompleted(CommandCompleted completed)
    {
        if (_pendingStops.Remove(completed.CmdId, out var stopped))
        {
            return OnStopped(stopped);
        }

        _pendingStarts.Remove(completed.CmdId);
        return OrchestratorResult.None;
    }

    /// <summary>
    /// Wspólny koniec stopu: restart startuje aplikację od nowa (i zdejmuje timeout <c>Stopping</c> na końcu batcha),
    /// zamknięcie kończy się w <c>NotRunning</c> z samym anulowaniem timeoutu <c>Stopping</c>. Restart używa
    /// <c>transitionId</c> z chwili decyzji w <see cref="OnStop"/>, nie z potwierdzenia stopu: inaczej stop
    /// dokończony po Home albo wybudzeniu wyniósłby aplikację na wierzch nad shellem. Gdy w trakcie <c>Stopping</c>
    /// inna aplikacja wzięła pierwszy plan, restart kończy się jak zamknięcie: dwie aplikacje w <c>Foreground</c>
    /// byłyby gorsze niż utracony restart.
    /// </summary>
    private OrchestratorResult OnStopped(PendingStop stop)
    {
        var app = _appsById[stop.AppId];
        app.Hwnd = null;

        // Stop potwierdzony = proces nie żyje: dopiero teraz reguła wyciszenia może zniknąć (przed StartApp restartu).
        var forget = ForgetRule(app);
        if (!stop.Restart || AnotherAppIsActive(app))
        {
            app.State = AppLifecycleState.NotRunning;
            var commands = new List<Command>();
            if (forget is not null)
            {
                commands.Add(forget);
            }
            if (CancelTimeout(_stopTimers, app.AppId) is { } cancel)
            {
                commands.Add(cancel);
            }

            return new OrchestratorResult(commands, Snapshot());
        }

        var cmdId = NextCmdId();
        app.ForegroundTransition = stop.TransitionId;
        app.State = AppLifecycleState.Starting;
        _pendingStarts.Add(cmdId, app.AppId);
        var restartCommands = new List<Command>();
        if (forget is not null)
        {
            restartCommands.Add(forget);
        }

        restartCommands.Add(new StartApp(cmdId, app.AppId));
        restartCommands.Add(ScheduleStartTimeout(app.AppId));
        if (CancelTimeout(_stopTimers, app.AppId) is { } stopTimeout)
        {
            restartCommands.Add(stopTimeout);
        }

        return new OrchestratorResult(restartCommands, Snapshot());
    }

    /// <summary><c>cmdId</c> korelacji stopu dla aplikacji; <c>null</c>, gdy stop nie jest w toku.</summary>
    private long? FindPendingStop(string appId)
    {
        foreach (var (cmdId, pending) in _pendingStops)
        {
            if (pending.AppId == appId)
            {
                return cmdId;
            }
        }

        return null;
    }
}
