using System.Reflection;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;

namespace MediaStation.Core.Tests;

public class OrchestratorTests
{
    /// <summary>Punkt startowy zegara testowego; <c>startingTimeout</c> z domyślnych <see cref="Settings"/> to 30 s, a <c>stoppingTimeout</c> — 45 s.</summary>
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static LoadedConfig TwoApps() => new(
        new AppsConfig([
            new WebApp("netflix", AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222),
            new NativeApp("plex", AppCategory.Game, "Plex", "plex.png", "C:\\Plex\\Plex.exe"),
        ]),
        new InputConfig([], []),
        new Settings());

    private static CoreOrchestrator Create(TimeProvider? time = null) => new(TwoApps(), time ?? new FakeTimeProvider(T0));

    private static (CoreOrchestrator Orchestrator, ShellSnapshot Started) Started(TimeProvider? time = null)
    {
        var orchestrator = Create(time);
        var started = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        return (orchestrator, started);
    }

    private static AppLifecycleState StateOf(ShellSnapshot snapshot, string appId) =>
        Assert.Single(snapshot.Apps, app => app.AppId == appId).State;

    /// <summary>Aplikacja w <c>Foreground</c> z zapamiętanym <c>hwnd</c>: <c>LaunchApp</c> i <c>AppWindowAttached</c>.</summary>
    private static ShellSnapshot Foreground(CoreOrchestrator orchestrator, string appId, long hwnd = 0x1A2B)
    {
        orchestrator.Handle(new LaunchApp(appId));
        return Assert.IsType<ShellSnapshot>(orchestrator.Handle(new AppWindowAttached(appId, hwnd, 4321)).Snapshot);
    }

    /// <summary>Aplikacja w <c>Background</c>: <see cref="Foreground"/> i Home.</summary>
    private static ShellSnapshot Background(CoreOrchestrator orchestrator, string appId, long hwnd = 0x1A2B)
    {
        Foreground(orchestrator, appId, hwnd);
        return Assert.IsType<ShellSnapshot>(orchestrator
            .Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1)).Snapshot);
    }

    [Fact]
    public void First_started_yields_placeholder_with_not_running_apps_and_default_focus()
    {
        var orchestrator = Create();

        var result = orchestrator.Handle(new OrchestratorStarted());

        Assert.Equal(
            new SetInputRouting(1, InputMode.Shell, null, PointerPolicy.Blocked, 1),
            Assert.Single(result.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(1, snapshot.Rev);
        Assert.Equal(ShellScreen.Placeholder, snapshot.Screen);
        Assert.Empty(snapshot.ConfigErrors);
        Assert.Collection(
            snapshot.Apps,
            app =>
            {
                Assert.Equal("netflix", app.AppId);
                Assert.Equal(AppCategory.Video, app.Category);
                Assert.Equal(AppLifecycleState.NotRunning, app.State);
            },
            app =>
            {
                Assert.Equal("plex", app.AppId);
                Assert.Equal(AppCategory.Game, app.Category);
                Assert.Equal(AppLifecycleState.NotRunning, app.State);
            });
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 1), snapshot.FocusRequest);
    }

    [Fact]
    public void Next_started_advances_rev_focus_seq_and_routing_epoch()
    {
        var orchestrator = Create();

        var firstResult = orchestrator.Handle(new OrchestratorStarted());
        var first = Assert.IsType<ShellSnapshot>(firstResult.Snapshot);
        var secondResult = orchestrator.Handle(new OrchestratorStarted());
        var second = Assert.IsType<ShellSnapshot>(secondResult.Snapshot);

        Assert.Equal(1, first.Rev);
        Assert.Equal(1, first.FocusRequest!.Seq);
        Assert.Equal(2, second.Rev);
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 2), second.FocusRequest);
        Assert.Equal(
            new SetInputRouting(1, InputMode.Shell, null, PointerPolicy.Blocked, 1),
            Assert.Single(firstResult.Commands));
        Assert.Equal(
            new SetInputRouting(2, InputMode.Shell, null, PointerPolicy.Blocked, 2),
            Assert.Single(secondResult.Commands));
    }

    [Fact]
    public void System_resumed_yields_default_focus_with_new_rev_and_seq()
    {
        var orchestrator = Create();
        var started = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);

        var result = orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));

        Assert.Empty(result.Commands);
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(2, snapshot.Rev);
        Assert.Equal(ShellScreen.Placeholder, snapshot.Screen);
        Assert.Equal(started.Apps, snapshot.Apps);
        Assert.Empty(snapshot.ConfigErrors);
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 2), snapshot.FocusRequest);
    }

    [Fact]
    public void Launch_app_in_not_running_moves_to_starting_and_schedules_start_timeout()
    {
        var (orchestrator, started) = Started();

        var result = orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new StartApp(2, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(3, 1, "netflix", T0.AddSeconds(30)), command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(started.Rev + 1, snapshot.Rev);
        Assert.Equal(started.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    /// <summary>
    /// <c>LaunchApp</c> uruchamia wyłącznie aplikację w <c>NotRunning</c>; <c>Foreground</c> i <c>Starting</c>
    /// są ignorowane, a <c>Background</c> ma osobną gałąź powrotu z tła.
    /// </summary>
    [Theory]
    [InlineData("starting", 3L)]
    [InlineData("foreground", 4L)]
    public void Launch_app_outside_not_running_is_ignored(string state, long expectedRevAfterIgnored)
    {
        var (orchestrator, _) = Started();
        Assert.NotNull(orchestrator.Handle(new LaunchApp("netflix")).Snapshot);
        if (state == "foreground")
        {
            Assert.NotNull(orchestrator.Handle(new AppWindowAttached("netflix", 10, 20)).Snapshot);
        }

        var ignored = orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Empty(ignored.Commands);
        Assert.Null(ignored.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(expectedRevAfterIgnored, next.Rev);
    }

    [Fact]
    public void Launch_app_with_unknown_app_id_is_ignored()
    {
        var (orchestrator, started) = Started();

        var result = orchestrator.Handle(new LaunchApp("nope"));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(started.Rev + 1, next.Rev);
    }

    [Fact]
    public void Command_completed_for_start_keeps_starting_and_yields_nothing()
    {
        var (orchestrator, _) = Started();
        orchestrator.Handle(new LaunchApp("netflix"));

        var result = orchestrator.Handle(new CommandCompleted(2));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));
    }

    [Theory]
    [InlineData(ErrorCode.Unexpected)]
    [InlineData(ErrorCode.CdpUnavailable)]
    public void Command_failed_for_start_moves_to_failed_cancels_timeout_without_focus_change(ErrorCode errorCode)
    {
        var (orchestrator, _) = Started();
        var starting = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("netflix")).Snapshot);

        var result = orchestrator.Handle(new CommandFailed(2, errorCode));

        Assert.Equal(new CancelTimer(4, 1), Assert.Single(result.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(starting.Rev + 1, snapshot.Rev);
        Assert.Equal(starting.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    public void Events_without_handling_yield_no_snapshot_and_no_commands(string kind)
    {
        var orchestrator = Create();
        Event message = kind == "completed" ? new CommandCompleted(7) : new CommandFailed(7, ErrorCode.Unexpected);

        var result = orchestrator.Handle(message);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Attached_window_in_starting_moves_to_foreground_routes_web_input_and_cancels_timeout()
    {
        var (orchestrator, started) = Started();
        orchestrator.Handle(new LaunchApp("netflix"));

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ActivateWindow(4, 0x1A2B), command),
            command => Assert.Equal(
                new SetInputRouting(5, InputMode.App, AppType.Web, PointerPolicy.Blocked, 2),
                command),
            command => Assert.Equal(new CancelTimer(6, 1), command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(started.Rev + 2, snapshot.Rev);
        Assert.Equal(started.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Attached_window_routes_native_pointer_from_config()
    {
        var (orchestrator, _) = Started();
        orchestrator.Handle(new LaunchApp("plex"));

        var result = orchestrator.Handle(new AppWindowAttached("plex", 42, 24));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ActivateWindow(4, 42), command),
            command => Assert.Equal(
                new SetInputRouting(5, InputMode.App, AppType.Native, PointerPolicy.Allowed, 2),
                command),
            command => Assert.Equal(new CancelTimer(6, 1), command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "plex"));
    }

    [Fact]
    public void Attached_window_without_start_is_ignored_and_keeps_not_running()
    {
        var (orchestrator, _) = Started();

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 1, 2));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var launched = orchestrator.Handle(new LaunchApp("netflix"));
        Assert.Equal(new StartApp(2, "netflix"), launched.Commands[0]);
    }

    [Fact]
    public void Attached_window_when_already_foreground_is_ignored()
    {
        var (orchestrator, _) = Started();
        orchestrator.Handle(new LaunchApp("netflix"));
        orchestrator.Handle(new AppWindowAttached("netflix", 10, 20));

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 10, 20));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Ignored_events_do_not_advance_rev()
    {
        var orchestrator = Create();
        orchestrator.Handle(new CommandCompleted(7));

        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);

        Assert.Equal(1, snapshot.Rev);
        Assert.Equal(1, snapshot.FocusRequest!.Seq);
    }

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
            command => Assert.Equal(new MinimizeWindow(7, 0x1A2B), command),
            command => Assert.Equal(new ShowShell(8), command),
            command => Assert.Equal(
                new SetInputRouting(9, InputMode.Shell, null, PointerPolicy.Blocked, 3),
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

        Assert.Equal(new ShowShell(4), Assert.Single(result.Commands));
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
            command => Assert.Equal(new MinimizeWindow(5, 0x1A2B), command),
            command => Assert.Equal(new CancelTimer(6, 1), command));
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
            command => Assert.Equal(new ActivateWindow(7, 0x1A2B), command),
            command => Assert.Equal(
                new SetInputRouting(8, InputMode.App, AppType.Web, PointerPolicy.Blocked, 2),
                command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(background.Rev + 1, snapshot.Rev);
        Assert.Equal(background.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "netflix"));
    }

    /// <summary>
    /// Home z pierwszego planu też jest nowszą decyzją: równolegle startująca aplikacja, której okno pojawi się
    /// po Home, dostaje kompensację zamiast <c>ActivateWindow</c>.
    /// </summary>
    [Fact]
    public void Home_from_foreground_makes_parallel_starting_app_lose_its_window()
    {
        var (orchestrator, _) = Started();
        var foreground = Foreground(orchestrator, "plex");
        orchestrator.Handle(new LaunchApp("netflix"));

        var result = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new MinimizeWindow(9, 0x1A2B), command),
            command => Assert.Equal(new ShowShell(10), command),
            command => Assert.Equal(
                new SetInputRouting(11, InputMode.Shell, null, PointerPolicy.Blocked, 3),
                command));
        var home = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(foreground.Rev + 2, home.Rev);
        Assert.Equal(foreground.FocusRequest, home.FocusRequest);
        Assert.Equal(AppLifecycleState.Background, StateOf(home, "plex"));
        Assert.Equal(AppLifecycleState.Starting, StateOf(home, "netflix"));

        var late = orchestrator.Handle(new AppWindowAttached("netflix", 0x77, 7));

        Assert.Collection(
            late.Commands,
            command => Assert.Equal(new MinimizeWindow(12, 0x77), command),
            command => Assert.Equal(new CancelTimer(13, 2), command));
        Assert.Equal(AppLifecycleState.Background, StateOf(Assert.IsType<ShellSnapshot>(late.Snapshot), "netflix"));
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
    [InlineData(ActionKind.AppMenu)]
    public void Action_other_than_home_is_ignored(ActionKind kind)
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");

        var result = orchestrator.Handle(new Action(kind, ActionPhase.Press, ActionSource.Remote, 2));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
    }

    // ── Powrót z tła: Background → Foreground ─────────────────────────────────

    [Fact]
    public void Launch_app_in_background_activates_window_and_routes_app_input()
    {
        var (orchestrator, _) = Started();
        var background = Background(orchestrator, "netflix");

        var result = orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ActivateWindow(10, 0x1A2B), command),
            command => Assert.Equal(
                new SetInputRouting(11, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(background.Rev + 1, snapshot.Rev);
        Assert.Equal(background.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Launch_app_in_background_while_another_app_is_foreground_is_ignored()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        Foreground(orchestrator, "plex");

        var result = orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Background, StateOf(next, "netflix"));
        Assert.Equal(AppLifecycleState.Foreground, StateOf(next, "plex"));
    }

    [Fact]
    public void Launch_app_in_background_while_another_app_is_starting_is_ignored()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        orchestrator.Handle(new LaunchApp("plex"));

        var result = orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Background, StateOf(next, "netflix"));
        Assert.Equal(AppLifecycleState.Starting, StateOf(next, "plex"));
    }

    // ── Zamknięcie i restart: Background → Stopping → NotRunning | Starting ────

    [Theory]
    [InlineData("foreground", false)]
    [InlineData("foreground", true)]
    [InlineData("starting", false)]
    [InlineData("starting", true)]
    public void Close_or_restart_in_background_is_ignored_while_another_app_is_active(string other, bool restart)
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var last = other == "foreground"
            ? Foreground(orchestrator, "plex")
            : Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("plex")).Snapshot);

        var result = orchestrator.Handle(restart ? new RestartApp("netflix") : new CloseApp("netflix"));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(last.Rev + 1, next.Rev);
        Assert.Equal(last.Apps.Select(app => app.State), next.Apps.Select(app => app.State));
        Assert.Equal(AppLifecycleState.Background, StateOf(next, "netflix"));
        Assert.Equal(
            other == "foreground" ? AppLifecycleState.Foreground : AppLifecycleState.Starting,
            StateOf(next, "plex"));
    }

    [Fact]
    public void Close_app_in_background_moves_to_stopping_and_sends_stop_app_with_stop_timeout()
    {
        var (orchestrator, _) = Started();
        var background = Background(orchestrator, "netflix");

        var result = orchestrator.Handle(new CloseApp("netflix"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new StopApp(10, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(11, 2, "netflix", T0.AddSeconds(45)), command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(background.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Command_completed_for_stop_moves_to_not_running_and_cancels_stop_timeout()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);

        var result = orchestrator.Handle(new CommandCompleted(10));

        Assert.Equal(new CancelTimer(12, 2), Assert.Single(result.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Process_exited_in_stopping_moves_to_not_running_and_cancels_stop_timeout()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);

        var result = orchestrator.Handle(new ProcessExited("netflix"));

        Assert.Equal(new CancelTimer(12, 2), Assert.Single(result.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "netflix"));

        // Korelacja stopu jest już zużyta: spóźniony wynik komendy nie zmienia stanu drugi raz.
        var late = orchestrator.Handle(new CommandCompleted(10));
        Assert.Empty(late.Commands);
        Assert.Null(late.Snapshot);
    }

    [Fact]
    public void Restart_app_in_background_moves_to_stopping_and_sends_stop_app_with_stop_timeout()
    {
        var (orchestrator, _) = Started();
        var background = Background(orchestrator, "netflix");

        var result = orchestrator.Handle(new RestartApp("netflix"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new StopApp(10, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(11, 2, "netflix", T0.AddSeconds(45)), command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(background.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Command_completed_for_stop_restarts_app_in_starting()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new RestartApp("netflix")).Snapshot);

        var result = orchestrator.Handle(new CommandCompleted(10));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new StartApp(12, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(13, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(14, 2), command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x99, 7));
        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(15, 0x99), command),
            command => Assert.Equal(
                new SetInputRouting(16, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command),
            command => Assert.Equal(new CancelTimer(17, 3), command));
        Assert.Equal(AppLifecycleState.Foreground, StateOf(Assert.IsType<ShellSnapshot>(attached.Snapshot), "netflix"));
    }

    /// <summary>
    /// Restart jest decyzją podjętą przy <c>RestartApp</c>, więc Home albo wybudzenie w trakcie <c>Stopping</c>
    /// jest nowsze: po potwierdzonym stopie okno restartowanej aplikacji dostaje kompensację, a shell zostaje
    /// na wierzchu. Id z chwili decyzji, nie z potwierdzenia stopu, jest tym, co przesądza.
    /// </summary>
    [Theory]
    [InlineData("home")]
    [InlineData("resumed")]
    public void Restart_decided_before_home_or_wake_in_stopping_loses_its_window(string newer)
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        orchestrator.Handle(new RestartApp("netflix"));

        if (newer == "home")
        {
            var home = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 3));
            Assert.Empty(home.Commands);
            Assert.Null(home.Snapshot);
        }
        else
        {
            var resumed = orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));
            Assert.Empty(resumed.Commands);
            Assert.Equal(
                new FocusRequest(FocusRequest.DefaultTarget, 2),
                Assert.IsType<ShellSnapshot>(resumed.Snapshot).FocusRequest);
        }

        var stopped = orchestrator.Handle(new CommandCompleted(10));

        Assert.Collection(
            stopped.Commands,
            command => Assert.Equal(new StartApp(12, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(13, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(14, 2), command));
        var starting = Assert.IsType<ShellSnapshot>(stopped.Snapshot);
        Assert.Equal(AppLifecycleState.Starting, StateOf(starting, "netflix"));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new MinimizeWindow(15, 0x1A2B), command),
            command => Assert.Equal(new CancelTimer(16, 3), command));
        var background = Assert.IsType<ShellSnapshot>(attached.Snapshot);
        Assert.Equal(AppLifecycleState.Background, StateOf(background, "netflix"));
        Assert.Equal(starting.FocusRequest, background.FocusRequest);
    }

    [Fact]
    public void Process_exited_in_stopping_restarts_app_in_starting()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        orchestrator.Handle(new RestartApp("netflix"));

        var result = orchestrator.Handle(new ProcessExited("netflix"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new StartApp(12, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(13, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(14, 2), command));
        Assert.Equal(AppLifecycleState.Starting, StateOf(Assert.IsType<ShellSnapshot>(result.Snapshot), "netflix"));

        // Korelacja stopu jest już zużyta: spóźniony wynik komendy nie startuje aplikacji drugi raz.
        var late = orchestrator.Handle(new CommandCompleted(10));
        Assert.Empty(late.Commands);
        Assert.Null(late.Snapshot);
    }

    [Theory]
    [InlineData("notRunning", false)]
    [InlineData("notRunning", true)]
    [InlineData("starting", false)]
    [InlineData("starting", true)]
    [InlineData("foreground", false)]
    [InlineData("foreground", true)]
    [InlineData("stopping", false)]
    [InlineData("stopping", true)]
    public void Close_or_restart_outside_background_is_ignored(string state, bool restart)
    {
        var (orchestrator, started) = Started();
        var last = started;
        switch (state)
        {
            case "starting":
                last = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("netflix")).Snapshot);
                break;
            case "foreground":
                last = Foreground(orchestrator, "netflix");
                break;
            case "stopping":
                Background(orchestrator, "netflix");
                last = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);
                break;
        }

        var result = orchestrator.Handle(restart ? new RestartApp("netflix") : new CloseApp("netflix"));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(last.Rev + 1, next.Rev);
        Assert.Equal(last.Apps.Select(app => app.State), next.Apps.Select(app => app.State));
    }

    [Theory]
    [InlineData("launch")]
    [InlineData("close")]
    [InlineData("restart")]
    [InlineData("processExited")]
    [InlineData("cdpLost")]
    public void Messages_for_unknown_app_are_ignored(string kind)
    {
        var (orchestrator, started) = Started();
        IOrchestratorMessage message = kind switch
        {
            "launch" => new LaunchApp("nope"),
            "close" => new CloseApp("nope"),
            "restart" => new RestartApp("nope"),
            "processExited" => new ProcessExited("nope"),
            _ => new CdpLost("nope"),
        };

        var result = orchestrator.Handle(message);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(started.Rev + 1, next.Rev);
    }

    [Theory]
    [InlineData("starting", "processExited")]
    [InlineData("starting", "cdpLost")]
    [InlineData("background", "processExited")]
    [InlineData("background", "cdpLost")]
    public void Failure_in_starting_or_background_enters_failed_and_only_starting_cancels_timeout(string state, string fact)
    {
        var (orchestrator, _) = Started();
        var last = state == "starting"
            ? Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("netflix")).Snapshot)
            : Background(orchestrator, "netflix");
        Event failure = fact == "cdpLost" ? new CdpLost("netflix") : new ProcessExited("netflix");

        var result = orchestrator.Handle(failure);

        if (state == "starting")
        {
            Assert.Equal(new CancelTimer(4, 1), Assert.Single(result.Commands));
        }
        else
        {
            Assert.Empty(result.Commands);
        }

        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(last.Rev + 1, snapshot.Rev);
        Assert.Equal(last.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    [Theory]
    [InlineData("processExited")]
    [InlineData("cdpLost")]
    public void Failure_in_foreground_shows_shell_routes_shell_and_focuses_the_tile(string fact)
    {
        var (orchestrator, _) = Started();
        var foreground = Foreground(orchestrator, "netflix");
        Event failure = fact == "cdpLost" ? new CdpLost("netflix") : new ProcessExited("netflix");

        var result = orchestrator.Handle(failure);

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ShowShell(7), command),
            command => Assert.Equal(
                new SetInputRouting(8, InputMode.Shell, null, PointerPolicy.Blocked, 3),
                command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(foreground.Rev + 1, snapshot.Rev);
        Assert.Equal(new FocusRequest("netflix", foreground.FocusRequest!.Seq + 1), snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    /// <summary>
    /// Porażka startu może dotrzeć po <c>AppWindowAttached</c>: kafelek jest już w <c>Foreground</c>, ale korelacja
    /// startu wciąż żyje, więc awaria zachowuje się jak każda inna awaria na pierwszym planie.
    /// </summary>
    [Fact]
    public void Command_failed_for_start_after_attached_window_enters_failed_from_foreground()
    {
        var (orchestrator, _) = Started();
        var foreground = Foreground(orchestrator, "netflix");

        var result = orchestrator.Handle(new CommandFailed(2, ErrorCode.CdpUnavailable));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ShowShell(7), command),
            command => Assert.Equal(
                new SetInputRouting(8, InputMode.Shell, null, PointerPolicy.Blocked, 3),
                command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(foreground.Rev + 1, snapshot.Rev);
        Assert.Equal(new FocusRequest("netflix", 2), snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    /// <summary>
    /// <c>CdpLost</c> w <c>Stopping</c> należy do korelacji stopu: zamykana aplikacja kończy się <c>NotRunning</c>,
    /// a nie <c>Failed</c>.
    /// </summary>
    [Fact]
    public void Cdp_lost_in_stopping_is_ignored_and_close_finishes_not_running()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);

        var ignored = orchestrator.Handle(new CdpLost("netflix"));

        Assert.Empty(ignored.Commands);
        Assert.Null(ignored.Snapshot);
        var completed = orchestrator.Handle(new CommandCompleted(10));
        Assert.Equal(new CancelTimer(12, 2), Assert.Single(completed.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(completed.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "netflix"));
    }

    /// <summary>
    /// Spóźnione fakty nie wskrzeszają kafelka: <c>NotRunning</c> nie ma procesu, a <c>Failed</c> nie ma żywego cyklu.
    /// </summary>
    [Theory]
    [InlineData("notRunning", "processExited")]
    [InlineData("notRunning", "cdpLost")]
    [InlineData("failed", "processExited")]
    [InlineData("failed", "cdpLost")]
    public void Facts_outside_live_cycle_are_ignored_without_snapshot(string state, string fact)
    {
        var (orchestrator, _) = Started();
        if (state == "failed")
        {
            Foreground(orchestrator, "netflix");
            orchestrator.Handle(new CdpLost("netflix"));
        }

        Event failure = fact == "cdpLost" ? new CdpLost("netflix") : new ProcessExited("netflix");
        var result = orchestrator.Handle(failure);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(
            state == "failed" ? AppLifecycleState.Failed : AppLifecycleState.NotRunning,
            StateOf(snapshot, "netflix"));
    }

    /// <summary>
    /// <c>Stopping</c> bez korelacji stopu nie powstaje z żadnego publicznego przejścia (<c>OnStop</c> zawsze
    /// ją zakłada), więc obronną gałąź <c>FindPendingStop == null</c> przypinamy na stanie z usuniętą korelacją:
    /// ani fakt, ani spóźniony wynik komendy nie mogą jej zamknąć.
    /// </summary>
    [Theory]
    [InlineData("processExited")]
    [InlineData("cdpLost")]
    public void Facts_in_stopping_without_stop_correlation_are_ignored(string fact)
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);
        var pendingStops = typeof(CoreOrchestrator)
            .GetField("_pendingStops", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(orchestrator);
        ((System.Collections.IDictionary)pendingStops!).Clear();
        Event failure = fact == "cdpLost" ? new CdpLost("netflix") : new ProcessExited("netflix");

        var result = orchestrator.Handle(failure);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var late = orchestrator.Handle(new CommandCompleted(10));
        Assert.Empty(late.Commands);
        Assert.Null(late.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Command_failed_for_stop_keeps_stopping_and_later_process_exit_closes_it()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        orchestrator.Handle(new CloseApp("netflix"));

        var failed = orchestrator.Handle(new CommandFailed(10, ErrorCode.Unexpected));

        Assert.Empty(failed.Commands);
        Assert.Null(failed.Snapshot);
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(stopping, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(
            Assert.IsType<ShellSnapshot>(orchestrator.Handle(new ProcessExited("netflix")).Snapshot),
            "netflix"));
    }

    [Fact]
    public void Foreground_changed_yields_nothing_and_first_start_still_has_rev_one()
    {
        var orchestrator = Create();

        var result = orchestrator.Handle(new ForegroundChanged(1234));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var started = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(1, started.Rev);
    }

    // ── Failed: co pokazuje, co ignoruje i jak z niego wyjść ──────────────────

    [Theory]
    [InlineData("launch")]
    [InlineData("home")]
    [InlineData("processExited")]
    [InlineData("cdpLost")]
    [InlineData("attached")]
    public void Failed_ignores_inputs_without_commands_or_snapshot(string message)
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        orchestrator.Handle(new CdpLost("netflix"));

        IOrchestratorMessage ignored = message switch
        {
            "launch" => new LaunchApp("netflix"),
            "home" => new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2),
            "processExited" => new ProcessExited("netflix"),
            "cdpLost" => new CdpLost("netflix"),
            _ => new AppWindowAttached("netflix", 0x77, 7),
        };

        var result = orchestrator.Handle(ignored);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("processExited")]
    public void Restart_in_failed_stops_then_starts_and_foreground_on_attached_window(string stopResult)
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        var failed = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CdpLost("netflix")).Snapshot);

        var restart = orchestrator.Handle(new RestartApp("netflix"));

        Assert.Collection(
            restart.Commands,
            command => Assert.Equal(new StopApp(9, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(10, 2, "netflix", T0.AddSeconds(45)), command));
        var stopping = Assert.IsType<ShellSnapshot>(restart.Snapshot);
        Assert.Equal(failed.Rev + 1, stopping.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(stopping, "netflix"));

        var stopped = stopResult == "completed"
            ? orchestrator.Handle(new CommandCompleted(9))
            : orchestrator.Handle(new ProcessExited("netflix"));

        Assert.Collection(
            stopped.Commands,
            command => Assert.Equal(new StartApp(11, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(12, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(13, 2), command));
        var starting = Assert.IsType<ShellSnapshot>(stopped.Snapshot);
        Assert.Equal(stopping.Rev + 1, starting.Rev);
        Assert.Equal(AppLifecycleState.Starting, StateOf(starting, "netflix"));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x99, 7));

        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(14, 0x99), command),
            command => Assert.Equal(
                new SetInputRouting(15, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command),
            command => Assert.Equal(new CancelTimer(16, 3), command));
        var foreground = Assert.IsType<ShellSnapshot>(attached.Snapshot);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(foreground, "netflix"));
        Assert.Equal(new FocusRequest("netflix", 2), foreground.FocusRequest);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    public void Stale_start_result_after_restart_in_failed_does_not_change_restarted_app(string stale)
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        orchestrator.Handle(new CdpLost("netflix"));
        var restart = orchestrator.Handle(new RestartApp("netflix"));
        var stop = Assert.IsType<StopApp>(restart.Commands[0]);
        var starting = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CommandCompleted(stop.CmdId)).Snapshot);

        IOrchestratorMessage late = stale == "completed"
            ? new CommandCompleted(2)
            : new CommandFailed(2, ErrorCode.CdpUnavailable);
        var result = orchestrator.Handle(late);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(starting.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Close_in_failed_stops_then_finishes_not_running()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var failed = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new ProcessExited("netflix")).Snapshot);

        var close = orchestrator.Handle(new CloseApp("netflix"));

        Assert.Collection(
            close.Commands,
            command => Assert.Equal(new StopApp(10, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(11, 2, "netflix", T0.AddSeconds(45)), command));
        var stop = Assert.IsType<StopApp>(close.Commands[0]);
        var stopping = Assert.IsType<ShellSnapshot>(close.Snapshot);
        Assert.Equal(failed.Rev + 1, stopping.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(stopping, "netflix"));

        var completed = orchestrator.Handle(new CommandCompleted(stop.CmdId));

        Assert.Equal(new CancelTimer(12, 2), Assert.Single(completed.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(completed.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "netflix"));
        Assert.Equal(failed.FocusRequest, snapshot.FocusRequest);
    }

    [Theory]
    [InlineData("foreground", false)]
    [InlineData("foreground", true)]
    [InlineData("starting", false)]
    [InlineData("starting", true)]
    public void Close_or_restart_in_failed_is_ignored_while_another_app_is_active(string other, bool restart)
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        orchestrator.Handle(new CdpLost("netflix"));
        var last = other == "foreground"
            ? Foreground(orchestrator, "plex")
            : Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("plex")).Snapshot);

        var result = orchestrator.Handle(restart ? new RestartApp("netflix") : new CloseApp("netflix"));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(last.Rev + 1, next.Rev);
        Assert.Equal(AppLifecycleState.Failed, StateOf(next, "netflix"));
        Assert.Equal(
            other == "foreground" ? AppLifecycleState.Foreground : AppLifecycleState.Starting,
            StateOf(next, "plex"));
    }

    // ── Timeout Starting: timer w Orchestratorze ──────────────────────────────

    [Fact]
    public void Timer_elapsed_in_starting_moves_to_failed_without_commands_or_new_focus()
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        var starting = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("netflix")).Snapshot);
        time.Advance(TimeSpan.FromSeconds(30));

        var result = orchestrator.Handle(new TimerElapsed(1));

        Assert.Empty(result.Commands);
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(starting.Rev + 1, snapshot.Rev);
        Assert.Equal(starting.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    [Fact]
    public void Timer_elapsed_after_attached_window_is_ignored_and_state_stays_foreground()
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        orchestrator.Handle(new LaunchApp("netflix"));
        orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));
        time.Advance(TimeSpan.FromSeconds(30));

        var result = orchestrator.Handle(new TimerElapsed(1));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Timer_elapsed_with_unknown_id_is_ignored()
    {
        var (orchestrator, started) = Started();

        var result = orchestrator.Handle(new TimerElapsed(99));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(started.Rev + 1, snapshot.Rev);
    }

    [Fact]
    public void Restart_after_start_timeout_schedules_new_timer_and_stale_elapsed_is_ignored()
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        orchestrator.Handle(new LaunchApp("netflix"));
        time.Advance(TimeSpan.FromSeconds(30));
        var failed = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new TimerElapsed(1)).Snapshot);
        Assert.Equal(AppLifecycleState.Failed, StateOf(failed, "netflix"));

        var restart = orchestrator.Handle(new RestartApp("netflix"));
        var stop = Assert.IsType<StopApp>(restart.Commands[0]);
        var stopped = orchestrator.Handle(new CommandCompleted(stop.CmdId));

        Assert.Collection(
            stopped.Commands,
            command => Assert.Equal(new StartApp(6, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(7, 3, "netflix", T0.AddSeconds(60)), command),
            command => Assert.Equal(new CancelTimer(8, 2), command));
        var starting = Assert.IsType<ShellSnapshot>(stopped.Snapshot);
        Assert.Equal(AppLifecycleState.Starting, StateOf(starting, "netflix"));

        var late = orchestrator.Handle(new TimerElapsed(1));

        Assert.Empty(late.Commands);
        Assert.Null(late.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));
    }

    // ── Timeout Stopping: timer stopu ─────────────────────────────────────────

    [Fact]
    public void Timer_elapsed_in_stopping_moves_to_failed_without_commands_or_new_focus()
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(stopping, "netflix"));
        time.Advance(TimeSpan.FromSeconds(45));

        var result = orchestrator.Handle(new TimerElapsed(2));

        Assert.Empty(result.Commands);
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(stopping.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    [Fact]
    public void Confirmed_stop_before_timeout_keeps_not_running_and_late_elapsed_is_ignored()
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        Background(orchestrator, "netflix");
        orchestrator.Handle(new CloseApp("netflix"));
        var stopped = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CommandCompleted(10)).Snapshot);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(stopped, "netflix"));

        time.Advance(TimeSpan.FromSeconds(45));
        var late = orchestrator.Handle(new TimerElapsed(2));

        Assert.Empty(late.Commands);
        Assert.Null(late.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(stopped.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "netflix"));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("processExited")]
    public void Stop_result_after_stop_timeout_is_ignored(string late)
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        Background(orchestrator, "netflix");
        orchestrator.Handle(new CloseApp("netflix"));
        time.Advance(TimeSpan.FromSeconds(45));
        var failed = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new TimerElapsed(2)).Snapshot);
        Assert.Equal(AppLifecycleState.Failed, StateOf(failed, "netflix"));

        IOrchestratorMessage message = late switch
        {
            "completed" => new CommandCompleted(10),
            "failed" => new CommandFailed(10, ErrorCode.Unexpected),
            _ => new ProcessExited("netflix"),
        };
        var result = orchestrator.Handle(message);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(failed.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Restart_after_stop_timeout_schedules_new_timers_and_stale_elapsed_is_ignored()
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        Background(orchestrator, "netflix");
        orchestrator.Handle(new CloseApp("netflix"));
        time.Advance(TimeSpan.FromSeconds(45));
        Assert.Equal(
            AppLifecycleState.Failed,
            StateOf(Assert.IsType<ShellSnapshot>(orchestrator.Handle(new TimerElapsed(2)).Snapshot), "netflix"));

        var restart = orchestrator.Handle(new RestartApp("netflix"));

        Assert.Collection(
            restart.Commands,
            command => Assert.Equal(new StopApp(12, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(13, 3, "netflix", T0.AddSeconds(90)), command));
        Assert.Equal(
            AppLifecycleState.Stopping,
            StateOf(Assert.IsType<ShellSnapshot>(restart.Snapshot), "netflix"));

        var stopped = orchestrator.Handle(new CommandCompleted(12));

        Assert.Collection(
            stopped.Commands,
            command => Assert.Equal(new StartApp(14, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(15, 4, "netflix", T0.AddSeconds(75)), command),
            command => Assert.Equal(new CancelTimer(16, 3), command));
        var starting = Assert.IsType<ShellSnapshot>(stopped.Snapshot);
        Assert.Equal(AppLifecycleState.Starting, StateOf(starting, "netflix"));

        // Spóźnione terminy starego i anulowanego stopu nie zmieniają stanu nowego startu.
        foreach (var staleTimerId in new[] { 2L, 3L })
        {
            var late = orchestrator.Handle(new TimerElapsed(staleTimerId));
            Assert.Empty(late.Commands);
            Assert.Null(late.Snapshot);
        }

        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(starting.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));
    }

    // ── Cała ścieżka cyklu życia ──────────────────────────────────────────────

    [Fact]
    public void Lifecycle_walks_foreground_background_foreground_background_stopping_and_not_running()
    {
        var (orchestrator, _) = Started();

        var foreground = Foreground(orchestrator, "netflix");
        Assert.Equal(AppLifecycleState.Foreground, StateOf(foreground, "netflix"));

        var home = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));
        var background = Assert.IsType<ShellSnapshot>(home.Snapshot);
        Assert.Equal(AppLifecycleState.Background, StateOf(background, "netflix"));
        Assert.Collection(
            home.Commands,
            command => Assert.IsType<MinimizeWindow>(command),
            command => Assert.IsType<ShowShell>(command),
            command => Assert.IsType<SetInputRouting>(command));

        var resumed = orchestrator.Handle(new LaunchApp("netflix"));
        Assert.Equal(AppLifecycleState.Foreground, StateOf(Assert.IsType<ShellSnapshot>(resumed.Snapshot), "netflix"));
        Assert.IsType<ActivateWindow>(resumed.Commands[0]);

        orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 3));
        Assert.Equal(
            AppLifecycleState.Background,
            StateOf(Assert.IsType<ShellSnapshot>(
                orchestrator.Handle(new OrchestratorStarted()).Snapshot), "netflix"));

        var close = orchestrator.Handle(new CloseApp("netflix"));
        Assert.Equal(AppLifecycleState.Stopping, StateOf(Assert.IsType<ShellSnapshot>(close.Snapshot), "netflix"));
        Assert.Collection(
            close.Commands,
            command => Assert.IsType<StopApp>(command),
            command => Assert.IsType<ScheduleTimer>(command));
        var stopped = Assert.IsType<StopApp>(close.Commands[0]);

        var completed = orchestrator.Handle(new CommandCompleted(stopped.CmdId));

        Assert.IsType<CancelTimer>(Assert.Single(completed.Commands));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(Assert.IsType<ShellSnapshot>(completed.Snapshot), "netflix"));
    }

    [Fact]
    public void Lifecycle_walks_background_stopping_starting_and_foreground_on_restart()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");

        var restart = orchestrator.Handle(new RestartApp("netflix"));
        Assert.Equal(AppLifecycleState.Stopping, StateOf(Assert.IsType<ShellSnapshot>(restart.Snapshot), "netflix"));
        Assert.Collection(
            restart.Commands,
            command => Assert.IsType<StopApp>(command),
            command => Assert.IsType<ScheduleTimer>(command));
        var stopped = Assert.IsType<StopApp>(restart.Commands[0]);

        var completed = orchestrator.Handle(new CommandCompleted(stopped.CmdId));
        Assert.Equal(AppLifecycleState.Starting, StateOf(Assert.IsType<ShellSnapshot>(completed.Snapshot), "netflix"));
        Assert.Collection(
            completed.Commands,
            command => Assert.Equal(new StartApp(12, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(13, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(14, 2), command));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x99, 7));

        Assert.Equal(AppLifecycleState.Foreground, StateOf(Assert.IsType<ShellSnapshot>(attached.Snapshot), "netflix"));
        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(15, 0x99), command),
            command => Assert.Equal(
                new SetInputRouting(16, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command),
            command => Assert.Equal(new CancelTimer(17, 3), command));
    }
}
