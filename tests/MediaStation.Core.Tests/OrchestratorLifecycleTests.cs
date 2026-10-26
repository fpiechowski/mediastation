using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

public class OrchestratorLifecycleTests
{
// ── Bramka foreground (AD-8): zbiór okien dla InputHub ─────────────────────

    /// <summary>
    /// Zbiór okien z <c>Hwnd</c> idzie do InputHub na końcu każdego wyniku, który go zmienia: attach z <c>rootPid</c>,
    /// adopcja, zamknięcie i awaria. Home, powrót z tła i wybudzenie go nie ruszają, więc nie ma duplikatów,
    /// a <c>epoch</c> zbioru rośnie osobno od <c>epoch</c> routingu.
    /// </summary>
    [Fact]
    public void Managed_windows_are_published_only_when_the_set_changes()
    {
        var (orchestrator, _) = Started();

        var attached = orchestrator.Handle(new LaunchApp("netflix"));
        Assert.DoesNotContain(attached.Commands, command => command is SetManagedWindows);
        attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));
        var first = Assert.IsType<SetManagedWindows>(attached.Commands[^1]);
        Assert.Equal([new ManagedWindow(0x1A2B, 4321)], first.Apps);
        Assert.Null(first.OskHwnd);

        var home = orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 2));
        var back = orchestrator.Handle(new LaunchApp("netflix"));
        var resumed = orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));
        Assert.DoesNotContain(home.Commands.Concat(back.Commands).Concat(resumed.Commands), command => command is SetManagedWindows);

        var adopted = orchestrator.Handle(new AppWindowAttached("plex", 0x2B3C, 24));
        Assert.Single(adopted.Commands.OfType<SetManagedWindows>());

        orchestrator.Handle(new Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 3));
        var failed = orchestrator.Handle(new CdpLost("netflix"));
        var afterFail = Assert.IsType<SetManagedWindows>(failed.Commands[^1]);
        Assert.Equal([new ManagedWindow(0x2B3C, 24)], afterFail.Apps);
        Assert.True(afterFail.Epoch > first.Epoch);
        Assert.True(afterFail.CmdId > first.CmdId);
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
            command => Assert.IsType<MuteProcessTree>(command),
            command => Assert.IsType<MinimizeWindow>(command),
            command => Assert.IsType<ShowShell>(command),
            command => Assert.IsType<SetInputRouting>(command));

        var resumed = orchestrator.Handle(new LaunchApp("netflix"));
        Assert.Equal(AppLifecycleState.Foreground, StateOf(Assert.IsType<ShellSnapshot>(resumed.Snapshot), "netflix"));
        Assert.IsType<ActivateWindow>(resumed.Commands[0]);
        Assert.IsType<UnmuteProcessTree>(resumed.Commands[1]);

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

        Assert.Collection(
            completed.Commands,
            command => Assert.IsType<CancelTimer>(command),
            command => Assert.IsType<SetManagedWindows>(command));
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
            command => Assert.Equal(new StartApp(17, "netflix"), command),
            command => Assert.Equal(new ScheduleTimer(18, 3, "netflix", T0.AddSeconds(30)), command),
            command => Assert.Equal(new CancelTimer(19, 2), command),
            command => AssertManaged(command, 20, 2));

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x99, 7));

        Assert.Equal(AppLifecycleState.Foreground, StateOf(Assert.IsType<ShellSnapshot>(attached.Snapshot), "netflix"));
        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new ActivateWindow(21, 0x99), command),
            command => Assert.Equal(new UnmuteProcessTree(22, 7), command),
            command => Assert.Equal(
                new SetInputRouting(23, InputMode.App, AppType.Web, PointerPolicy.Blocked, 4),
                command),
            command => Assert.Equal(new CancelTimer(24, 3), command),
            command => AssertManaged(command, 25, 3, (0x99, 7)));
    }
}
