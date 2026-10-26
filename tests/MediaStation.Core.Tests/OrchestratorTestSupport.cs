using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;

namespace MediaStation.Core.Tests;

/// <summary>Wspólne konfiguracje i helpery testów Orchestratora, współdzielone przez <c>Orchestrator*Tests</c>.</summary>
internal static class OrchestratorTestSupport
{
    /// <summary>Punkt startowy zegara testowego; <c>startingTimeout</c> z domyślnych <see cref="Settings"/> to 30 s, a <c>stoppingTimeout</c> — 45 s.</summary>
    internal static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    internal static LoadedConfig TwoApps() => new(
        new AppsConfig([
            new WebApp("netflix", AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222),
            new NativeApp("plex", AppCategory.Game, "Plex", "plex.png", "C:\\Plex\\Plex.exe"),
        ]),
        new InputConfig([], []),
        new Settings());

    internal static CoreOrchestrator Create(TimeProvider? time = null) => new(TwoApps(), time ?? new FakeTimeProvider(T0));

    /// <summary>Config z aplikacją webową i Moonlightem: dwie różne ścieżki hostów dla tej samej adopcji.</summary>
    internal static LoadedConfig WebAndMoonlight() => new(
        new AppsConfig([
            new WebApp("netflix", AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222),
            new MoonlightApp("moonlight-pc", AppCategory.Game, "Moonlight", "moonlight.png", "192.168.1.10", "Desktop"),
        ]),
        new InputConfig([], []),
        new Settings());

    /// <summary>Config z dwiema aplikacjami webowymi i Moonlightem: kolejność <c>apps.json</c> i filtr typu w jednym miejscu.</summary>
    internal static LoadedConfig TwoWebAndMoonlight() => new(
        new AppsConfig([
            new WebApp("netflix", AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222),
            new WebApp("plex-web", AppCategory.Video, "Plex", "plex-web.png", "plex-web", BrowserKind.Edge, 9223),
            new MoonlightApp("moonlight-pc", AppCategory.Game, "Moonlight", "moonlight.png", "192.168.1.10", "Desktop"),
        ]),
        new InputConfig([], []),
        new Settings());

    internal static (CoreOrchestrator Orchestrator, ShellSnapshot Started) StartedWith(LoadedConfig config)
    {
        var orchestrator = new CoreOrchestrator(config, new FakeTimeProvider(T0));
        var started = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        return (orchestrator, started);
    }

    internal static (CoreOrchestrator Orchestrator, ShellSnapshot Started) Started(TimeProvider? time = null)
    {
        var orchestrator = Create(time);
        var started = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        return (orchestrator, started);
    }

    /// <summary>
    /// <see cref="SetManagedWindows"/> z zadanym <c>cmdId</c>, własnym <c>epoch</c> i dokładnie podanymi oknami aplikacji
    /// (rekordy porównują tablice po referencji, więc pola sprawdzamy jawnie).
    /// </summary>
    internal static void AssertManaged(
        Command command,
        long cmdId,
        long epoch,
        params (long Hwnd, int RootPid)[] apps)
    {
        var managed = Assert.IsType<SetManagedWindows>(command);
        Assert.Equal(cmdId, managed.CmdId);
        Assert.Equal(epoch, managed.Epoch);
        Assert.Null(managed.OskHwnd);
        Assert.Equal(apps.Select(app => new ManagedWindow(app.Hwnd, app.RootPid)), managed.Apps);
    }

    internal static AppLifecycleState StateOf(ShellSnapshot snapshot, string appId) =>
        Assert.Single(snapshot.Apps, app => app.AppId == appId).State;

    /// <summary>Aplikacja w <c>Foreground</c> z zapamiętanym <c>hwnd</c>: <c>LaunchApp</c> i <c>AppWindowAttached</c>.</summary>
    internal static ShellSnapshot Foreground(CoreOrchestrator orchestrator, string appId, long hwnd = 0x1A2B)
    {
        orchestrator.Handle(new LaunchApp(appId));
        return Assert.IsType<ShellSnapshot>(orchestrator.Handle(new AppWindowAttached(appId, hwnd, 4321)).Snapshot);
    }

    /// <summary>Aplikacja w <c>Background</c>: <see cref="Foreground"/> i Home.</summary>
    internal static ShellSnapshot Background(CoreOrchestrator orchestrator, string appId, long hwnd = 0x1A2B)
    {
        Foreground(orchestrator, appId, hwnd);
        return Assert.IsType<ShellSnapshot>(orchestrator
            .Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1)).Snapshot);
    }
}
