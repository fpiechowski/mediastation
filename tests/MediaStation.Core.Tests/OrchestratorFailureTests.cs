using System.Reflection;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

public class OrchestratorFailureTests
{
    /// <summary>
    /// Awaria aplikacji <c>netflix</c> po nazwie z <c>InlineData</c>. <see cref="PageVersionRejected"/> (AD-7) ma ten sam
    /// handler co <see cref="CdpLost"/>, więc każda teoria <c>cdpLost</c> sprawdza też <c>pageVersionRejected</c>.
    /// </summary>
    private static Event Failure(string fact) => fact switch
    {
        "cdpLost" => new CdpLost("netflix"),
        "pageVersionRejected" => new PageVersionRejected("netflix", 2),
        _ => new ProcessExited("netflix"),
    };

    [Theory]
    [InlineData("launch")]
    [InlineData("close")]
    [InlineData("restart")]
    [InlineData("processExited")]
    [InlineData("cdpLost")]
    [InlineData("pageVersionRejected")]
    [InlineData("attached")]
    public void Messages_for_unknown_app_are_ignored(string kind)
    {
        var (orchestrator, started) = Started();
        IOrchestratorMessage message = kind switch
        {
            "launch" => new LaunchApp("nope"),
            "close" => new CloseApp("nope"),
            "restart" => new RestartApp("nope"),
            "processExited" => new ProcessExited("nope"),
            "attached" => new AppWindowAttached("nope", 10, 20),
            "pageVersionRejected" => new PageVersionRejected("nope", 2),
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
    [InlineData("starting", "pageVersionRejected")]
    [InlineData("background", "processExited")]
    [InlineData("background", "cdpLost")]
    [InlineData("background", "pageVersionRejected")]
    public void Failure_in_starting_or_background_enters_failed_and_only_starting_cancels_timeout(string state, string fact)
    {
        var (orchestrator, _) = Started();
        var last = state == "starting"
            ? Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("netflix")).Snapshot)
            : Background(orchestrator, "netflix");
        Event failure = Failure(fact);

        var result = orchestrator.Handle(failure);

        if (state == "starting")
        {
            Assert.Equal(new CancelTimer(6, 1), Assert.Single(result.Commands));
        }
        else if (fact == "processExited")
        {
            Assert.Collection(
                result.Commands,
                command => Assert.Equal(new ForgetProcessTree(15, 4321), command),
                command => Assert.IsType<SetManagedWindows>(command));
        }
        else
        {
            Assert.IsType<SetManagedWindows>(Assert.Single(result.Commands));
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
    [InlineData("pageVersionRejected")]
    public void Failure_in_foreground_shows_shell_routes_shell_and_focuses_the_tile(string fact)
    {
        var (orchestrator, _) = Started();
        var foreground = Foreground(orchestrator, "netflix");
        Event failure = Failure(fact);

        var result = orchestrator.Handle(failure);

        if (fact != "processExited")
        {
            // Proces żyje: reguła zostaje, okno jest wyciszone i zminimalizowane przed shellem (AD-5).
            Assert.Collection(
                result.Commands,
                command => Assert.Equal(new MuteProcessTree(11, 4321), command),
                command => Assert.Equal(new MinimizeWindow(12, 0x1A2B), command),
                command => Assert.Equal(new ShowShell(13), command),
                command => Assert.Equal(
                    new SetInputRouting(14, InputMode.Shell, null, PointerPolicy.Blocked, 3),
                    command),
                command => AssertManaged(command, 15, 2));
        }
        else
        {
            // Proces zginął: reguła znika przed shellem, bez Mute i Minimize.
            Assert.Collection(
                result.Commands,
                command => Assert.Equal(new ForgetProcessTree(11, 4321), command),
                command => Assert.Equal(new ShowShell(12), command),
                command => Assert.Equal(
                    new SetInputRouting(13, InputMode.Shell, null, PointerPolicy.Blocked, 3),
                    command),
                command => AssertManaged(command, 14, 2));
        }

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

        var result = orchestrator.Handle(new CommandFailed(4, ErrorCode.CdpUnavailable));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new MuteProcessTree(11, 4321), command),
            command => Assert.Equal(new MinimizeWindow(12, 0x1A2B), command),
            command => Assert.Equal(new ShowShell(13), command),
            command => Assert.Equal(
                new SetInputRouting(14, InputMode.Shell, null, PointerPolicy.Blocked, 3),
                command),
            command => AssertManaged(command, 15, 2));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(foreground.Rev + 1, snapshot.Rev);
        Assert.Equal(new FocusRequest("netflix", 2), snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
    }

    /// <summary>
    /// <c>CdpLost</c> i <c>PageVersionRejected</c> w <c>Stopping</c> należą do korelacji stopu: zamykana aplikacja kończy
    /// się <c>NotRunning</c>, a nie <c>Failed</c>.
    /// </summary>
    [Theory]
    [InlineData("cdpLost")]
    [InlineData("pageVersionRejected")]
    public void Cdp_lost_in_stopping_is_ignored_and_close_finishes_not_running(string fact)
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);

        var ignored = orchestrator.Handle(Failure(fact));

        Assert.Empty(ignored.Commands);
        Assert.Null(ignored.Snapshot);
        var completed = orchestrator.Handle(new CommandCompleted(16));
        Assert.Collection(
            completed.Commands,
            command => Assert.Equal(new ForgetProcessTree(18, 4321), command),
            command => Assert.Equal(new CancelTimer(19, 2), command),
            command => AssertManaged(command, 20, 2));
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
    [InlineData("notRunning", "pageVersionRejected")]
    [InlineData("failed", "processExited")]
    [InlineData("failed", "cdpLost")]
    [InlineData("failed", "pageVersionRejected")]
    public void Facts_outside_live_cycle_are_ignored_without_snapshot(string state, string fact)
    {
        var (orchestrator, _) = Started();
        if (state == "failed")
        {
            Foreground(orchestrator, "netflix");
            orchestrator.Handle(new CdpLost("netflix"));

            // Reguła żywego procesu została zwolniona: dalsze fakty nie mają już czego robić.
            orchestrator.Handle(new ProcessExited("netflix"));
        }

        Event failure = Failure(fact);
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
    [InlineData("pageVersionRejected")]
    public void Facts_in_stopping_without_stop_correlation_are_ignored(string fact)
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);
        var pendingStops = typeof(CoreOrchestrator)
            .GetField("_pendingStops", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(orchestrator);
        ((System.Collections.IDictionary)pendingStops!).Clear();
        Event failure = Failure(fact);

        var result = orchestrator.Handle(failure);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var late = orchestrator.Handle(new CommandCompleted(13));
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

        var failed = orchestrator.Handle(new CommandFailed(13, ErrorCode.Unexpected));

        Assert.Empty(failed.Commands);
        Assert.Null(failed.Snapshot);
        var stopping = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(stopping, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(
            Assert.IsType<ShellSnapshot>(orchestrator.Handle(new ProcessExited("netflix")).Snapshot),
            "netflix"));
    }

    [Fact]
    public void Foreground_changed_yields_only_the_settle_timer_and_first_start_still_has_rev_one()
    {
        var orchestrator = Create();

        var result = orchestrator.Handle(new ForegroundChanged(1234, 8));

        var timer = Assert.IsType<ScheduleTimer>(Assert.Single(result.Commands));
        Assert.Equal(T0.AddMilliseconds(300), timer.Due);
        Assert.Null(result.Snapshot);
        var started = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(1, started.Rev);
    }

    // ── Failed: co pokazuje, co ignoruje i jak z niego wyjść ──────────────────

    [Fact]
    public void Failed_app_does_not_stop_home_from_reasserting_the_shell()
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        orchestrator.Handle(new CdpLost("netflix"));
        orchestrator.Handle(new ProcessExited("netflix"));

        var result = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));

        Assert.IsType<ShowShell>(Assert.Single(result.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
    }

    [Theory]
    [InlineData("launch")]
    [InlineData("processExited")]
    [InlineData("cdpLost")]
    [InlineData("pageVersionRejected")]
    [InlineData("attached")]
    public void Failed_ignores_inputs_without_commands_or_snapshot(string message)
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        orchestrator.Handle(new CdpLost("netflix"));
        orchestrator.Handle(new ProcessExited("netflix"));

        IOrchestratorMessage ignored = message switch
        {
            "launch" => new LaunchApp("netflix"),
            "processExited" => new ProcessExited("netflix"),
            "cdpLost" => new CdpLost("netflix"),
            "pageVersionRejected" => new PageVersionRejected("netflix", 2),
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
            command => Assert.Equal(new UnmuteProcessTree(16, 4321), command),
            command => Assert.Equal(new StopApp(17, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(18, 2, "netflix", T0.AddSeconds(45)), command));
        var stopping = Assert.IsType<ShellSnapshot>(restart.Snapshot);
        Assert.Equal(failed.Rev + 1, stopping.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(stopping, "netflix"));

        var stopped = stopResult == "completed"
            ? orchestrator.Handle(new CommandCompleted(17))
            : orchestrator.Handle(new ProcessExited("netflix"));

        Assert.Collection(
            stopped.Commands,
            command => Assert.Equal(new ForgetProcessTree(19, 4321), command),
            command => Assert.Equal(new StartApp(20, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(21, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(22, 2), command));
        var starting = Assert.IsType<ShellSnapshot>(stopped.Snapshot);
        Assert.Equal(stopping.Rev + 1, starting.Rev);
        Assert.Equal(AppLifecycleState.Starting, StateOf(starting, "netflix"));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x99, 7));

        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(23, 0x99), command),
            command => Assert.Equal(new UnmuteProcessTree(24, 7), command),
            command => Assert.Equal(
                new SetInputRouting(25, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command),
            command => Assert.Equal(new CancelTimer(26, 3), command),
            command => AssertManaged(command, 27, 3, (0x99, 7)));
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
        var stop = Assert.IsType<StopApp>(restart.Commands[1]);
        var starting = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CommandCompleted(stop.CmdId)).Snapshot);

        IOrchestratorMessage late = stale == "completed"
            ? new CommandCompleted(4)
            : new CommandFailed(4, ErrorCode.CdpUnavailable);
        var result = orchestrator.Handle(late);

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(starting.Rev + 1, snapshot.Rev);
        Assert.Equal(AppLifecycleState.Starting, StateOf(snapshot, "netflix"));
    }

    /// <summary>ProcessExited w Failed zwalnia zachowaną regułę dokładnie raz i nie zmienia widoku.</summary>
    [Fact]
    public void Process_exited_in_failed_with_kept_rule_forgets_it_once_without_snapshot()
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        orchestrator.Handle(new CdpLost("netflix"));

        var exited = orchestrator.Handle(new ProcessExited("netflix"));

        var forget = Assert.IsType<ForgetProcessTree>(Assert.Single(exited.Commands));
        Assert.Equal(4321, forget.RootPid);
        Assert.Null(exited.Snapshot);

        var again = orchestrator.Handle(new ProcessExited("netflix"));

        Assert.Empty(again.Commands);
        Assert.Null(again.Snapshot);
    }

    /// <summary>
    /// Awaria przy żywym procesie zostawia regułę wyciszenia: wyjście z <c>Failed</c> przez zamknięcie wysyła
    /// <c>ForgetProcessTree</c> dopiero po potwierdzonym stopie, nigdy razem ze <c>StopApp</c>.
    /// </summary>
    [Fact]
    public void Close_in_failed_with_live_process_forgets_the_rule_only_after_confirmed_stop()
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        orchestrator.Handle(new CdpLost("netflix"));

        var close = orchestrator.Handle(new CloseApp("netflix"));

        Assert.DoesNotContain(close.Commands, command => command is ForgetProcessTree);
        var stop = Assert.IsType<StopApp>(close.Commands[1]);

        var completed = orchestrator.Handle(new CommandCompleted(stop.CmdId));

        Assert.Equal(new ForgetProcessTree(19, 4321), completed.Commands[0]);
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(Assert.IsType<ShellSnapshot>(completed.Snapshot), "netflix"));
    }

    /// <summary>
    /// Wynik komendy okiennej albo audio ze starego <c>transitionId</c> (Mute, Minimize, Forget, Unmute) niczego
    /// nie zmienia: ani komend, ani snapshotu.
    /// </summary>
    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    public void Window_and_audio_results_of_stale_transition_change_nothing(string result)
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        var home = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));
        var closed = orchestrator.Handle(new CloseApp("netflix"));
        var stop = Assert.IsType<StopApp>(closed.Commands[1]);
        var forgotten = orchestrator.Handle(new CommandCompleted(stop.CmdId));
        var forget = Assert.IsType<ForgetProcessTree>(forgotten.Commands[0]);
        var before = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);

        foreach (var command in home.Commands.Concat([forget]).Where(command => command is WindowCommand or AudioCommand))
        {
            var outcome = orchestrator.Handle(result == "completed"
                ? new CommandCompleted(command.CmdId)
                : new CommandFailed(command.CmdId, ErrorCode.Unexpected));

            Assert.Empty(outcome.Commands);
            Assert.Null(outcome.Snapshot);
        }

        var after = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(before.Rev + 1, after.Rev);
        Assert.Equal(before.Apps.Select(app => app.State), after.Apps.Select(app => app.State));
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
            command => Assert.Equal(new StopApp(17, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(18, 2, "netflix", T0.AddSeconds(45)), command));
        var stop = Assert.IsType<StopApp>(close.Commands[0]);
        var stopping = Assert.IsType<ShellSnapshot>(close.Snapshot);
        Assert.Equal(failed.Rev + 1, stopping.Rev);
        Assert.Equal(AppLifecycleState.Stopping, StateOf(stopping, "netflix"));

        var completed = orchestrator.Handle(new CommandCompleted(stop.CmdId));

        Assert.Equal(new CancelTimer(19, 2), Assert.Single(completed.Commands));
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
}
