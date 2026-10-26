using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

public class OrchestratorHomeTests
{
    // ── Home: Foreground → Background ─────────────────────────────────────────

    [Fact]
    public void Home_from_foreground_minimizes_shows_shell_routes_shell_input_and_keeps_focus()
    {
        var (orchestrator, _) = Started();
        var foreground = Foreground(orchestrator, "netflix");
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 1), foreground.FocusRequest);

        var result = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new MinimizeWindow(10, 0x1A2B), command),
            command => Assert.Equal(new ShowShell(11), command),
            command => Assert.Equal(
                new SetInputRouting(12, InputMode.Shell, null, PointerPolicy.Blocked, 3),
                command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(foreground.Rev + 1, snapshot.Rev);
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 1), snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    [Fact]
    public void Home_when_shell_is_on_top_is_ignored()
    {
        var (orchestrator, started) = Started();

        var result = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(started.Rev + 1, next.Rev);
    }

    /// <summary>
    /// Home w trakcie <c>Starting</c> chowa tylko shell: aplikacja zostaje <c>Starting</c> z tym samym fokusem,
    /// bez routingu i bez anulowania timeoutu, a powtórne <c>LaunchApp</c> jest ignorowane.
    /// </summary>
    [Fact]
    public void Home_while_app_is_starting_shows_shell_without_focus_routing_or_timer_cancel()
    {
        var (orchestrator, _) = Started();
        var starting = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("netflix")).Snapshot);

        var result = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));

        Assert.Equal(new ShowShell(6), Assert.Single(result.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(starting.Rev + 1, snapshot.Rev);
        Assert.Equal(starting.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(1, snapshot.FocusRequest!.Seq);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));

        var repeated = orchestrator.Handle(new LaunchApp("netflix"));
        Assert.Empty(repeated.Commands);
        Assert.Null(repeated.Snapshot);
    }

    /// <summary>
    /// Okno, które dokończy start po Home, przegrywa z nowszą decyzją: dostaje kompensujące
    /// <see cref="MinimizeWindow"/> i <c>Background</c> z zapisanym <c>hwnd</c>, bez aktywacji i routingu.
    /// </summary>
    [Fact]
    public void Late_attached_window_after_home_is_minimized_and_moves_to_background()
    {
        var (orchestrator, _) = Started();
        orchestrator.Handle(new LaunchApp("netflix"));
        orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new MinimizeWindow(7, 0x1A2B), command),
            command => Assert.Equal(new CancelTimer(8, 1), command),
            command => AssertManaged(command, 9, 1, (0x1A2B, 4321)));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 1), snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    /// <summary>
    /// Home nie anuluje timeoutu <c>Starting</c>: bez okna aplikacja kończy start w <c>Failed</c>,
    /// bez komend i bez nowego fokusu.
    /// </summary>
    [Fact]
    public void Start_timeout_after_home_enters_failed_without_commands_or_new_focus()
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        orchestrator.Handle(new LaunchApp("netflix"));
        var home = Assert.IsType<ShellSnapshot>(
            orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2)).Snapshot);
        time.Advance(TimeSpan.FromSeconds(30));

        var result = orchestrator.Handle(new TimerElapsed(1));

        Assert.Empty(result.Commands);
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(home.Rev + 1, snapshot.Rev);
        Assert.Equal(home.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(1, snapshot.FocusRequest!.Seq);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
    }

    /// <summary>
    /// Attach po timeoucie startu nie wskrzesza kafelka: <c>Failed</c> nie ma już czego aktywować,
    /// a timeout zdjął korelację startu.
    /// </summary>
    [Fact]
    public void Attached_window_after_start_timeout_is_ignored()
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        orchestrator.Handle(new LaunchApp("netflix"));
        time.Advance(TimeSpan.FromSeconds(30));
        orchestrator.Handle(new TimerElapsed(1));

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
    }

    /// <summary>
    /// Powrót po kompensacji: zapisane <c>hwnd</c> wystarcza, żeby wrócić na pierwszy plan z routingiem aplikacji.
    /// </summary>
    [Fact]
    public void Launch_after_home_compensation_activates_window_and_routes_app_input()
    {
        var (orchestrator, _) = Started();
        orchestrator.Handle(new LaunchApp("netflix"));
        orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));
        var background = Assert.IsType<ShellSnapshot>(
            orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321)).Snapshot);

        var result = orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ActivateWindow(10, 0x1A2B), command),
            command => Assert.Equal(
                new SetInputRouting(11, InputMode.App, AppType.Web, PointerPolicy.Blocked, 2),
                command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(background.Rev + 1, snapshot.Rev);
        Assert.Equal(background.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Home_when_app_is_already_background_is_ignored()
    {
        var (orchestrator, _) = Started();
        var background = Background(orchestrator, "netflix");

        var result = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 3));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(background.Rev + 1, next.Rev);
        Assert.Equal(AppLifecycleState.Background, StateOf(next, "netflix"));
    }

    [Theory]
    [InlineData(ActionKind.Ok)]
    [InlineData(ActionKind.Back)]
    public void Action_other_than_home_is_ignored(ActionKind kind)
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");

        var result = orchestrator.Handle(new Action(kind, ActionPhase.Press, ActionSource.Remote, 2));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
    }
}
