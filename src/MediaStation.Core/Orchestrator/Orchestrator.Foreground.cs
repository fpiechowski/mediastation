using System;
using System.Collections.Generic;
using System.Linq;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Orchestrator;

public sealed partial class Orchestrator
{
    /// <summary>Jak skończyła się komenda okienna, na którą Orchestrator czekał (AD-15).</summary>
    private enum WindowResult
    {
        None,
        Completed,
        Failed,
    }

    /// <summary>
    /// Zmiana okna na pierwszym planie (AD-8, AD-15): właściciel jest rozpoznawany od razu i logowany, ale decyzja
    /// zapada dopiero po ustaleniu. Zapis ostatniej obserwacji restartuje timer <c>foregroundSettleMs</c>; gdy
    /// ocena jest zablokowana (komenda okienna bez wyniku, aplikacja w <c>Starting</c>), stan prosi o
    /// <see cref="ReportForeground"/> po ustaniu przyczyny.
    /// </summary>
    private OrchestratorResult OnForegroundChanged(ForegroundChanged changed)
    {
        var apps = new List<ForegroundApp>(_apps.Count);
        foreach (var app in _apps)
        {
            apps.Add(new ForegroundApp(app.AppId, app.Hwnd ?? 0, app.RootPid));
        }

        var observation = ForegroundOwnerResolver.Resolve(changed.Hwnd, changed.Pid, _foreground?.ShellHwnd ?? 0, apps);
        _observation = observation;
        if (IsHardBlocked())
        {
            _reportNeeded = true;
        }

        var commands = new List<Command>();
        RestartSettleTimer(commands);
        return new OrchestratorResult(commands, null) { Foreground = observation };
    }

    /// <summary>Komenda okienna bez wyniku (poza <see cref="ReportForeground"/>) albo aplikacja w <c>Starting</c> blokuje ocenę.</summary>
    private bool IsHardBlocked() =>
        _pendingWindow.Keys.Any(cmdId => !_reportCmds.Contains(cmdId))
        || _apps.Exists(app => app.State == AppLifecycleState.Starting);

    /// <summary>Anuluje poprzedni timer ustalenia (jeśli żyje) i planuje nowy na <c>foregroundSettleMs</c> od teraz.</summary>
    private void RestartSettleTimer(List<Command> commands)
    {
        CancelSettleTimer(commands);
        _settleTimerId = ++_timerId;
        _settleTransition = _transitionId;
        commands.Add(new ScheduleTimer(NextCmdId(), _settleTimerId, ForegroundTimerOwner, _time.GetUtcNow() + _foregroundSettle));
    }

    private void CancelSettleTimer(List<Command> commands)
    {
        if (_settleTimerId != 0)
        {
            commands.Add(new CancelTimer(NextCmdId(), _settleTimerId));
            _settleTimerId = 0;
        }
    }

    /// <summary>Zdejmuje wynik komendy okiennej ze zbioru oczekujących; <c>None</c> dla każdego innego komunikatu.</summary>
    private WindowResult TakeWindowResult(IOrchestratorMessage message)
    {
        switch (message)
        {
            case CommandCompleted completed when _pendingWindow.Remove(completed.CmdId):
                _reportCmds.Remove(completed.CmdId);
                return WindowResult.Completed;
            case CommandFailed failed when _pendingWindow.Remove(failed.CmdId):
                _reportCmds.Remove(failed.CmdId);
                return WindowResult.Failed;
            default:
                return WindowResult.None;
        }
    }

    /// <summary>
    /// Polityka ustalenia po każdym kroku (AD-15): komendy okienne z wyniku wchodzą do zbioru oczekujących
    /// bieżącego <c>transitionId</c>, nowe przejście albo zmiana zbioru okien unieważnia oczekującą ocenę,
    /// wynik komendy restartuje timer (porażka niezgodnej obserwacji kończy się logiem), a po ustaniu przyczyny
    /// blokady wynik dostaje <see cref="ReportForeground"/>. Na końcu ustawia termin wyników komend okiennych.
    /// </summary>
    private OrchestratorResult ApplyForegroundPolicy(OrchestratorResult result, long transitionBefore, WindowResult windowResult)
    {
        var commands = new List<Command>(result.Commands);
        foreach (var command in result.Commands)
        {
            if (command is WindowCommand window)
            {
                _pendingWindow[window.CmdId] = _transitionId;
            }
        }

        foreach (var cmdId in _pendingWindow.Where(pair => pair.Value != _transitionId).Select(pair => pair.Key).ToList())
        {
            _pendingWindow.Remove(cmdId);
            _reportCmds.Remove(cmdId);
        }

        ForegroundObservation? failedObservation = null;
        if (_observation is not null && (_transitionId != transitionBefore || _windowsChanged))
        {
            CancelSettleTimer(commands);
            _observation = null;
            _reportNeeded = true;
        }
        else if (windowResult == WindowResult.Failed && _observation is { } observed && WouldSwitch(observed))
        {
            failedObservation = observed;
            CancelSettleTimer(commands);
            _observation = null;
            _reportNeeded = false;
        }

        if (_reportNeeded && _pendingWindow.Count == 0 && !_apps.Exists(app => app.State == AppLifecycleState.Starting))
        {
            var report = new ReportForeground(NextCmdId());
            _pendingWindow[report.CmdId] = _transitionId;
            _reportCmds.Add(report.CmdId);
            commands.Add(report);
            CancelSettleTimer(commands);
            _observation = null;
            _reportNeeded = false;
        }

        if (windowResult != WindowResult.None && _observation is not null)
        {
            RestartSettleTimer(commands);
        }

        UpdateWindowResultDeadline(commands, restart: result.Commands.Any(command => command is WindowCommand));

        return result with { Commands = commands, ForegroundTransitionFailed = failedObservation ?? result.ForegroundTransitionFailed };
    }

    /// <summary>
    /// Termin <c>windowResultTimeout</c> żyje tylko wtedy, gdy na wyniki czeka ocena (obserwacja albo prośba o raport),
    /// więc Home i start bez obserwacji nie dają dodatkowych komend, a raport bez czekającej oceny nie ponawia się
    /// bez końca. Nowa komenda okienna restartuje termin: jeden timer wystarcza na cały zbiór, a młode komendy nie
    /// zostają uznane za zgubione.
    /// </summary>
    private void UpdateWindowResultDeadline(List<Command> commands, bool restart)
    {
        var waiting = _pendingWindow.Count > 0 && (_observation is not null || _reportNeeded);
        if (_windowResultTimerId != 0 && (restart || !waiting))
        {
            commands.Add(new CancelTimer(NextCmdId(), _windowResultTimerId));
            _windowResultTimerId = 0;
        }

        if (_windowResultTimerId == 0 && waiting)
        {
            _windowResultTimerId = ++_timerId;
            commands.Add(new ScheduleTimer(NextCmdId(), _windowResultTimerId, WindowResultTimerOwner, _time.GetUtcNow() + _windowResultTimeout));
        }
    }

    /// <summary>
    /// Termin wyników minął (AD-15): wykonawca złamał AD-4, więc oczekujące komendy są uznane za zgubione, bez
    /// ponawiania i bez cofania stanu. Po zgubionej komendzie poza <see cref="ReportForeground"/> polityka prosi o świeży
    /// raport, bo zapisana obserwacja była zablokowana i nie może zostać oceniona. Po zgubionym raporcie zachowana
    /// obserwacja nie była zablokowana, więc wystarczy ją ustalić, bez kolejnego raportu.
    /// </summary>
    private OrchestratorResult OnWindowResultTimeout()
    {
        _windowResultTimerId = 0;
        var lost = _pendingWindow.Keys.Order().ToArray();
        var hard = lost.Any(cmdId => !_reportCmds.Contains(cmdId));
        _pendingWindow.Clear();
        _reportCmds.Clear();

        var commands = new List<Command>();
        if (hard)
        {
            _reportNeeded = true;
        }
        else if (_observation is not null && !_reportNeeded)
        {
            RestartSettleTimer(commands);
        }

        return new OrchestratorResult(commands, null) { WindowResultsLost = lost };
    }

    /// <summary>Ustalenie: ocenia ostatnią obserwację, o ile jej timer jest bieżący i nic jej nie blokuje (AD-15).</summary>
    private OrchestratorResult OnSettled()
    {
        _settleTimerId = 0;
        if (_settleTransition != _transitionId || _observation is not { } observation)
        {
            return OrchestratorResult.None;
        }

        if (IsHardBlocked())
        {
            _observation = null;
            _reportNeeded = true;
            return OrchestratorResult.None;
        }

        if (_pendingWindow.Count > 0)
        {
            // Trwa tylko ReportForeground: jego wynik albo termin wyników restartuje timer dla zachowanej obserwacji.
            return OrchestratorResult.None;
        }

        _observation = null;
        return Decide(observation);
    }

    /// <summary>Czy ustalona obserwacja wymaga przełączenia z zewnątrz (poza echem i obcym oknem).</summary>
    private bool WouldSwitch(ForegroundObservation observation) => observation.Owner switch
    {
        ForegroundOwner.Shell => _apps.Exists(app => app.State == AppLifecycleState.Foreground),
        ForegroundOwner.App => observation.AppId is { } appId
            && _appsById.TryGetValue(appId, out var app)
            && app.State == AppLifecycleState.Background,
        _ => false,
    };

    /// <summary>
    /// Przełączenie z zewnątrz (AD-5): kroki okienne wykonał już użytkownik, więc bez <c>ShowShell</c>,
    /// <c>ActivateWindow</c>, <c>restore</c> i fokusu UI. Echo, okno obce i aplikacja nieaktywna nie zmieniają nic.
    /// </summary>
    private OrchestratorResult Decide(ForegroundObservation observation)
    {
        if (!WouldSwitch(observation))
        {
            return OrchestratorResult.None;
        }

        var commands = new List<Command>();
        var transitionId = NextTransitionId();
        var previous = _apps.Find(app => app.State == AppLifecycleState.Foreground);
        if (previous is not null)
        {
            previous.State = AppLifecycleState.Background;
            commands.Add(new MuteProcessTree(NextCmdId(), previous.RootPid));
            if (previous.Hwnd is { } hwnd)
            {
                commands.Add(new MinimizeWindow(NextCmdId(), hwnd));
            }
        }

        if (observation.Owner == ForegroundOwner.Shell)
        {
            commands.Add(Route(InputMode.Shell, null, PointerPolicy.Blocked));
        }
        else
        {
            var app = _appsById[observation.AppId!];
            app.ForegroundTransition = transitionId;
            app.State = AppLifecycleState.Foreground;
            commands.Add(new UnmuteProcessTree(NextCmdId(), app.RootPid));
            commands.Add(Route(InputMode.App, app.Type, app.Pointer));
        }

        return new OrchestratorResult(commands, Snapshot())
        {
            ForegroundSwitch = new ForegroundSwitch(observation.Owner, observation.AppId, transitionId),
        };
    }
}
