using MediaStation.Core.Contracts;
using MediaStation.Core.Orchestrator;
using Microsoft.Extensions.Time.Testing;
using static MediaStation.Core.Tests.OrchestratorTestSupport;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;

namespace MediaStation.Core.Tests;

/// <summary>Polityka <c>ForegroundChanged</c> (AD-5, AD-15): ustalenie, echo, przełączenie z zewnątrz i unieważnienia.</summary>
public class OrchestratorForegroundTests
{
    private const long ShellHwnd = 0x500;
    private const long NetflixHwnd = 0x1A2B;
    private const int NetflixPid = 4321;
    private const long PlexHwnd = 0x3C4D;
    private const int PlexPid = 777;

    private sealed class FakeLookup : IForegroundLookup
    {
        public long ShellHwnd => OrchestratorForegroundTests.ShellHwnd;
    }

    private static CoreOrchestrator NewOrchestrator()
    {
        var orchestrator = new CoreOrchestrator(TwoApps(), new FakeTimeProvider(T0), new FakeLookup());
        orchestrator.Handle(new OrchestratorStarted());
        return orchestrator;
    }

    private static Action HomeAction() => new(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1);

    /// <summary>Wysyła <see cref="CommandCompleted"/> dla każdej komendy okiennej wyniku i zwraca wynik ostatniego.</summary>
    private static OrchestratorResult CompleteWindowCommands(CoreOrchestrator orchestrator, OrchestratorResult result)
    {
        var last = OrchestratorResult.None;
        foreach (var window in result.Commands.OfType<WindowCommand>())
        {
            last = orchestrator.Handle(new CommandCompleted(window.CmdId));
        }

        return last;
    }

    /// <summary>Aplikacja w <c>Foreground</c> z domkniętymi komendami okiennymi.</summary>
    private static OrchestratorResult StartForeground(CoreOrchestrator orchestrator, string appId, long hwnd, int pid)
    {
        orchestrator.Handle(new LaunchApp(appId));
        var attached = orchestrator.Handle(new AppWindowAttached(appId, hwnd, pid));
        CompleteWindowCommands(orchestrator, attached);
        return attached;
    }

    private static void HomeSettled(CoreOrchestrator orchestrator)
    {
        CompleteWindowCommands(orchestrator, orchestrator.Handle(HomeAction()));
    }

    private static long TimerOf(OrchestratorResult result) =>
        Assert.Single(result.Commands.OfType<ScheduleTimer>()).TimerId;

    private static OrchestratorResult Observe(CoreOrchestrator orchestrator, long hwnd, int pid, out long timerId)
    {
        var result = orchestrator.Handle(new ForegroundChanged(hwnd, pid));
        timerId = TimerOf(result);
        return result;
    }

    private static OrchestratorResult Settle(CoreOrchestrator orchestrator, long hwnd, int pid)
    {
        Observe(orchestrator, hwnd, pid, out var timerId);
        return orchestrator.Handle(new TimerElapsed(timerId));
    }

    private static void AssertNoEffect(OrchestratorResult result)
    {
        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
        Assert.Null(result.ForegroundSwitch);
    }

    private static ShellSnapshot Snapshot(OrchestratorResult result) => Assert.IsType<ShellSnapshot>(result.Snapshot);

    [Fact]
    public void Settle_timer_is_scheduled_for_foreground_settle_ms()
    {
        var orchestrator = NewOrchestrator();

        var result = Observe(orchestrator, 0x9999, 5, out _);

        var timer = Assert.Single(result.Commands.OfType<ScheduleTimer>());
        Assert.Equal(T0.AddMilliseconds(300), timer.Due);
    }

    [Fact]
    public void Echo_of_activate_window_yields_nothing_after_settling()
    {
        var orchestrator = NewOrchestrator();
        orchestrator.Handle(new LaunchApp("netflix"));
        var attached = orchestrator.Handle(new AppWindowAttached("netflix", NetflixHwnd, NetflixPid));
        var activate = attached.Commands.OfType<ActivateWindow>().Single();
        orchestrator.Handle(new CommandCompleted(activate.CmdId));

        var result = Settle(orchestrator, NetflixHwnd, NetflixPid);

        AssertNoEffect(result);
    }

    [Fact]
    public void Echo_of_show_shell_yields_nothing_after_settling()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        HomeSettled(orchestrator);

        var result = Settle(orchestrator, ShellHwnd, 10);

        AssertNoEffect(result);
    }

    [Fact]
    public void Only_the_last_observation_is_evaluated_after_its_own_settle_time()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        Observe(orchestrator, 0x9999, 5, out var first);

        var second = Observe(orchestrator, ShellHwnd, 10, out var secondTimer);

        Assert.Contains(second.Commands.OfType<CancelTimer>(), cancel => cancel.TimerId == first);
        AssertNoEffect(orchestrator.Handle(new TimerElapsed(first)));
        var settled = orchestrator.Handle(new TimerElapsed(secondTimer));
        Assert.Equal(ForegroundOwner.Shell, settled.ForegroundSwitch!.Owner);
    }

    [Fact]
    public void Last_observation_wins_over_an_earlier_switching_one()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        Observe(orchestrator, ShellHwnd, 10, out var shellTimer);
        Observe(orchestrator, NetflixHwnd, NetflixPid, out var appTimer);

        AssertNoEffect(orchestrator.Handle(new TimerElapsed(shellTimer)));
        AssertNoEffect(orchestrator.Handle(new TimerElapsed(appTimer)));
    }

    [Fact]
    public void Observation_with_a_window_command_without_result_is_not_evaluated_and_is_reported_after_the_result()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        var home = orchestrator.Handle(HomeAction());
        var minimize = home.Commands.OfType<MinimizeWindow>().Single();
        var showShell = home.Commands.OfType<ShowShell>().Single();
        Observe(orchestrator, ShellHwnd, 10, out var timerId);

        AssertNoEffect(orchestrator.Handle(new TimerElapsed(timerId)));
        var afterMinimize = orchestrator.Handle(new CommandCompleted(minimize.CmdId));
        Assert.Empty(afterMinimize.Commands.OfType<ReportForeground>());
        var afterShowShell = orchestrator.Handle(new CommandCompleted(showShell.CmdId));

        var report = Assert.Single(afterShowShell.Commands.OfType<ReportForeground>());

        // Świeża obserwacja z raportu jest oceniana po wyniku raportu.
        Observe(orchestrator, ShellHwnd, 10, out _);
        var completed = orchestrator.Handle(new CommandCompleted(report.CmdId));
        var fresh = TimerOf(completed);
        AssertNoEffect(orchestrator.Handle(new TimerElapsed(fresh)));
    }

    [Fact]
    public void Report_result_restarts_the_timer_and_a_mismatching_report_switches()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        var home = orchestrator.Handle(HomeAction());
        Observe(orchestrator, 0x9999, 5, out _);
        var result = CompleteWindowCommands(orchestrator, home);
        var report = Assert.Single(result.Commands.OfType<ReportForeground>());

        // Użytkownik wrócił do Netflix w trakcie przejścia: raport to ujawnia.
        Observe(orchestrator, NetflixHwnd, NetflixPid, out var observed);
        var completed = orchestrator.Handle(new CommandCompleted(report.CmdId));
        var restarted = TimerOf(completed);
        Assert.NotEqual(observed, restarted);
        Assert.Contains(completed.Commands.OfType<CancelTimer>(), cancel => cancel.TimerId == observed);

        AssertNoEffect(orchestrator.Handle(new TimerElapsed(observed)));
        var settled = orchestrator.Handle(new TimerElapsed(restarted));
        Assert.Equal("netflix", settled.ForegroundSwitch!.AppId);
    }

    [Fact]
    public void Observation_during_starting_is_not_evaluated_and_is_reported_when_starting_ends()
    {
        var orchestrator = NewOrchestrator();
        orchestrator.Handle(new LaunchApp("netflix"));
        Observe(orchestrator, ShellHwnd, 10, out var timerId);

        AssertNoEffect(orchestrator.Handle(new TimerElapsed(timerId)));
        var attached = orchestrator.Handle(new AppWindowAttached("netflix", NetflixHwnd, NetflixPid));
        var activate = attached.Commands.OfType<ActivateWindow>().Single();
        Assert.Empty(attached.Commands.OfType<ReportForeground>());

        var completed = orchestrator.Handle(new CommandCompleted(activate.CmdId));

        Assert.Single(completed.Commands.OfType<ReportForeground>());
    }

    [Fact]
    public void Change_of_managed_windows_discards_the_pending_evaluation_and_reports()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        Observe(orchestrator, ShellHwnd, 10, out var timerId);

        var adopted = orchestrator.Handle(new AppWindowAttached("plex", PlexHwnd, PlexPid));
        Assert.Contains(adopted.Commands.OfType<CancelTimer>(), cancel => cancel.TimerId == timerId);
        Assert.Empty(adopted.Commands.OfType<ReportForeground>());
        AssertNoEffect(orchestrator.Handle(new TimerElapsed(timerId)));

        var completed = CompleteWindowCommands(orchestrator, adopted);

        Assert.Single(completed.Commands.OfType<ReportForeground>());
    }

    [Fact]
    public void Window_command_failure_with_a_mismatching_observation_only_reports_the_failure()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        var home = orchestrator.Handle(HomeAction());
        var minimize = home.Commands.OfType<MinimizeWindow>().Single();
        var showShell = home.Commands.OfType<ShowShell>().Single();
        Observe(orchestrator, NetflixHwnd, NetflixPid, out var timerId);

        var failed = orchestrator.Handle(new CommandFailed(minimize.CmdId, ErrorCode.Unexpected));

        Assert.Equal(ForegroundOwner.App, failed.ForegroundTransitionFailed!.Owner);
        Assert.Equal("netflix", failed.ForegroundTransitionFailed.AppId);
        Assert.Null(failed.ForegroundSwitch);
        Assert.Null(failed.Snapshot);
        Assert.Empty(failed.Commands.OfType<ReportForeground>());
        AssertNoEffect(orchestrator.Handle(new TimerElapsed(timerId)));
        AssertNoEffect(orchestrator.Handle(new CommandCompleted(showShell.CmdId)));
    }

    [Fact]
    public void Window_command_failure_with_a_matching_observation_logs_nothing_and_restarts_the_timer()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        var home = orchestrator.Handle(HomeAction());
        var minimize = home.Commands.OfType<MinimizeWindow>().Single();
        Observe(orchestrator, ShellHwnd, 10, out _);

        var failed = orchestrator.Handle(new CommandFailed(minimize.CmdId, ErrorCode.Unexpected));

        Assert.Null(failed.ForegroundTransitionFailed);
        Assert.Single(failed.Commands.OfType<ScheduleTimer>());
    }

    [Fact]
    public void Shell_on_top_leaves_the_foreground_app_without_show_shell_or_focus_request()
    {
        var orchestrator = NewOrchestrator();
        var before = Snapshot(StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid));

        var result = Settle(orchestrator, ShellHwnd, 10);

        var commands = result.Commands.ToArray();
        Assert.Equal(3, commands.Length);
        Assert.Equal(NetflixPid, Assert.IsType<MuteProcessTree>(commands[0]).RootPid);
        Assert.Equal(NetflixHwnd, Assert.IsType<MinimizeWindow>(commands[1]).Hwnd);
        var routing = Assert.IsType<SetInputRouting>(commands[2]);
        Assert.Equal(InputMode.Shell, routing.Mode);
        Assert.Equal(PointerPolicy.Blocked, routing.Pointer);
        Assert.DoesNotContain(commands, command => command is ShowShell or ActivateWindow);
        var snapshot = Snapshot(result);
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "netflix"));
        Assert.Equal(before.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(ForegroundOwner.Shell, result.ForegroundSwitch!.Owner);
        Assert.Null(result.ForegroundSwitch.AppId);
    }

    [Fact]
    public void Background_app_on_top_is_activated_without_activate_window()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        HomeSettled(orchestrator);

        var result = Settle(orchestrator, NetflixHwnd, NetflixPid);

        var commands = result.Commands.ToArray();
        Assert.Equal(2, commands.Length);
        Assert.Equal(NetflixPid, Assert.IsType<UnmuteProcessTree>(commands[0]).RootPid);
        var routing = Assert.IsType<SetInputRouting>(commands[1]);
        Assert.Equal(InputMode.App, routing.Mode);
        Assert.Equal(AppType.Web, routing.AppType);
        Assert.Equal(PointerPolicy.Blocked, routing.Pointer);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(Snapshot(result), "netflix"));
        Assert.Equal(new ForegroundSwitch(ForegroundOwner.App, "netflix", result.ForegroundSwitch!.TransitionId), result.ForegroundSwitch);
    }

    [Fact]
    public void Alt_tab_from_app_a_to_app_b_mutes_and_minimizes_a_then_unmutes_b()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        HomeSettled(orchestrator);
        var plexForeground = Snapshot(StartForeground(orchestrator, "plex", PlexHwnd, PlexPid));

        var result = Settle(orchestrator, NetflixHwnd, NetflixPid);

        var commands = result.Commands.ToArray();
        Assert.Equal(4, commands.Length);
        Assert.Equal(PlexPid, Assert.IsType<MuteProcessTree>(commands[0]).RootPid);
        Assert.Equal(PlexHwnd, Assert.IsType<MinimizeWindow>(commands[1]).Hwnd);
        Assert.Equal(NetflixPid, Assert.IsType<UnmuteProcessTree>(commands[2]).RootPid);
        var routingB = Assert.IsType<SetInputRouting>(commands[3]);
        Assert.Equal(InputMode.App, routingB.Mode);
        Assert.Equal(PointerPolicy.Blocked, routingB.Pointer);
        Assert.DoesNotContain(commands, command => command is ActivateWindow or ShowShell);
        var snapshot = Snapshot(result);
        Assert.Equal(AppLifecycleState.Foreground, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "plex"));
        Assert.Equal(plexForeground.FocusRequest, snapshot.FocusRequest);
    }

    [Fact]
    public void Foreign_owner_and_hwnd_zero_change_nothing()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);

        AssertNoEffect(Settle(orchestrator, 0x9999, 5));
        AssertNoEffect(Settle(orchestrator, 0, 0));
    }

    [Fact]
    public void App_that_is_stopping_is_only_logged()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        HomeSettled(orchestrator);
        orchestrator.Handle(new CloseApp("netflix"));

        var result = Settle(orchestrator, NetflixHwnd, NetflixPid);

        AssertNoEffect(result);
    }

    [Fact]
    public void Stale_settle_timer_after_a_new_transition_does_not_evaluate_and_a_fresh_report_follows_the_results()
    {
        var orchestrator = NewOrchestrator();
        StartForeground(orchestrator, "netflix", NetflixHwnd, NetflixPid);
        Observe(orchestrator, ShellHwnd, 10, out var timerId);

        var home = orchestrator.Handle(HomeAction());
        Assert.Contains(home.Commands.OfType<CancelTimer>(), cancel => cancel.TimerId == timerId);
        AssertNoEffect(orchestrator.Handle(new TimerElapsed(timerId)));

        var completed = CompleteWindowCommands(orchestrator, home);

        Assert.Single(completed.Commands.OfType<ReportForeground>());
    }
}
