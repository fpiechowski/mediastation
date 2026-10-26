using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Orchestrator;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

public class OrchestratorStopTests
{
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
            command => Assert.Equal(new StopApp(15, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(16, 2, "netflix", T0.AddSeconds(45)), command));
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

        var result = orchestrator.Handle(new CommandCompleted(15));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ForgetProcessTree(17, 4321), command),
            command => Assert.Equal(new CancelTimer(18, 2), command),
            command => AssertManaged(command, 19, 2));
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

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ForgetProcessTree(17, 4321), command),
            command => Assert.Equal(new CancelTimer(18, 2), command),
            command => AssertManaged(command, 19, 2));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "netflix"));

        // Korelacja stopu jest już zużyta: spóźniony wynik komendy nie zmienia stanu drugi raz.
        var late = orchestrator.Handle(new CommandCompleted(15));
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
            command => Assert.Equal(new StopApp(15, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(16, 2, "netflix", T0.AddSeconds(45)), command));
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

        var result = orchestrator.Handle(new CommandCompleted(15));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ForgetProcessTree(17, 4321), command),
            command => Assert.Equal(new StartApp(18, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(19, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(20, 2), command),
            command => AssertManaged(command, 21, 2));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(stopping.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x99, 7));
        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(22, 0x99), command),
            command => Assert.Equal(new UnmuteProcessTree(23, 7), command),
            command => Assert.Equal(
                new SetInputRouting(24, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command),
            command => Assert.Equal(new CancelTimer(25, 3), command),
            command => AssertManaged(command, 26, 3, (0x99, 7)));
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

        var stopped = orchestrator.Handle(new CommandCompleted(15));

        Assert.Collection(
            stopped.Commands,
            command => Assert.Equal(new ForgetProcessTree(17, 4321), command),
            command => Assert.Equal(new StartApp(18, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(19, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(20, 2), command),
            command => AssertManaged(command, 21, 2));
        var starting = Assert.IsType<ShellSnapshot>(stopped.Snapshot);
        Assert.Equal(AppLifecycleState.Starting, StateOf(starting, "netflix"));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new MuteProcessTree(22, 4321), command),
            command => Assert.Equal(new MinimizeWindow(23, 0x1A2B), command),
            command => Assert.Equal(new CancelTimer(24, 3), command),
            command => AssertManaged(command, 25, 3, (0x1A2B, 4321)));
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
            command => Assert.Equal(new ForgetProcessTree(17, 4321), command),
            command => Assert.Equal(new StartApp(18, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(19, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(20, 2), command),
            command => AssertManaged(command, 21, 2));
        Assert.Equal(AppLifecycleState.Starting, StateOf(Assert.IsType<ShellSnapshot>(result.Snapshot), "netflix"));

        // Korelacja stopu jest już zużyta: spóźniony wynik komendy nie startuje aplikacji drugi raz.
        var late = orchestrator.Handle(new CommandCompleted(15));
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
        var ignored = Assert.IsType<IgnoredIntent>(result.IgnoredIntent);
        Assert.Equal((restart ? "RestartApp" : "CloseApp", "netflix"), (ignored.Intent, ignored.AppId));
        Assert.Contains("nie pozwala na zatrzymanie", ignored.Reason);
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(last.Rev + 1, next.Rev);
        Assert.Equal(last.Apps.Select(app => app.State), next.Apps.Select(app => app.State));
    }

    [Theory]
    [InlineData(false, "CloseApp")]
    [InlineData(true, "RestartApp")]
    public void Close_or_restart_with_unknown_app_id_is_ignored_with_intent(bool restart, string name)
    {
        var (orchestrator, _) = Started();

        var result = orchestrator.Handle(restart ? new RestartApp("nope") : new CloseApp("nope"));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        Assert.Equal(new IgnoredIntent(name, "nope", "nieznany appId"), result.IgnoredIntent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_or_restart_while_another_app_is_active_is_ignored_with_intent(bool restart)
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        Foreground(orchestrator, "plex");

        var result = orchestrator.Handle(restart ? new RestartApp("netflix") : new CloseApp("netflix"));

        var ignored = Assert.IsType<IgnoredIntent>(result.IgnoredIntent);
        Assert.Equal("inna aplikacja jest aktywna", ignored.Reason);
    }
}
