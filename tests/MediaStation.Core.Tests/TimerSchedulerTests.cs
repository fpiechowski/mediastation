using MediaStation.Core.Contracts;
using MediaStation.Core.Orchestrator;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Scheduler timerów: czas wyłącznie z <see cref="FakeTimeProvider"/>, jeden publikowany
/// <see cref="TimerElapsed"/> na termin i cisza po anulowaniu albo <c>Dispose</c>.
/// </summary>
public class TimerSchedulerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Elapsed_is_published_once_only_after_due()
    {
        var time = new FakeTimeProvider(T0);
        var posted = new List<IOrchestratorMessage>();
        using var scheduler = new TimerScheduler(time, posted.Add);

        scheduler.Execute(new ScheduleTimer(1, 42, "netflix", T0.AddSeconds(30)));

        time.Advance(TimeSpan.FromSeconds(29.999));
        Assert.Empty(posted);

        time.Advance(TimeSpan.FromMilliseconds(1));
        var elapsed = Assert.IsType<TimerElapsed>(Assert.Single(posted));
        Assert.Equal(42, elapsed.TimerId);

        // Timer jest jednorazowy: dalszy zegar nie publikuje drugiego zdarzenia.
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(posted);
    }

    [Fact]
    public void Cancel_before_due_suppresses_publication()
    {
        var time = new FakeTimeProvider(T0);
        var posted = new List<IOrchestratorMessage>();
        using var scheduler = new TimerScheduler(time, posted.Add);

        scheduler.Execute(new ScheduleTimer(1, 7, "netflix", T0.AddSeconds(5)));
        scheduler.Execute(new CancelTimer(2, 7));

        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Empty(posted);
    }

    [Fact]
    public void Cancel_of_unknown_timer_is_a_noop_and_keeps_other_terms()
    {
        var time = new FakeTimeProvider(T0);
        var posted = new List<IOrchestratorMessage>();
        using var scheduler = new TimerScheduler(time, posted.Add);

        scheduler.Execute(new ScheduleTimer(1, 7, "netflix", T0.AddSeconds(10)));
        scheduler.Execute(new CancelTimer(2, 999));

        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(7, Assert.IsType<TimerElapsed>(Assert.Single(posted)).TimerId);
    }

    [Fact]
    public void Dispose_suppresses_publication_and_ignores_later_schedules()
    {
        var time = new FakeTimeProvider(T0);
        var posted = new List<IOrchestratorMessage>();
        var scheduler = new TimerScheduler(time, posted.Add);

        scheduler.Execute(new ScheduleTimer(1, 7, "netflix", T0.AddSeconds(10)));
        scheduler.Dispose();
        scheduler.Execute(new ScheduleTimer(2, 8, "netflix", T0.AddSeconds(20)));
        scheduler.Dispose();

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Empty(posted);
    }

    [Fact]
    public void Create_timer_failure_does_not_escape_execute_and_never_publishes()
    {
        var time = new ThrowingCreateTimerTimeProvider(T0);
        var posted = new List<IOrchestratorMessage>();
        using var scheduler = new TimerScheduler(time, posted.Add);

        // TimeProvider.System odrzuca termin dalszy niż ~49,7 dnia: port nie może przepuścić wyjątku (AD-4),
        // a odrzucony termin nie może zostać w mapie jako wiecznie oczekujący wpis.
        var exception = Record.Exception(() =>
            scheduler.Execute(new ScheduleTimer(1, 7, "netflix", T0.AddDays(365))));

        Assert.Null(exception);
        time.Advance(TimeSpan.FromDays(365));
        Assert.Empty(posted);
    }

    [Fact]
    public void Duplicate_timer_id_replaces_the_previous_term()
    {
        var time = new FakeTimeProvider(T0);
        var posted = new List<IOrchestratorMessage>();
        using var scheduler = new TimerScheduler(time, posted.Add);

        scheduler.Execute(new ScheduleTimer(1, 7, "netflix", T0.AddSeconds(10)));
        scheduler.Execute(new ScheduleTimer(2, 7, "plex", T0.AddSeconds(20)));

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(posted);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(7, Assert.IsType<TimerElapsed>(Assert.Single(posted)).TimerId);
    }

    /// <summary>Provider, który zawsze odrzuca termin — jak <c>TimeProvider.System</c> dla zbyt dalekiej daty.</summary>
    private sealed class ThrowingCreateTimerTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new ArgumentOutOfRangeException(nameof(dueTime));

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
