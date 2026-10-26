using MediaStation.App.Platform.Audio;

namespace MediaStation.Core.Tests;

/// <summary>Dopasowanie procesu do drzewa od <c>rootPid</c> po łańcuchu rodziców oraz reguła drzew (<c>AudioRule</c>).</summary>
public class ProcessTreeMatcherTests
{
    private static readonly IReadOnlyDictionary<int, int> Parents = ProcessTreeMatcher.ToParentMap(
    [
        new(1, 0), new(100, 1), new(200, 100), new(300, 200), new(900, 1),
    ]);

    [Theory]
    [InlineData(100, 0)]
    [InlineData(200, 1)]
    [InlineData(300, 2)]
    public void Root_and_descendants_are_in_the_tree_with_their_distance(int pid, int distance) =>
        Assert.Equal(distance, ProcessTreeMatcher.DistanceToRoot(pid, 100, Parents));

    [Theory]
    [InlineData(900)]
    [InlineData(1)]
    [InlineData(4000)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Foreign_unknown_and_invalid_pids_are_outside_the_tree(int pid) =>
        Assert.False(ProcessTreeMatcher.IsInTree(pid, 100, Parents));

    [Fact]
    public void Non_positive_root_matches_nothing() =>
        Assert.False(ProcessTreeMatcher.IsInTree(300, 0, Parents));

    [Fact]
    public void Cycle_in_the_snapshot_terminates_without_a_match()
    {
        var cyclic = ProcessTreeMatcher.ToParentMap([new(10, 20), new(20, 10)]);

        Assert.False(ProcessTreeMatcher.IsInTree(10, 100, cyclic));
    }

    [Fact]
    public void Self_parent_terminates_without_a_match()
    {
        var self = ProcessTreeMatcher.ToParentMap([new(10, 10)]);

        Assert.False(ProcessTreeMatcher.IsInTree(10, 100, self));
    }

    [Fact]
    public void Duplicate_pid_in_the_snapshot_keeps_the_last_parent()
    {
        var map = ProcessTreeMatcher.ToParentMap([new(300, 900), new(300, 200)]);

        Assert.Equal(200, map[300]);
    }

    [Fact]
    public void Nested_roots_resolve_to_the_nearest_root()
    {
        Assert.Equal(200, ProcessTreeMatcher.FindRoot(300, [100, 200], Parents));
        Assert.Equal(200, ProcessTreeMatcher.FindRoot(300, [200, 100], Parents));
    }

    [Fact]
    public void Find_root_returns_null_for_a_foreign_process() =>
        Assert.Null(ProcessTreeMatcher.FindRoot(900, [100, 200], Parents));

    [Fact]
    public void Rule_governs_by_the_closest_root_and_reports_muted_state()
    {
        var rule = new AudioRule();
        rule.Set(100, true);
        rule.Set(200, false);

        var governing = rule.Governing(300, Parents);

        Assert.Equal(200, governing);
        Assert.False(rule.IsMuted(200));
        Assert.True(rule.IsMuted(100));
        Assert.Null(rule.IsMuted(300));
        Assert.True(rule.AnyMuted);
    }

    [Fact]
    public void Rule_set_replaces_the_previous_state_of_a_root()
    {
        var rule = new AudioRule();
        rule.Set(100, true);
        rule.Set(100, false);

        Assert.False(rule.AnyMuted);
        Assert.Equal([100], rule.Roots);
    }

    [Fact]
    public void Prune_dead_removes_roots_missing_from_the_snapshot()
    {
        var rule = new AudioRule();
        rule.Set(100, true);
        rule.Set(777, true);

        var dead = rule.PruneDead(Parents);

        Assert.Equal([777], dead);
        Assert.Equal([100], rule.Roots);
    }

    [Fact]
    public void Prune_dead_ignores_an_empty_snapshot()
    {
        var rule = new AudioRule();
        rule.Set(100, true);

        var dead = rule.PruneDead(new Dictionary<int, int>());

        Assert.Empty(dead);
        Assert.True(rule.AnyMuted);
    }

    [Fact]
    public void Sweep_schedule_retries_unresolved_sessions_a_limited_number_of_times_then_polls()
    {
        var schedule = new AudioSweepSchedule();

        for (var i = 0; i < AudioSweepSchedule.MaxRetries; i++)
        {
            Assert.Equal(AudioSweepSchedule.RetryDelay, schedule.NextDelay(unresolvedSession: true, anyMutedRoot: true));
        }

        Assert.Equal(AudioSweepSchedule.PollInterval, schedule.NextDelay(unresolvedSession: true, anyMutedRoot: true));
        Assert.Equal(AudioSweepSchedule.PollInterval, schedule.NextDelay(unresolvedSession: false, anyMutedRoot: true));
        Assert.Equal(AudioSweepSchedule.RetryDelay, schedule.NextDelay(unresolvedSession: true, anyMutedRoot: true));
    }

    [Fact]
    public void Sweep_schedule_is_idle_without_muted_roots_and_unresolved_sessions()
    {
        var schedule = new AudioSweepSchedule();

        Assert.Null(schedule.NextDelay(unresolvedSession: false, anyMutedRoot: false));
        Assert.Equal(AudioSweepSchedule.PollInterval, schedule.NextDelay(unresolvedSession: false, anyMutedRoot: true));
    }
}
