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
    /// Start pętli: ekran główny z nowym <c>seq</c> fokusu plus <see cref="SetInputRouting"/> z trybem <c>shell</c>
    /// i po jednej sondzie <see cref="ProbeExisting"/> dla każdej pozycji <c>apps.json</c>, w kolejności pliku.
    /// Sondy są fire-and-forget (AD-14): host albo podpina żywe okno przez <see cref="AppWindowAttached"/>,
    /// albo milczy, a spóźniona odpowiedź ląduje w adopcji w <see cref="OnAppWindowAttached"/>. Drugi start też
    /// wysyła routing z nowym <c>epoch</c>, bo to jedyny przypadek powtórzenia trybu.
    /// </summary>
    private OrchestratorResult OnOrchestratorStarted()
    {
        var commands = new List<Command>(_apps.Count + 1)
        {
            Route(InputMode.Shell, null, PointerPolicy.Blocked),
        };
        foreach (var app in _apps)
        {
            commands.Add(new ProbeExisting(NextCmdId(), app.AppId));
        }

        return OnDefaultFocus(commands);
    }

    /// <summary>
    /// Ekran główny z nowym <c>seq</c> fokusu i nowym <c>transitionId</c>: kolejna decyzja o pierwszym planie
    /// zastępuje poprzednią, więc spóźniony <see cref="AppWindowAttached"/> po starcie pętli nie wchodzi na wierzch.
    /// </summary>
    private OrchestratorResult OnDefaultFocus(IReadOnlyList<Command>? commands = null)
    {
        NextTransitionId();
        _focusSeq++;
        _focus = new FocusRequest(FocusRequest.DefaultTarget, _focusSeq);
        return new OrchestratorResult(commands ?? Array.Empty<Command>(), Snapshot());
    }

    /// <summary>
    /// Wybudzenie z Modern Standby: pełna polityka pierwszej decyzji po powrocie. Nowe <c>transitionId</c>
    /// powstaje przed komendami, bo wybudzenie zastępuje każdą wcześniejszą decyzję o pierwszym planie —
    /// okno startujące w trakcie wybudzenia trafi w kompensację z <see cref="OnAppWindowAttached"/>,
    /// a timeout <c>Starting</c> biegnie dalej. Aplikacja z <c>Foreground</c> schodzi do <c>Background</c>
    /// w kolejności Home bez wyciszenia (mute na tych ścieżkach to story 3; AD-5/AD-15): <see cref="MinimizeWindow"/>, <see cref="ShowShell"/>, routing <c>shell</c>.
    /// Na końcu każda aplikacja webowa w <c>Foreground</c> albo <c>Background</c> dostaje fire-and-forget
    /// <see cref="RenewSession"/> w kolejności <c>apps.json</c>: po schowaniu shell ma wrócić na wierzch,
    /// a zminimalizowana przeglądarka nadal potrzebuje świeżej sesji CDP. Gdy nic nie jest w <c>Foreground</c>,
    /// komendy okienkowe i routing nie lecą — <c>Background</c>, <c>Starting</c> i <c>Stopping</c> utrzymują
    /// tryb <c>shell</c>. <see cref="ShowShell"/> wysyłają Home i adopcja, a spóźnione okno <c>Starting</c>
    /// dostaje tylko kompensację <see cref="MinimizeWindow"/>. <c>Starting</c>, <c>Stopping</c>, <c>Failed</c>
    /// i <c>NotRunning</c> nie dostają <see cref="RenewSession"/>.
    /// </summary>
    private OrchestratorResult OnSystemResumed()
    {
        NextTransitionId();
        _focusSeq++;
        _focus = new FocusRequest(FocusRequest.DefaultTarget, _focusSeq);

        var commands = new List<Command>();
        if (_apps.Find(candidate => candidate.State == AppLifecycleState.Foreground) is { } foreground
            && foreground.Hwnd is { } hwnd)
        {
            foreground.State = AppLifecycleState.Background;
            commands.Add(new MinimizeWindow(NextCmdId(), hwnd));
            commands.Add(new ShowShell(NextCmdId()));
            commands.Add(Route(InputMode.Shell, null, PointerPolicy.Blocked));
        }

        foreach (var app in _apps)
        {
            if (app.Type == AppType.Web
                && app.State is AppLifecycleState.Foreground or AppLifecycleState.Background)
            {
                commands.Add(new RenewSession(NextCmdId(), app.AppId));
            }
        }

        return new OrchestratorResult(commands, Snapshot());
    }
}
