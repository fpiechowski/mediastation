using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using Microsoft.Extensions.Time.Testing;
using MediaStation.Core.Orchestrator;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

public class OrchestratorLaunchTests
{
    [Fact]
    public void Launch_app_in_not_running_moves_to_starting_and_schedules_start_timeout()
    {
        var (orchestrator, started) = Started();

        var result = orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new StartApp(4, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(5, 1, "netflix", T0.AddSeconds(30)), command));
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
        var intent = Assert.IsType<IgnoredIntent>(ignored.IgnoredIntent);
        Assert.Equal(("LaunchApp", "netflix"), (intent.Intent, intent.AppId));
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
        Assert.Equal(new IgnoredIntent("LaunchApp", "nope", "nieznany appId"), result.IgnoredIntent);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(started.Rev + 1, next.Rev);
    }

    [Fact]
    public void Command_completed_for_start_keeps_starting_and_yields_nothing()
    {
        var (orchestrator, _) = Started();
        orchestrator.Handle(new LaunchApp("netflix"));

        var result = orchestrator.Handle(new CommandCompleted(4));

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

        var result = orchestrator.Handle(new CommandFailed(4, errorCode));

        Assert.Equal(new CancelTimer(6, 1), Assert.Single(result.Commands));
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
            command => Assert.Equal(new ActivateWindow(6, 0x1A2B), command),
            command => Assert.Equal(new UnmuteProcessTree(7, 4321), command),
            command => Assert.Equal(
                new SetInputRouting(8, InputMode.App, AppType.Web, PointerPolicy.Blocked, 2),
                command),
            command => Assert.Equal(new CancelTimer(9, 1), command),
            command => AssertManaged(command, 10, 1, (0x1A2B, 4321)));
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
            command => Assert.Equal(new ActivateWindow(6, 42), command),
            command => Assert.Equal(new UnmuteProcessTree(7, 24), command),
            command => Assert.Equal(
                new SetInputRouting(8, InputMode.App, AppType.Native, PointerPolicy.Allowed, 2),
                command),
            command => Assert.Equal(new CancelTimer(9, 1), command),
            command => AssertManaged(command, 10, 1, (42, 24)));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "plex"));
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
            command => Assert.Equal(new ActivateWindow(15, 0x1A2B), command),
            command => Assert.Equal(new UnmuteProcessTree(16, 4321), command),
            command => Assert.Equal(
                new SetInputRouting(17, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Launch_not_running_app_while_another_app_is_active_is_ignored_and_never_yields_two_foregrounds(bool otherStarting)
    {
        var (orchestrator, _) = Started();
        var last = otherStarting
            ? Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("plex")).Snapshot)
            : Foreground(orchestrator, "plex");

        var launch = orchestrator.Handle(new LaunchApp("netflix"));
        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x2B3C, 8765));

        Assert.Empty(launch.Commands);
        Assert.Null(launch.Snapshot);
        Assert.DoesNotContain(attached.Commands, command => command is StartApp or ActivateWindow);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        foreach (var snapshot in new[] { attached.Snapshot, next })
        {
            Assert.True((snapshot as ShellSnapshot)?.Apps.Count(app => app.State == AppLifecycleState.Foreground) is null or <= 1);
        }
        Assert.Equal(last.Apps.Count(app => app.State == AppLifecycleState.Foreground), next.Apps.Count(app => app.State == AppLifecycleState.Foreground));
        Assert.Equal(AppLifecycleState.Background, StateOf(next, "netflix"));
    }

    [Fact]
    public void Restart_confirmed_stop_ends_not_running_when_another_app_took_the_foreground_meanwhile()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "plex");
        orchestrator.Handle(new RestartApp("plex"));
        Foreground(orchestrator, "netflix");

        var result = orchestrator.Handle(new ProcessExited("plex"));

        Assert.DoesNotContain(result.Commands, command => command is StartApp);
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "netflix"));
    }
}
