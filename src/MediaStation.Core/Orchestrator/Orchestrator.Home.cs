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
    /// Action z InputHub: Home jest obsługiwany jak dotąd (<see cref="OnHome"/>) i zawsze trafia też do Shell,
    /// także gdy nic nie zmienił. Pozostałe Actions Shell dostaje tylko w trybie <c>shell</c> (AD-8): strzałki
    /// (<c>press</c>/<c>repeat</c>), Ok (<c>press</c>/<c>long</c>) i Back (<c>press</c>). W trybie <c>app</c>/<c>osk</c>
    /// wejście należy do aplikacji, więc <see cref="OrchestratorResult.Actions"/> zostaje puste.
    /// </summary>
    private OrchestratorResult OnAction(ActionMessage action)
    {
        var result = OnHome(action);
        var forward = action.Kind == ActionKind.Home || (_mode == InputMode.Shell && IsShellAction(action));
        return forward ? result with { Actions = [action] } : result;
    }

    private static bool IsShellAction(ActionMessage action) => action.Kind switch
    {
        ActionKind.Up or ActionKind.Down or ActionKind.Left or ActionKind.Right =>
            action.Phase is ActionPhase.Press or ActionPhase.Repeat,
        ActionKind.Ok => action.Phase is ActionPhase.Press or ActionPhase.Long,
        ActionKind.Back => action.Phase == ActionPhase.Press,
        _ => false,
    };

    /// <summary>
    /// Home z pierwszego planu: aplikacja schodzi do <c>Background</c>, shell wraca na wierzch, a InputHub
    /// dostaje tryb <c>shell</c>. Kolejność komend jest istotna (AD-5/AD-15): najpierw <see cref="MuteProcessTree"/>, potem minimalizacja,
    /// potem <see cref="ShowShell"/>, na końcu routing. Fokusu nie zmieniamy — UI sam wraca na ostatni kafelek.
    /// Home w trakcie <c>Starting</c> pokazuje od razu tylko shell: aplikacja zostaje <c>Starting</c> bez fokusu,
    /// bez routingu i bez anulowania timeoutu, a okno, które pojawi się później, dostanie kompensację w
    /// <see cref="OnAppWindowAttached"/>. Home jest decyzją o pierwszym planie także wtedy, gdy nie ma czego chować
    /// (np. aplikacja w <c>Stopping</c>), więc zawsze podbija <c>transitionId</c>: restart podjęty wcześniej
    /// nie wróci na wierzch po potwierdzonym stopie.
    /// </summary>
    private OrchestratorResult OnHome(ActionMessage action)
    {
        if (action.Kind != ActionKind.Home)
        {
            return OrchestratorResult.None;
        }

        NextTransitionId();

        var app = _apps.Find(candidate => candidate.State == AppLifecycleState.Foreground);
        if (app is not null)
        {
            if (app.Hwnd is not { } hwnd)
            {
                return OrchestratorResult.None;
            }

            app.State = AppLifecycleState.Background;
            var mute = new MuteProcessTree(NextCmdId(), app.RootPid);
            var minimize = new MinimizeWindow(NextCmdId(), hwnd);
            var showShell = new ShowShell(NextCmdId());
            var routing = Route(InputMode.Shell, null, PointerPolicy.Blocked);
            return new OrchestratorResult([mute, minimize, showShell, routing], Snapshot());
        }

        if (!_apps.Exists(candidate => candidate.State == AppLifecycleState.Starting))
        {
            return OrchestratorResult.None;
        }

        return new OrchestratorResult([new ShowShell(NextCmdId())], Snapshot());
    }
}
