using System.Collections.Generic;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Orchestrator;

public sealed partial class Orchestrator
{
    /// <summary>
    /// Zmiana okna na pierwszym planie (AD-8, AD-15) to fakt: Orchestrator rozpoznaje właściciela okna do logu i nic
    /// więcej. <c>mode</c>, stan aplikacji i dźwięk zostają bez zmian, bo powrót na shell daje Home, a nie
    /// samo przełączenie okna. Wynik nie ma komend ani snapshotu.
    /// </summary>
    private OrchestratorResult OnForegroundChanged(ForegroundChanged changed)
    {
        var apps = new List<ForegroundApp>(_apps.Count);
        foreach (var app in _apps)
        {
            apps.Add(new ForegroundApp(app.AppId, app.Hwnd ?? 0, app.RootPid));
        }

        var observation = ForegroundOwnerResolver.Resolve(changed.Hwnd, changed.Pid, _foreground?.ShellHwnd ?? 0, apps);
        return OrchestratorResult.None with { Foreground = observation };
    }
}
