using System.Reflection;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;

namespace MediaStation.Core.Tests;

public class OrchestratorTests
{
    private static LoadedConfig TwoApps() => new(
        new AppsConfig([
            new WebApp("netflix", AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222),
            new NativeApp("plex", AppCategory.Game, "Plex", "plex.png", "C:\\Plex\\Plex.exe"),
        ]),
        new InputConfig([], []),
        new Settings());

    private static (CoreOrchestrator Orchestrator, ShellSnapshot Started) Started()
    {
        var orchestrator = new CoreOrchestrator(TwoApps());
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
        var orchestrator = new CoreOrchestrator(TwoApps());

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
        var orchestrator = new CoreOrchestrator(TwoApps());

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
        var orchestrator = new CoreOrchestrator(TwoApps());
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
    public void Launch_app_in_not_running_moves_to_starting_and_sends_start_app()
    {
        var (orchestrator, started) = Started();

        var result = orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Equal(new StartApp(2, "netflix"), Assert.Single(result.Commands));
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
    public void Command_failed_for_start_moves_to_failed_without_commands_or_focus_change(ErrorCode errorCode)
    {
        var (orchestrator, _) = Started();
        var starting = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("netflix")).Snapshot);

        var result = orchestrator.Handle(new CommandFailed(2, errorCode));

        Assert.Empty(result.Commands);
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
        var orchestrator = new CoreOrchestrator(TwoApps());
        Event message = kind == "completed" ? new CommandCompleted(7) : new CommandFailed(7, ErrorCode.Unexpected);

        var result = orchestrator.Handle(message);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Attached_window_in_starting_moves_to_foreground_and_routes_web_input()
    {
        var (orchestrator, started) = Started();
        orchestrator.Handle(new LaunchApp("netflix"));

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ActivateWindow(3, 0x1A2B), command),
            command => Assert.Equal(
                new SetInputRouting(4, InputMode.App, AppType.Web, PointerPolicy.Blocked, 2),
                command));
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
            command => Assert.Equal(new ActivateWindow(3, 42), command),
            command => Assert.Equal(
                new SetInputRouting(4, InputMode.App, AppType.Native, PointerPolicy.Allowed, 2),
                command));
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
        Assert.Equal(new StartApp(2, "netflix"), Assert.Single(launched.Commands));
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
        var orchestrator = new CoreOrchestrator(TwoApps());
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
            command => Assert.Equal(new MinimizeWindow(5, 0x1A2B), command),
            command => Assert.Equal(new ShowShell(6), command),
            command => Assert.Equal(
                new SetInputRouting(7, InputMode.Shell, null, PointerPolicy.Blocked, 3),
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

    [Fact]
    public void Home_while_app_is_starting_is_ignored()
    {
        var (orchestrator, _) = Started();
        orchestrator.Handle(new LaunchApp("netflix"));

        var result = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Starting, StateOf(next, "netflix"));
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
            command => Assert.Equal(new ActivateWindow(8, 0x1A2B), command),
            command => Assert.Equal(
                new SetInputRouting(9, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
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
    public void Close_app_in_background_moves_to_stopping_and_sends_stop_app()
    {
        var (orchestrator, _) = Started();
        var background = Background(orchestrator, "netflix");

        var result = orchestrator.Handle(new CloseApp("netflix"));

        Assert.Equal(new StopApp(8, "netflix"), Assert.Single(result.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(background.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Command_completed_for_stop_moves_to_not_running()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);

        var result = orchestrator.Handle(new CommandCompleted(8));

        Assert.Empty(result.Commands);
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "netflix"));
    }

    [Fact]
    public void Process_exited_in_stopping_moves_to_not_running()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);

        var result = orchestrator.Handle(new ProcessExited("netflix"));

        Assert.Empty(result.Commands);
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "netflix"));

        // Korelacja stopu jest już zużyta: spóźniony wynik komendy nie zmienia stanu drugi raz.
        var late = orchestrator.Handle(new CommandCompleted(8));
        Assert.Empty(late.Commands);
        Assert.Null(late.Snapshot);
    }

    [Fact]
    public void Restart_app_in_background_moves_to_stopping_and_sends_stop_app()
    {
        var (orchestrator, _) = Started();
        var background = Background(orchestrator, "netflix");

        var result = orchestrator.Handle(new RestartApp("netflix"));

        Assert.Equal(new StopApp(8, "netflix"), Assert.Single(result.Commands));
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

        var result = orchestrator.Handle(new CommandCompleted(8));

        Assert.Equal(new StartApp(9, "netflix"), Assert.Single(result.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x99, 7));
        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(10, 0x99), command),
            command => Assert.Equal(
                new SetInputRouting(11, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command));
        Assert.Equal(AppLifecycleState.Foreground, StateOf(Assert.IsType<ShellSnapshot>(attached.Snapshot), "netflix"));
    }

    [Fact]
    public void Process_exited_in_stopping_restarts_app_in_starting()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        orchestrator.Handle(new RestartApp("netflix"));

        var result = orchestrator.Handle(new ProcessExited("netflix"));

        Assert.Equal(new StartApp(9, "netflix"), Assert.Single(result.Commands));
        Assert.Equal(AppLifecycleState.Starting, StateOf(Assert.IsType<ShellSnapshot>(result.Snapshot), "netflix"));

        // Korelacja stopu jest już zużyta: spóźniony wynik komendy nie startuje aplikacji drugi raz.
        var late = orchestrator.Handle(new CommandCompleted(8));
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
    public void Failure_in_starting_or_background_enters_failed_without_commands_or_new_focus(string state, string fact)
    {
        var (orchestrator, _) = Started();
        var last = state == "starting"
            ? Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("netflix")).Snapshot)
            : Background(orchestrator, "netflix");
        Event failure = fact == "cdpLost" ? new CdpLost("netflix") : new ProcessExited("netflix");

        var result = orchestrator.Handle(failure);

        Assert.Empty(result.Commands);
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
            command => Assert.Equal(new ShowShell(5), command),
            command => Assert.Equal(
                new SetInputRouting(6, InputMode.Shell, null, PointerPolicy.Blocked, 3),
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
            command => Assert.Equal(new ShowShell(5), command),
            command => Assert.Equal(
                new SetInputRouting(6, InputMode.Shell, null, PointerPolicy.Blocked, 3),
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
        var completed = orchestrator.Handle(new CommandCompleted(8));
        Assert.Empty(completed.Commands);
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
        var late = orchestrator.Handle(new CommandCompleted(8));
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

        var failed = orchestrator.Handle(new CommandFailed(8, ErrorCode.Unexpected));

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
        var orchestrator = new CoreOrchestrator(TwoApps());

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

        var stop = Assert.IsType<StopApp>(Assert.Single(restart.Commands));
        Assert.Equal("netflix", stop.AppId);
        var stopping = Assert.IsType<ShellSnapshot>(restart.Snapshot);
        Assert.Equal(failed.Rev + 1, stopping.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(stopping, "netflix"));

        var stopped = stopResult == "completed"
            ? orchestrator.Handle(new CommandCompleted(stop.CmdId))
            : orchestrator.Handle(new ProcessExited("netflix"));

        Assert.Equal(new StartApp(8, "netflix"), Assert.Single(stopped.Commands));
        var starting = Assert.IsType<ShellSnapshot>(stopped.Snapshot);
        Assert.Equal(stopping.Rev + 1, starting.Rev);
        Assert.Equal(AppLifecycleState.Starting, StateOf(starting, "netflix"));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x99, 7));

        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(9, 0x99), command),
            command => Assert.Equal(
                new SetInputRouting(10, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command));
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
        var stop = Assert.IsType<StopApp>(Assert.Single(restart.Commands));
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

        var stop = Assert.IsType<StopApp>(Assert.Single(close.Commands));
        Assert.Equal("netflix", stop.AppId);
        var stopping = Assert.IsType<ShellSnapshot>(close.Snapshot);
        Assert.Equal(failed.Rev + 1, stopping.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(stopping, "netflix"));

        var completed = orchestrator.Handle(new CommandCompleted(stop.CmdId));

        Assert.Empty(completed.Commands);
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
        var stopped = Assert.IsType<StopApp>(Assert.Single(close.Commands));

        var completed = orchestrator.Handle(new CommandCompleted(stopped.CmdId));

        Assert.Empty(completed.Commands);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(Assert.IsType<ShellSnapshot>(completed.Snapshot), "netflix"));
    }

    [Fact]
    public void Lifecycle_walks_background_stopping_starting_and_foreground_on_restart()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");

        var restart = orchestrator.Handle(new RestartApp("netflix"));
        Assert.Equal(AppLifecycleState.Stopping, StateOf(Assert.IsType<ShellSnapshot>(restart.Snapshot), "netflix"));
        var stopped = Assert.IsType<StopApp>(Assert.Single(restart.Commands));

        var completed = orchestrator.Handle(new CommandCompleted(stopped.CmdId));
        Assert.Equal(AppLifecycleState.Starting, StateOf(Assert.IsType<ShellSnapshot>(completed.Snapshot), "netflix"));
        Assert.Equal("netflix", Assert.IsType<StartApp>(Assert.Single(completed.Commands)).AppId);

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x99, 7));

        Assert.Equal(AppLifecycleState.Foreground, StateOf(Assert.IsType<ShellSnapshot>(attached.Snapshot), "netflix"));
        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(10, 0x99), command),
            command => Assert.Equal(
                new SetInputRouting(11, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command));
    }
}
