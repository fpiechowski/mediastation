using MediaStation.Core.Contracts;
using static MediaStation.Core.Tests.OrchestratorTestSupport;

namespace MediaStation.Core.Tests;

/// <summary>Routing Actions do Shell wg trybu wejścia Orchestratora (AD-8) i Home zawsze przekazywany.</summary>
public class OrchestratorActionTests
{
    private static Action Act(ActionKind kind, ActionPhase phase = ActionPhase.Press) =>
        new(kind, phase, ActionSource.Keyboard, 5);

    [Theory]
    [InlineData(ActionKind.Up, ActionPhase.Press)]
    [InlineData(ActionKind.Down, ActionPhase.Repeat)]
    [InlineData(ActionKind.Left, ActionPhase.Press)]
    [InlineData(ActionKind.Right, ActionPhase.Repeat)]
    [InlineData(ActionKind.Ok, ActionPhase.Press)]
    [InlineData(ActionKind.Ok, ActionPhase.Long)]
    [InlineData(ActionKind.Back, ActionPhase.Press)]
    public void Shell_mode_forwards_navigation_actions(ActionKind kind, ActionPhase phase)
    {
        var (orchestrator, _) = Started();
        var action = Act(kind, phase);

        var result = orchestrator.Handle(action);

        Assert.Equal([action], result.Actions);
        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
    }

    [Theory]
    [InlineData(ActionKind.Ok, ActionPhase.Repeat)]
    [InlineData(ActionKind.Left, ActionPhase.Long)]
    [InlineData(ActionKind.Back, ActionPhase.Repeat)]
    [InlineData(ActionKind.Back, ActionPhase.Long)]
    [InlineData(ActionKind.Keyboard, ActionPhase.Press)]
    [InlineData(ActionKind.AppMenu, ActionPhase.Press)]
    public void Shell_mode_drops_other_actions(ActionKind kind, ActionPhase phase)
    {
        var (orchestrator, _) = Started();

        Assert.Empty(orchestrator.Handle(Act(kind, phase)).Actions);
    }

    [Theory]
    [InlineData(ActionKind.Up)]
    [InlineData(ActionKind.Right)]
    [InlineData(ActionKind.Ok)]
    [InlineData(ActionKind.Back)]
    public void App_mode_forwards_nothing(ActionKind kind)
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");

        Assert.Empty(orchestrator.Handle(Act(kind)).Actions);
    }

    [Fact]
    public void Mode_returns_to_shell_after_home()
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        var home = Act(ActionKind.Home);

        var homeResult = orchestrator.Handle(home);
        var right = Act(ActionKind.Right);

        Assert.Equal([home], homeResult.Actions);
        Assert.Equal([right], orchestrator.Handle(right).Actions);
    }

    [Fact]
    public void Mode_returns_to_shell_after_foreground_app_fails()
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        Assert.Empty(orchestrator.Handle(Act(ActionKind.Left)).Actions);

        orchestrator.Handle(new ProcessExited("netflix"));

        var left = Act(ActionKind.Left);
        Assert.Equal([left], orchestrator.Handle(left).Actions);
    }

    [Fact]
    public void App_mode_after_resume_from_background_forwards_nothing()
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        orchestrator.Handle(Act(ActionKind.Home));
        orchestrator.Handle(new LaunchApp("netflix"));

        Assert.Empty(orchestrator.Handle(Act(ActionKind.Right)).Actions);
    }

    [Fact]
    public void Wake_with_foreground_app_returns_mode_to_shell()
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");

        orchestrator.Handle(new SystemResumed("PBT_APMRESUMEAUTOMATIC"));

        var right = Act(ActionKind.Right);
        Assert.Equal([right], orchestrator.Handle(right).Actions);
    }

    [Fact]
    public void Home_is_always_forwarded_even_when_nothing_changed()
    {
        var (orchestrator, _) = Started();
        var home = Act(ActionKind.Home);

        var result = orchestrator.Handle(home);

        Assert.Equal([home], result.Actions);
        Assert.Empty(result.Commands);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Home_from_foreground_keeps_commands_snapshot_and_forwards_action()
    {
        var (orchestrator, _) = Started();
        Foreground(orchestrator, "netflix");
        var home = Act(ActionKind.Home);

        var result = orchestrator.Handle(home);

        Assert.Equal(3, result.Commands.Count);
        Assert.NotNull(result.Snapshot);
        Assert.Equal([home], result.Actions);
    }

    [Fact]
    public void Snapshot_carries_title_from_apps_json()
    {
        var (_, started) = Started();

        Assert.Equal(["Netflix", "Plex"], started.Apps.Select(app => app.Title));
    }
}
