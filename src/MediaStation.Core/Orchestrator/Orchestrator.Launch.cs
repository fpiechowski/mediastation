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
    /// Wybór kafelka uruchamia aplikację w <c>NotRunning</c> (<c>cmdId</c> zostaje w korelacji startu) i planuje
    /// timeout <c>Starting</c> na <c>startingTimeout</c>, albo przywraca ją z <c>Background</c>. Start jest decyzją
    /// o pierwszym planie, więc przed <c>Starting</c> aplikacja dostaje bieżący <c>transitionId</c>; jeśli w międzyczasie
    /// zapadnie nowsza decyzja (Home, wybudzenie), spóźnione okno nie wejdzie na wierzch. Guard „inna aplikacja
    /// w <c>Foreground</c>/<c>Starting</c>” stoi tu, we wspólnym wejściu obu gałęzi, więc launch jest wtedy odrzucany;
    /// przełączanie między dwiema żywymi aplikacjami dokłada osobna gałąź.
    /// </summary>
    private OrchestratorResult OnLaunchApp(LaunchApp launch)
    {
        if (!_appsById.TryGetValue(launch.AppId, out var app))
        {
            return Ignored(launch, "nieznany appId");
        }

        if (AnotherAppIsActive(app))
        {
            return Ignored(launch, "inna aplikacja jest aktywna");
        }

        if (app.State == AppLifecycleState.Background)
        {
            return app.Hwnd is { } hwnd ? OnResume(app, hwnd) : Ignored(launch, "brak okna aplikacji w tle");
        }

        if (app.State != AppLifecycleState.NotRunning)
        {
            return Ignored(launch, $"stan {app.State} nie pozwala na uruchomienie");
        }

        var cmdId = NextCmdId();
        app.ForegroundTransition = NextTransitionId();
        app.State = AppLifecycleState.Starting;
        _pendingStarts.Add(cmdId, app.AppId);
        return new OrchestratorResult([new StartApp(cmdId, app.AppId), ScheduleStartTimeout(app.AppId)], Snapshot());
    }

    /// <summary>Odrzucenie <see cref="LaunchApp"/>: bez komend i snapshotu, z <see cref="IgnoredIntent"/> do logu pętli.</summary>
    private static OrchestratorResult Ignored(LaunchApp launch, string reason) =>
        Ignored(nameof(LaunchApp), launch.AppId, reason);

    /// <summary>Odrzucenie Intentu o danej nazwie: bez komend i snapshotu, z <see cref="IgnoredIntent"/> do logu pętli.</summary>
    private static OrchestratorResult Ignored(string intent, string appId, string reason) =>
        OrchestratorResult.None with { IgnoredIntent = new IgnoredIntent(intent, appId, reason) };

    /// <summary>
    /// Powrót z tła: proces żyje, więc wystarczą <see cref="ActivateWindow"/> i routing wejścia aplikacji.
    /// Guard niezmiennika jednej aplikacji na pierwszym planie stoi w <see cref="OnLaunchApp"/>. Powrót jest
    /// nową decyzją o pierwszym planie, więc podbija <c>transitionId</c> i zapisuje je w aplikacji.
    /// </summary>
    private OrchestratorResult OnResume(AppState app, long hwnd)
    {
        app.ForegroundTransition = NextTransitionId();
        app.State = AppLifecycleState.Foreground;
        var activate = new ActivateWindow(NextCmdId(), hwnd);
        var routing = Route(InputMode.App, app.Type, app.Pointer);
        return new OrchestratorResult([activate, routing], Snapshot());
    }
}
