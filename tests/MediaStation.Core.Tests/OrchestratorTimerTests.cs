using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

public class OrchestratorTimerTests
{
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
            command => Assert.Equal(new StartApp(8, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(9, 3, "netflix", T0.AddSeconds(60)), command),
            command => Assert.Equal(new CancelTimer(10, 2), command));
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

        Assert.IsType<SetManagedWindows>(Assert.Single(result.Commands));
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
        var stopped = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CommandCompleted(13)).Snapshot);
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
            "completed" => new CommandCompleted(12),
            "failed" => new CommandFailed(12, ErrorCode.Unexpected),
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
            command => Assert.Equal(new StopApp(16, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(17, 3, "netflix", T0.AddSeconds(90)), command));
        Assert.Equal(
            AppLifecycleState.Stopping,
            StateOf(Assert.IsType<ShellSnapshot>(restart.Snapshot), "netflix"));

        var stopped = orchestrator.Handle(new CommandCompleted(16));

        Assert.Collection(
            stopped.Commands,
            command => Assert.Equal(new StartApp(18, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(19, 4, "netflix", T0.AddSeconds(75)), command),
            command => Assert.Equal(new CancelTimer(20, 3), command));
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
}
