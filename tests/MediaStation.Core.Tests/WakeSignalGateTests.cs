using MediaStation.App.Platform.System;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Reguła zwycięzcy wybudzenia: dwa źródła epiki i okno 5 s na zegarze monotonicznym.
/// </summary>
public class WakeSignalGateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(WakeSignalGate.ResumeAutomaticSource)]
    [InlineData(WakeSignalGate.DisplayStateSource)]
    public void First_candidate_of_a_winner_source_is_accepted(string source)
    {
        Assert.True(new WakeSignalGate(new FakeTimeProvider(T0)).TryAccept(source));
    }

    [Theory]
    [InlineData("PowerModeChanged")]
    [InlineData("pbt_apmresumeautomatic")]
    [InlineData("")]
    public void Source_outside_the_epic_rule_never_wins(string source)
    {
        Assert.False(new WakeSignalGate(new FakeTimeProvider(T0)).TryAccept(source));
    }

    [Fact]
    public void Candidate_before_five_seconds_is_rejected_and_at_five_seconds_is_accepted()
    {
        var time = new FakeTimeProvider(T0);
        var gate = new WakeSignalGate(time);

        Assert.True(gate.TryAccept(WakeSignalGate.ResumeAutomaticSource));
        time.Advance(TimeSpan.FromSeconds(4.999));
        Assert.False(gate.TryAccept(WakeSignalGate.ResumeAutomaticSource));
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(gate.TryAccept(WakeSignalGate.ResumeAutomaticSource));
    }

    [Fact]
    public void Window_anchor_is_the_last_accepted_signal_not_the_last_seen_one()
    {
        var time = new FakeTimeProvider(T0);
        var gate = new WakeSignalGate(time);

        Assert.True(gate.TryAccept(WakeSignalGate.ResumeAutomaticSource));
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(gate.TryAccept(WakeSignalGate.DisplayStateSource));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(gate.TryAccept(WakeSignalGate.ResumeAutomaticSource));
    }

    [Fact]
    public void Wall_clock_moving_backwards_does_not_move_the_window()
    {
        // Znacznik monotoniczny rośnie, a zegar ścienny skacze w obie strony: okno nadal liczy 5 s
        // od ostatniego zwycięzcy.
        var time = new SplitClockTimeProvider(T0);
        var gate = new WakeSignalGate(time);

        Assert.True(gate.TryAccept(WakeSignalGate.ResumeAutomaticSource));

        time.Advance(TimeSpan.FromSeconds(1));
        time.SetWallClock(T0.AddHours(-2));
        Assert.False(gate.TryAccept(WakeSignalGate.ResumeAutomaticSource));

        time.SetWallClock(T0.AddHours(2));
        Assert.False(gate.TryAccept(WakeSignalGate.ResumeAutomaticSource));

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.True(gate.TryAccept(WakeSignalGate.ResumeAutomaticSource));
    }

    /// <summary>Znacznik monotoniczny sterowany osobno od czasu ściennego (jak <c>Stopwatch</c> vs zegar systemowy).</summary>
    private sealed class SplitClockTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan delta)
        {
            // Domyślna częstotliwość TimeProvider to TimeSpan.TicksPerSecond.
            _timestamp += delta.Ticks;
            _utcNow = _utcNow.Add(delta);
        }

        public void SetWallClock(DateTimeOffset value) => _utcNow = value;
    }
}
