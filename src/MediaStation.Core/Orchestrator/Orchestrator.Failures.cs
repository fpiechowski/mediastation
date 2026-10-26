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
    /// Porażka stopu nie jest dowodem, że proces zniknął: stan zostaje <c>Stopping</c>, a korelacja czeka
    /// na <see cref="ProcessExited"/> albo na timeout <c>Stopping</c>. Nieudany <c>StartApp</c> przenosi aplikację
    /// w <c>Failed</c> także wtedy, gdy okno zdążyło już dać <c>Foreground</c>/<c>Background</c>; porażka komendy
    /// spoza korelacji startu nie zmienia stanu.
    /// </summary>
    private OrchestratorResult OnCommandFailed(CommandFailed failed)
    {
        if (_pendingStarts.Remove(failed.CmdId, out var appId)
            && _appsById.TryGetValue(appId, out var app)
            && app.State is AppLifecycleState.Starting or AppLifecycleState.Foreground or AppLifecycleState.Background)
        {
            return EnterFailed(app, wasForeground: app.State == AppLifecycleState.Foreground);
        }

        return OrchestratorResult.None;
    }

    /// <summary>
    /// Zakończony proces domyka <c>Stopping</c> tak samo jak potwierdzony <see cref="StopApp"/>.
    /// W żywym cyklu (<c>Starting</c>, <c>Foreground</c>, <c>Background</c>) fakt jest awarią i prowadzi w <c>Failed</c>;
    /// <c>NotRunning</c>, <c>Failed</c> i <c>Stopping</c> bez korelacji stopu ignorują go, bo spóźniony fakt nie może
    /// wskrzesić kafelka.
    /// </summary>
    private OrchestratorResult OnProcessExited(ProcessExited exited)
    {
        if (!_appsById.TryGetValue(exited.AppId, out var app))
        {
            return OrchestratorResult.None;
        }

        if (app.State == AppLifecycleState.Stopping)
        {
            if (FindPendingStop(app.AppId) is not { } cmdId)
            {
                return OrchestratorResult.None;
            }

            var pending = _pendingStops[cmdId];
            _pendingStops.Remove(cmdId);
            return OnStopped(pending);
        }

        return app.State is AppLifecycleState.Starting or AppLifecycleState.Foreground or AppLifecycleState.Background
            ? EnterFailed(app, wasForeground: app.State == AppLifecycleState.Foreground)
            : OrchestratorResult.None;
    }

    /// <summary>
    /// Zerwane CDP to awaria tylko w żywym cyklu: <c>Starting</c>, <c>Foreground</c> i <c>Background</c> prowadzą
    /// w <c>Failed</c> (AD-10). <c>Stopping</c> należy do korelacji stopu, a <c>NotRunning</c> i <c>Failed</c>
    /// nie mają żywego procesu, którego awarię mógłby raportować.
    /// </summary>
    private OrchestratorResult OnCdpLost(CdpLost lost)
    {
        if (!_appsById.TryGetValue(lost.AppId, out var app))
        {
            return OrchestratorResult.None;
        }

        return app.State is AppLifecycleState.Starting or AppLifecycleState.Foreground or AppLifecycleState.Background
            ? EnterFailed(app, wasForeground: app.State == AppLifecycleState.Foreground)
            : OrchestratorResult.None;
    }

    /// <summary>
    /// Wspólne wejście w <c>Failed</c>: okno po awarii nie jest już pod kontrolą Orchestratora, więc <c>Hwnd</c> znika,
    /// a korelacje startu i stopu przepadają — spóźniony wynik startu albo stopu nie może już zmienić stanu.
    /// Timeout <c>Starting</c> jest anulowany ostatnią komendą batcha, bo zegar nie ma już czego pilnować.
    /// Awaria na pierwszym planie pokazuje shell i oddaje mu wejście (AD-5/AD-15), a fokus z nowym <c>seq</c>
    /// wskazuje kafelek, który padł (AD-16); to także decyzja o pierwszym planie, więc dostaje nowe
    /// <c>transitionId</c>. Kolejność komend jest istotna: najpierw <see cref="ShowShell"/>, potem routing,
    /// na końcu timer.
    /// </summary>
    private OrchestratorResult EnterFailed(AppState app, bool wasForeground)
    {
        app.Hwnd = null;
        app.State = AppLifecycleState.Failed;
        DropPendingStarts(app.AppId);
        DropPendingStops(app.AppId);

        var commands = new List<Command>();
        if (wasForeground)
        {
            NextTransitionId();
            commands.Add(new ShowShell(NextCmdId()));
            commands.Add(new SetInputRouting(NextCmdId(), InputMode.Shell, null, PointerPolicy.Blocked, NextEpoch()));
            _focus = new FocusRequest(app.AppId, ++_focusSeq);
        }

        if (CancelStartTimeout(app.AppId) is { } cancel)
        {
            commands.Add(cancel);
        }

        return new OrchestratorResult(commands, Snapshot());
    }

    /// <summary>Korelacje startu aplikacji tracą znaczenie po wejściu w <c>Failed</c>.</summary>
    private void DropPendingStarts(string appId)
    {
        var stale = new List<long>();
        foreach (var (cmdId, pendingAppId) in _pendingStarts)
        {
            if (pendingAppId == appId)
            {
                stale.Add(cmdId);
            }
        }

        foreach (var cmdId in stale)
        {
            _pendingStarts.Remove(cmdId);
        }
    }

    /// <summary>
    /// Korelacje stopu aplikacji tracą znaczenie po wejściu w <c>Failed</c>: spóźniony wynik <see cref="StopApp"/>
    /// nie może zamknąć kafelka w <c>NotRunning</c> ani wystartować go od nowa.
    /// </summary>
    private void DropPendingStops(string appId)
    {
        var stale = new List<long>();
        foreach (var (cmdId, pending) in _pendingStops)
        {
            if (pending.AppId == appId)
            {
                stale.Add(cmdId);
            }
        }

        foreach (var cmdId in stale)
        {
            _pendingStops.Remove(cmdId);
        }
    }
}
