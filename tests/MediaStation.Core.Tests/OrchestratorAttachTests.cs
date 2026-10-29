using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

public class OrchestratorAttachTests
{
    // ── Adopcja żywych aplikacji po restarcie ─────────────────────────────────

    /// <summary>
    /// Okno bez wcześniejszego <c>StartApp</c> jest adopcją: minimalizacja, shell na wierzchu i <c>Background</c>
    /// z zapisanym <c>hwnd</c>. Adopcja nie jest decyzją o pierwszym planie, więc nie rusza fokusu, trybu ani timerów.
    /// </summary>
    [Fact]
    public void Attached_window_without_start_is_adopted_into_background_and_keeps_shell_on_top()
    {
        var (orchestrator, started) = Started();

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new MuteProcessTree(4, 4321), command),
            command => Assert.Equal(new MinimizeWindow(5, 0x1A2B), command),
            command => Assert.Equal(new ShowShell(6), command),
            command => AssertManaged(command, 7, 1, (0x1A2B, 4321)));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(started.Rev + 1, snapshot.Rev);
        Assert.Equal(started.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(1, snapshot.FocusRequest!.Seq);
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex"));
        Assert.DoesNotContain(result.Commands, command => command is ActivateWindow or SetInputRouting or ScheduleTimer);
    }

    /// <summary>
    /// Powrót po adopcji: zapisane <c>hwnd</c> wystarcza, żeby wejść na pierwszy plan z routingiem aplikacji
    /// i nowym <c>epoch</c>.
    /// </summary>
    [Fact]
    public void Launch_after_adoption_activates_window_and_routes_app_input()
    {
        var (orchestrator, _) = Started();
        var adopted = Assert.IsType<ShellSnapshot>(
            orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321)).Snapshot);

        var result = orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new ActivateWindow(8, 0x1A2B), command),
            command => Assert.Equal(new UnmuteProcessTree(9, 4321), command),
            command => Assert.Equal(
                new SetInputRouting(10, InputMode.App, AppType.Web, PointerPolicy.Blocked, 2),
                command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(adopted.Rev + 1, snapshot.Rev);
        Assert.Equal(adopted.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "netflix"));
    }

    /// <summary>
    /// Dwie adopcje po kolei: każda dostaje <c>MinimizeWindow</c> i <c>ShowShell</c>, bo żadna nie jest aktywna,
    /// a fokus zostaje domyślny z <c>seq</c> 1.
    /// </summary>
    [Fact]
    public void Second_adoption_minimizes_both_windows_and_keeps_default_focus()
    {
        var (orchestrator, started) = Started();

        var first = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));
        var second = orchestrator.Handle(new AppWindowAttached("plex", 0x2B3C, 24));

        Assert.Collection(
            first.Commands,
            command => Assert.Equal(new MuteProcessTree(4, 4321), command),
            command => Assert.Equal(new MinimizeWindow(5, 0x1A2B), command),
            command => Assert.Equal(new ShowShell(6), command),
            command => AssertManaged(command, 7, 1, (0x1A2B, 4321)));
        Assert.Collection(
            second.Commands,
            command => Assert.Equal(new MuteProcessTree(8, 24), command),
            command => Assert.Equal(new MinimizeWindow(9, 0x2B3C), command),
            command => Assert.Equal(new ShowShell(10), command),
            command => AssertManaged(command, 11, 2, (0x1A2B, 4321), (0x2B3C, 24)));
        var snapshot = Assert.IsType<ShellSnapshot>(second.Snapshot);
        Assert.Equal(started.Rev + 2, snapshot.Rev);
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 1), snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "plex"));
    }

    /// <summary>
    /// Sonda jest fire-and-forget, więc adopcja może dotrzeć, gdy inna aplikacja jest już na pierwszym planie
    /// albo w trakcie startu: wtedy tylko minimalizacja, bez <c>ShowShell</c> i bez routingu, a druga aplikacja
    /// zachowuje stan.
    /// </summary>
    [Theory]
    [InlineData("foreground")]
    [InlineData("starting")]
    public void Adoption_while_another_app_is_active_only_minimizes_the_adopted_window(string other)
    {
        var (orchestrator, _) = Started();
        var last = other == "foreground"
            ? Foreground(orchestrator, "plex")
            : Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("plex")).Snapshot);
        var expectedMinimizeCmdId = other == "foreground" ? 11L : 6L;

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new MuteProcessTree(expectedMinimizeCmdId, 4321), command),
            command => Assert.Equal(new MinimizeWindow(expectedMinimizeCmdId + 1, 0x1A2B), command),
            command => Assert.IsType<SetManagedWindows>(command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(last.Rev + 1, snapshot.Rev);
        Assert.Equal(last.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "netflix"));
        Assert.Equal(
            other == "foreground" ? AppLifecycleState.Foreground : AppLifecycleState.Starting,
            StateOf(snapshot, "plex"));
    }

    /// <summary>
    /// Adopcja Moonlighta: ten sam batch co dla przeglądarki, a routing wejścia aplikacji nigdy nie leci,
    /// bo adopcja nie jest decyzją o pierwszym planie.
    /// </summary>
    [Fact]
    public void Moonlight_attached_without_start_is_adopted_without_app_routing()
    {
        var (orchestrator, started) = StartedWith(WebAndMoonlight());

        var result = orchestrator.Handle(new AppWindowAttached("moonlight-pc", 0x3C4D, 77));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new MuteProcessTree(4, 77), command),
            command => Assert.Equal(new MinimizeWindow(5, 0x3C4D), command),
            command => Assert.Equal(new ShowShell(6), command),
            command => AssertManaged(command, 7, 1, (0x3C4D, 77)));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(started.Rev + 1, snapshot.Rev);
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 1), snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "moonlight-pc"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "netflix"));
    }

    /// <summary>
    /// Adopcja nie jest decyzją o pierwszym planie, więc nie podbija <c>transitionId</c>: aplikacja uruchomiona
    /// równolegle dalej wygrywa ze swoim oknem i dostaje <c>ActivateWindow</c> oraz routing, a nie kompensację.
    /// </summary>
    [Fact]
    public void Adoption_while_another_app_is_starting_leaves_its_start_decision_current()
    {
        var (orchestrator, _) = Started();
        var starting = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new LaunchApp("plex")).Snapshot);

        var adopted = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Collection(
            adopted.Commands,
            command => Assert.Equal(new MuteProcessTree(6, 4321), command),
            command => Assert.Equal(new MinimizeWindow(7, 0x1A2B), command),
            command => AssertManaged(command, 8, 1, (0x1A2B, 4321)));
        var afterAdoption = Assert.IsType<ShellSnapshot>(adopted.Snapshot);
        Assert.Equal(AppLifecycleState.Background, StateOf(afterAdoption, "netflix"));
        Assert.Equal(AppLifecycleState.Starting, StateOf(afterAdoption, "plex"));
        Assert.Equal(starting.FocusRequest, afterAdoption.FocusRequest);

        var attached = orchestrator.Handle(new AppWindowAttached("plex", 0x2B3C, 24));

        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(9, 0x2B3C), command),
            command => Assert.Equal(new UnmuteProcessTree(10, 24), command),
            command => Assert.Equal(
                new SetInputRouting(11, InputMode.App, AppType.Native, PointerPolicy.Allowed, 2),
                command),
            command => Assert.Equal(new CancelTimer(12, 1), command),
            command => AssertManaged(command, 13, 2, (0x1A2B, 4321), (0x2B3C, 24)));
        var snapshot = Assert.IsType<ShellSnapshot>(attached.Snapshot);
        Assert.Equal(starting.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "plex"));
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "netflix"));
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("background")]
    [InlineData("stopping")]
    public void Attached_window_outside_not_running_and_starting_is_ignored(string state)
    {
        var (orchestrator, _) = Started();
        switch (state)
        {
            case "foreground":
                Foreground(orchestrator, "netflix");
                break;
            case "background":
                Background(orchestrator, "netflix");
                break;
            default:
                Background(orchestrator, "netflix");
                Assert.IsType<StopApp>(orchestrator.Handle(new CloseApp("netflix")).Commands[1]);
                break;
        }

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 10, 20));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(
            state switch
            {
                "foreground" => AppLifecycleState.Foreground,
                "background" => AppLifecycleState.Background,
                _ => AppLifecycleState.Stopping,
            },
            StateOf(snapshot, "netflix"));
    }

    // ── DW-4: spóźnione okno po timeoucie Starting ────────────────────────────

    /// <summary>
    /// Start, timeout <c>Starting</c> (<c>Failed</c>), potem okno: kompensacja jak przy adopcji (wyciszenie,
    /// minimalizacja, shell), ale stan zostaje <c>Failed</c> bez <c>hwnd</c>, bez snapshotu i bez zmiany fokusu.
    /// </summary>
    [Fact]
    public void Late_window_after_start_timeout_is_muted_minimized_and_hidden_behind_the_shell()
    {
        var (orchestrator, _) = Started();
        TimedOut(orchestrator);

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        // Bez snapshotu nie ma też nowego żądania fokusu; bez `hwnd` nie ma SetManagedWindows ani routingu.
        Assert.Collection(
            result.Commands,
            command => Assert.Equal(4321, Assert.IsType<MuteProcessTree>(command).RootPid),
            command => Assert.Equal(0x1A2B, Assert.IsType<MinimizeWindow>(command).Hwnd),
            command => Assert.IsType<ShowShell>(command));
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
    }

    /// <summary>Inna aplikacja jest aktywna: okno jest wyciszane i minimalizowane, ale shell nie wraca nad nią.</summary>
    [Theory]
    [InlineData("foreground")]
    [InlineData("starting")]
    public void Late_window_while_another_app_is_active_does_not_show_the_shell(string other)
    {
        var (orchestrator, _) = Started();
        TimedOut(orchestrator);
        if (other == "foreground")
        {
            Foreground(orchestrator, "plex", 0x2B3C);
        }
        else
        {
            orchestrator.Handle(new LaunchApp("plex"));
        }

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x77, 77));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(77, Assert.IsType<MuteProcessTree>(command).RootPid),
            command => Assert.Equal(0x77, Assert.IsType<MinimizeWindow>(command).Hwnd));
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
        Assert.Equal(other == "foreground" ? AppLifecycleState.Foreground : AppLifecycleState.Starting, StateOf(snapshot, "plex"));
    }

    /// <summary>Zapisany <c>RootPid</c> spóźnionego okna: restart odcisza drzewo przed stopem i zapomina regułę po nim.</summary>
    [Fact]
    public void Restart_after_late_window_unmutes_before_stop_and_forgets_the_tree_after_it()
    {
        var (orchestrator, _) = Started();
        TimedOut(orchestrator);
        orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        var restart = orchestrator.Handle(new RestartApp("netflix"));

        Assert.Null(restart.IgnoredIntent);
        Assert.Collection(
            restart.Commands,
            command => Assert.Equal(4321, Assert.IsType<UnmuteProcessTree>(command).RootPid),
            command => Assert.Equal("netflix", Assert.IsType<StopApp>(command).AppId),
            command => Assert.IsType<ScheduleTimer>(command));
        var stop = restart.Commands.OfType<StopApp>().Single();

        var stopped = orchestrator.Handle(new CommandCompleted(stop.CmdId));

        Assert.Collection(
            stopped.Commands,
            command => Assert.Equal(4321, Assert.IsType<ForgetProcessTree>(command).RootPid),
            command => Assert.Equal("netflix", Assert.IsType<StartApp>(command).AppId),
            command => Assert.IsType<ScheduleTimer>(command),
            command => Assert.IsType<CancelTimer>(command));
        Assert.Equal(AppLifecycleState.Starting, StateOf(Assert.IsType<ShellSnapshot>(stopped.Snapshot), "netflix"));
    }

    /// <summary>
    /// Flaga spóźnionego okna jest jednorazowa: drugi attach, attach po <see cref="ProcessExited"/> w <c>Failed</c>
    /// i attach w <c>Failed</c> z innej awarii po zamknięciu kafelka są ignorowane (bez komend i snapshotu).
    /// </summary>
    [Theory]
    [InlineData("second")]
    [InlineData("afterProcessExited")]
    [InlineData("afterClose")]
    public void Late_window_compensation_is_single_use(string when)
    {
        var (orchestrator, _) = Started();
        TimedOut(orchestrator);
        switch (when)
        {
            case "second":
                Assert.NotEmpty(orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321)).Commands);
                break;
            case "afterProcessExited":
                orchestrator.Handle(new ProcessExited("netflix"));
                break;
            default:
                var stop = Assert.Single(orchestrator.Handle(new CloseApp("netflix")).Commands.OfType<StopApp>());
                orchestrator.Handle(new CommandCompleted(stop.CmdId));
                orchestrator.Handle(new LaunchApp("netflix"));
                orchestrator.Handle(new CdpLost("netflix"));
                break;
        }

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x77, 77));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        var snapshot = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new OrchestratorStarted()).Snapshot);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
    }

    /// <summary>Flagę spóźnionego okna stawia tylko timeout <c>Starting</c>: <c>Failed</c> po timeoucie <c>Stopping</c> ignoruje attach.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Attach_after_a_stopping_timeout_is_ignored(bool restart)
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        var stop = orchestrator.Handle(restart ? new RestartApp("netflix") : new CloseApp("netflix"));
        var timer = Assert.Single(stop.Commands.OfType<ScheduleTimer>());
        var failed = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new TimerElapsed(timer.TimerId)).Snapshot);
        Assert.Equal(AppLifecycleState.Failed, StateOf(failed, "netflix"));

        var result = orchestrator.Handle(new AppWindowAttached("netflix", 0x77, 77));

        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
    }

    /// <summary><c>LaunchApp</c> i timeout <c>Starting</c> (pierwszy timer): kafelek w <c>Failed</c> bez okna.</summary>
    private static ShellSnapshot TimedOut(CoreOrchestrator orchestrator)
    {
        var launch = orchestrator.Handle(new LaunchApp("netflix"));
        var timer = Assert.Single(launch.Commands.OfType<ScheduleTimer>());
        var failed = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new TimerElapsed(timer.TimerId)).Snapshot);
        Assert.Equal(AppLifecycleState.Failed, StateOf(failed, "netflix"));
        return failed;
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
}
