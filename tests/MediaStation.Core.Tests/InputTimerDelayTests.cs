using MediaStation.App.Platform.Input;

namespace MediaStation.Core.Tests;

/// <summary>
/// Termin long-press → opóźnienie <c>SetTimer</c>: brak terminu zabija timer, termin miniony odpala po 1 ms,
/// ułamkowe milisekundy są zaokrąglane w górę, a ogromny termin jest ograniczony do <see cref="int.MaxValue"/>.
/// </summary>
public class InputTimerDelayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_deadline_kills_the_timer()
    {
        Assert.Null(InputTimerDelay.Compute(null, Now));
    }

    [Fact]
    public void Past_or_current_deadline_fires_at_one_millisecond()
    {
        Assert.Equal((uint?)1, InputTimerDelay.Compute(Now.AddMilliseconds(-500), Now));
        Assert.Equal((uint?)1, InputTimerDelay.Compute(Now, Now));
    }

    [Fact]
    public void Fractional_milliseconds_are_rounded_up()
    {
        // 0.5 ms i 599.2 ms: AddTicks, bo AddMilliseconds zaokrągla do pełnej milisekundy.
        Assert.Equal((uint?)1, InputTimerDelay.Compute(Now.AddTicks(TimeSpan.TicksPerMillisecond / 2), Now));
        Assert.Equal((uint?)600, InputTimerDelay.Compute(Now.AddTicks(5_992_000), Now));
    }

    [Fact]
    public void Whole_milliseconds_are_preserved()
    {
        Assert.Equal((uint?)600, InputTimerDelay.Compute(Now.AddMilliseconds(600), Now));
    }

    [Fact]
    public void Huge_deadline_is_clamped_to_int_max()
    {
        Assert.Equal((uint?)int.MaxValue, InputTimerDelay.Compute(Now.AddDays(60), Now));
    }
}
