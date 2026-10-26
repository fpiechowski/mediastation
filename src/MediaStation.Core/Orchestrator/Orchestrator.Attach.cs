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
    /// Okno bez wcześniejszego <c>StartApp</c> (<c>NotRunning</c>) jest adopcją: zapis <c>hwnd</c>, przejście
    /// w <c>Background</c> i <see cref="MinimizeWindow"/>, a shell zostaje na wierzchu. Okno po starcie daje
    /// <c>Foreground</c>, gdy decyzja o starcie wciąż jest bieżąca: aktywacja okna i routing wejścia według tabeli
    /// AD-8. Gdy w międzyczasie zapadła nowsza decyzja (Home, wybudzenie), okno dostaje kompensację:
    /// <see cref="MinimizeWindow"/> i <c>Background</c> bez <see cref="ActivateWindow"/> i bez routingu, bo shell
    /// ma zostać na wierzchu (AD-14). Timeout <c>Starting</c> jest wtedy zdejmowany, a zapisane <c>hwnd</c> pozwala
    /// wrócić przez <c>LaunchApp</c>. Poza <c>NotRunning</c> i <c>Starting</c> fakt jest ignorowany: spóźniony attach
    /// po <c>Failed</c> zostaje w DW-4.
    /// </summary>
    private OrchestratorResult OnAppWindowAttached(AppWindowAttached attached)
    {
        if (!_appsById.TryGetValue(attached.AppId, out var app))
        {
            return OrchestratorResult.None;
        }

        // Adopcję rozpoznaje sam stan: `StartApp` ustawia `Starting` synchronicznie w tym samym handlerze,
        // więc `NotRunning` znaczy brak startu w locie i brak terminu `Starting` do skorelowania.
        if (app.State == AppLifecycleState.NotRunning)
        {
            return OnAdopt(app, attached.Hwnd, attached.RootPid);
        }

        if (app.State != AppLifecycleState.Starting)
        {
            return OrchestratorResult.None;
        }

        app.Hwnd = attached.Hwnd;
        app.RootPid = attached.RootPid;
        if (app.ForegroundTransition != _transitionId)
        {
            app.State = AppLifecycleState.Background;
            var compensating = new List<Command> { new MinimizeWindow(NextCmdId(), attached.Hwnd) };
            if (CancelStartTimeout(app.AppId) is { } staleTimer)
            {
                compensating.Add(staleTimer);
            }

            return new OrchestratorResult(compensating, Snapshot());
        }

        app.State = AppLifecycleState.Foreground;
        var activate = new ActivateWindow(NextCmdId(), attached.Hwnd);
        var unmute = new UnmuteProcessTree(NextCmdId(), attached.RootPid);
        var routing = Route(InputMode.App, app.Type, app.Pointer);
        var commands = new List<Command> { activate, unmute, routing };

        // Okno zdążyło przed deadlinem: timeout `Starting` traci znaczenie, a scheduler dostaje jawne anulowanie.
        if (CancelStartTimeout(app.AppId) is { } cancel)
        {
            commands.Add(cancel);
        }

        return new OrchestratorResult(commands, Snapshot());
    }

    /// <summary>
    /// Adopcja żywej aplikacji po restarcie (AD-10): <c>hwnd</c> trafia do stanu, aplikacja ląduje w <c>Background</c>,
    /// a shell zostaje na ekranie głównym. Adopcja nie jest decyzją o pierwszym planie: nie podbija
    /// <c>transitionId</c> ani <c>_focusSeq</c>, nie wysyła routingu wejścia i nie rusza timerów. Kolejność jest ta
    /// sama co w Home, bez <c>MuteProcessTree</c> (story 3; AD-5/AD-15): najpierw <see cref="MinimizeWindow"/>, potem <see cref="ShowShell"/> — i tylko
    /// wtedy, gdy inna aplikacja nie jest aktywna, bo sondy są fire-and-forget i odpowiedź hosta może dotrzeć po
    /// wyborze kafelka.
    /// </summary>
    private OrchestratorResult OnAdopt(AppState app, long hwnd, int rootPid)
    {
        app.Hwnd = hwnd;
        app.RootPid = rootPid;
        app.State = AppLifecycleState.Background;
        var commands = new List<Command> { new MinimizeWindow(NextCmdId(), hwnd) };
        if (!AnotherAppIsActive(app))
        {
            commands.Add(new ShowShell(NextCmdId()));
        }

        return new OrchestratorResult(commands, Snapshot());
    }
}
