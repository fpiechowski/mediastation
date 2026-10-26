using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using Microsoft.Extensions.Time.Testing;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

public class OrchestratorStartupTests
{
    [Fact]
    public void First_started_yields_placeholder_with_not_running_apps_and_default_focus()
    {
        var orchestrator = Create();

        var result = orchestrator.Handle(new OrchestratorStarted());

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(
                new SetInputRouting(1, InputMode.Shell, null, PointerPolicy.Blocked, 1),
                command),
            command => Assert.Equal(new ProbeExisting(2, "netflix"), command),
            command => Assert.Equal(new ProbeExisting(3, "plex"), command));
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
        Assert.Collection(
            firstResult.Commands,
            command => Assert.Equal(
                new SetInputRouting(1, InputMode.Shell, null, PointerPolicy.Blocked, 1),
                command),
            command => Assert.Equal(new ProbeExisting(2, "netflix"), command),
            command => Assert.Equal(new ProbeExisting(3, "plex"), command));
        Assert.Collection(
            secondResult.Commands,
            command => Assert.Equal(
                new SetInputRouting(4, InputMode.Shell, null, PointerPolicy.Blocked, 2),
                command),
            command => Assert.Equal(new ProbeExisting(5, "netflix"), command),
            command => Assert.Equal(new ProbeExisting(6, "plex"), command));
    }

    [Fact]
    public void System_resumed_with_nothing_running_yields_default_focus_with_new_rev_and_seq()
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

    /// <summary>
    /// Wybudzenie z pierwszego planu chowa aplikację i oddaje shellowi wejście, a na końcu odświeża sesję
    /// każdej żywej aplikacji webowej. Aplikacja w <c>NotRunning</c> nie dostaje <see cref="RenewSession"/>,
    /// a Moonlight nie ma sesji CDP do odświeżenia.
    /// </summary>
    [Fact]
    public void System_resumed_from_foreground_minimizes_shows_shell_routes_shell_and_renews_only_live_web_apps()
    {
        var (orchestrator, started) = StartedWith(TwoWebAndMoonlight());
        Background(orchestrator, "moonlight-pc", 0x3C4D);
        Foreground(orchestrator, "netflix", 0x1A2B);

        var result = orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new MinimizeWindow(20, 0x1A2B), command),
            command => Assert.Equal(new ShowShell(21), command),
            command => Assert.Equal(
                new SetInputRouting(22, InputMode.Shell, null, PointerPolicy.Blocked, 5),
                command),
            command => Assert.Equal(new RenewSession(23, "netflix"), command));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(started.Rev + 6, snapshot.Rev);
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 2), snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.NotRunning, StateOf(snapshot, "plex-web"));
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "moonlight-pc"));
    }

    /// <summary>
    /// Wybudzenie z Home nie ma czego chować ani przestawiać: jedyną komendą jest odświeżenie sesji
    /// webowej aplikacji z <c>Background</c>.
    /// </summary>
    [Fact]
    public void System_resumed_from_home_renews_only_the_web_app()
    {
        var (orchestrator, _) = Started();
        Background(orchestrator, "netflix");
        Background(orchestrator, "plex", 0x2B3C);

        var result = orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));

        Assert.Equal(new RenewSession(22, "netflix"), Assert.Single(result.Commands));
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(new FocusRequest(FocusRequest.DefaultTarget, 2), snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "netflix"));
        Assert.Equal(AppLifecycleState.Background, StateOf(snapshot, "plex"));
    }

    /// <summary>
    /// Kolejność <see cref="RenewSession"/> idzie za <c>apps.json</c>, a nie za kolejnością zmian stanu:
    /// <c>plex-web</c> schodzi do <c>Background</c> pierwsze, ale odświeżenie dostaje po <c>netflix</c>.
    /// Moonlight nie ma sesji CDP, więc nie dostaje komendy.
    /// </summary>
    [Fact]
    public void System_resumed_renews_web_apps_in_apps_json_order_and_skips_moonlight()
    {
        var (orchestrator, _) = StartedWith(TwoWebAndMoonlight());
        Background(orchestrator, "plex-web", 0x2B3C);
        Background(orchestrator, "netflix", 0x1A2B);

        var result = orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));

        Assert.Collection(
            result.Commands,
            command => Assert.Equal(new RenewSession(23, "netflix"), command),
            command => Assert.Equal(new RenewSession(24, "plex-web"), command));
    }

    /// <summary>
    /// Wybudzenie w trakcie <c>Starting</c> nie odświeża sesji, bo aplikacja nie ma jej jeszcze czym odświeżyć;
    /// spóźnione okno przegrywa z nowszą decyzją i dostaje kompensację bez aktywacji.
    /// </summary>
    [Fact]
    public void System_resumed_while_app_is_starting_skips_renew_and_late_window_is_compensated()
    {
        var (orchestrator, _) = Started();
        orchestrator.Handle(new LaunchApp("netflix"));

        var resumed = orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));

        Assert.Empty(resumed.Commands);
        Assert.Equal(
            new FocusRequest(FocusRequest.DefaultTarget, 2),
            Assert.IsType<ShellSnapshot>(resumed.Snapshot).FocusRequest);

        var attached = orchestrator.Handle(new AppWindowAttached("netflix", 0x1A2B, 4321));

        Assert.Collection(
            attached.Commands,
            command => Assert.Equal(new MinimizeWindow(6, 0x1A2B), command),
            command => Assert.Equal(new CancelTimer(7, 1), command),
            command => AssertManaged(command, 8, 1, (0x1A2B, 4321)));
        Assert.DoesNotContain(attached.Commands, command => command is ActivateWindow or RenewSession);
        Assert.Equal(
            AppLifecycleState.Background,
            StateOf(Assert.IsType<ShellSnapshot>(attached.Snapshot), "netflix"));
    }

    /// <summary>
    /// Timeout <c>Starting</c> biegnie przez wybudzenie dalej: bez okna aplikacja kończy start w <c>Failed</c>,
    /// bez komend i bez nowego fokusu.
    /// </summary>
    [Fact]
    public void Start_timeout_after_system_resumed_enters_failed_without_new_focus()
    {
        var time = new FakeTimeProvider(T0);
        var (orchestrator, _) = Started(time);
        orchestrator.Handle(new LaunchApp("netflix"));
        var resumed = Assert.IsType<ShellSnapshot>(
            orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC")).Snapshot);
        time.Advance(TimeSpan.FromSeconds(30));

        var result = orchestrator.Handle(new TimerElapsed(1));

        Assert.Empty(result.Commands);
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(resumed.Rev + 1, snapshot.Rev);
        Assert.Equal(resumed.FocusRequest, snapshot.FocusRequest);
        Assert.Equal(AppLifecycleState.Failed, StateOf(snapshot, "netflix"));
    }

    /// <summary>
    /// Wybudzenie nie odświeża sesji aplikacji w <c>Failed</c> ani w <c>Stopping</c>: pierwsza nie ma żywego CDP,
    /// a druga należy do korelacji stopu i nie dostaje żadnych komend.
    /// </summary>
    [Theory]
    [InlineData("failed")]
    [InlineData("stopping")]
    public void System_resumed_does_not_renew_web_app_that_is_failed_or_stopping(string state)
    {
        var (orchestrator, _) = Started();
        ShellSnapshot before;
        if (state == "failed")
        {
            Foreground(orchestrator, "netflix");
            before = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CdpLost("netflix")).Snapshot);
        }
        else
        {
            Background(orchestrator, "netflix");
            before = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new CloseApp("netflix")).Snapshot);
        }

        var result = orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));

        Assert.Empty(result.Commands);
        var snapshot = Assert.IsType<ShellSnapshot>(result.Snapshot);
        Assert.Equal(before.Rev + 1, snapshot.Rev);
        Assert.Equal(
            state == "failed" ? AppLifecycleState.Failed : AppLifecycleState.Stopping,
            StateOf(snapshot, "netflix"));
    }
}
