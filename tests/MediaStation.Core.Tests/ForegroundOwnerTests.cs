using MediaStation.Core.Contracts;
using MediaStation.Core.Orchestrator;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

/// <summary>Rozpoznanie właściciela okna z <see cref="ForegroundChanged"/> (AD-8, AD-15) i brak reakcji Orchestratora.</summary>
public class ForegroundOwnerTests
{
    private const long Shell = 0x500;

    private static readonly ForegroundApp[] Apps =
    [
        new("netflix", 0x1A2B, 4321),
        new("plex", 0, 777),
    ];

    private sealed class FakeLookup : IForegroundLookup
    {
        public long ShellHwnd => Shell;
    }

    [Fact]
    public void Shell_handle_is_the_shell()
    {
        var result = ForegroundOwnerResolver.Resolve(Shell, 99, Shell, Apps);

        Assert.Equal(new ForegroundObservation(Shell, 99, ForegroundOwner.Shell, null), result);
    }

    [Fact]
    public void App_window_is_matched_by_hwnd_before_pid()
    {
        var result = ForegroundOwnerResolver.Resolve(0x1A2B, 777, Shell, Apps);

        Assert.Equal(new ForegroundObservation(0x1A2B, 777, ForegroundOwner.App, "netflix"), result);
    }

    [Fact]
    public void Other_window_of_the_app_process_is_matched_by_root_pid()
    {
        var result = ForegroundOwnerResolver.Resolve(0x9999, 777, Shell, Apps);

        Assert.Equal(new ForegroundObservation(0x9999, 777, ForegroundOwner.App, "plex"), result);
    }

    [Fact]
    public void Unknown_window_is_foreign()
    {
        var result = ForegroundOwnerResolver.Resolve(0x9999, 55, Shell, Apps);

        Assert.Equal(new ForegroundObservation(0x9999, 55, ForegroundOwner.Foreign, null), result);
    }

    [Fact]
    public void Zero_pid_never_matches_an_app_without_a_root_pid()
    {
        var apps = new ForegroundApp[] { new("plex", 0, 0) };

        var result = ForegroundOwnerResolver.Resolve(0x9999, 0, Shell, apps);

        Assert.Equal(ForegroundOwner.Foreign, result.Owner);
    }

    [Fact]
    public void Zero_hwnd_is_foreign_even_when_the_shell_is_unknown()
    {
        var result = ForegroundOwnerResolver.Resolve(0, 4321, 0, Apps);

        Assert.Equal(new ForegroundObservation(0, 4321, ForegroundOwner.Foreign, null), result);
    }

    [Fact]
    public void Orchestrator_reports_shell_app_by_hwnd_app_by_root_pid_and_foreign()
    {
        var orchestrator = new MediaStation.Core.Orchestrator.Orchestrator(
            TwoApps(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(T0),
            new FakeLookup());
        orchestrator.Handle(new OrchestratorStarted());
        Foreground(orchestrator, "netflix");

        Assert.Equal(
            new ForegroundObservation(Shell, 1, ForegroundOwner.Shell, null),
            orchestrator.Handle(new ForegroundChanged(Shell, 1)).Foreground);
        Assert.Equal(
            new ForegroundObservation(0x1A2B, 2, ForegroundOwner.App, "netflix"),
            orchestrator.Handle(new ForegroundChanged(0x1A2B, 2)).Foreground);
        Assert.Equal(
            new ForegroundObservation(0x7001, 4321, ForegroundOwner.App, "netflix"),
            orchestrator.Handle(new ForegroundChanged(0x7001, 4321)).Foreground);
        Assert.Equal(
            new ForegroundObservation(0x7002, 12, ForegroundOwner.Foreign, null),
            orchestrator.Handle(new ForegroundChanged(0x7002, 12)).Foreground);
        Assert.Equal(
            new ForegroundObservation(0, 0, ForegroundOwner.Foreign, null),
            orchestrator.Handle(new ForegroundChanged(0, 0)).Foreground);
    }

    [Fact]
    public void Foreground_changed_leaves_commands_snapshot_and_state_untouched()
    {
        var orchestrator = new MediaStation.Core.Orchestrator.Orchestrator(
            TwoApps(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(T0),
            new FakeLookup());
        orchestrator.Handle(new OrchestratorStarted());
        Background(orchestrator, "netflix");

        foreach (var hwnd in new long[] { Shell, 0x1A2B, 0x9999, 0 })
        {
            var result = orchestrator.Handle(new ForegroundChanged(hwnd, 5));

            Assert.Empty(result.Commands);
            Assert.Null(result.Snapshot);
            Assert.Empty(result.Actions);
        }

        // Stan bez zmian: Netflix zostaje w tle (AD-8), powrót daje dopiero Home.
        var next = Assert.IsType<ShellSnapshot>(orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC")).Snapshot);
        Assert.Equal(AppLifecycleState.Background, StateOf(next, "netflix"));
    }

    [Fact]
    public void Foreground_changed_without_lookup_is_foreign()
    {
        var orchestrator = Create();
        orchestrator.Handle(new OrchestratorStarted());

        var result = orchestrator.Handle(new ForegroundChanged(1234, 8));

        Assert.Equal(new ForegroundObservation(1234, 8, ForegroundOwner.Foreign, null), result.Foreground);
    }
}
