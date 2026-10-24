using MediaStation.App.Platform.Input;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Normalizacja 32-bitowego czasu wejścia: zawinięcie licznika daje rosnący <c>long</c>, a zwykły krok
/// wstecz zegara nie dolicza pełnego obiegu <c>2^32</c>. <c>time</c> hooka i <c>GetMessageTime</c> są w tej
/// samej jednostce, więc po normalizacji okno korelacji 50 ms nadal działa.
/// </summary>
public class InputClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Ścieżka Raw Input dongla; config trzyma w niej sygnaturę <c>VID_1915&amp;PID_1025</c>.</summary>
    private const string DonglePath =
        @"\\?\HID#VID_1915&PID_1025&MI_01#7&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void Wrap_of_the_32_bit_counter_yields_a_monotonic_long()
    {
        var clock = new InputClock();

        var before = clock.Normalize(0xFFFF_FFF0);
        var after = clock.Normalize(0x0000_0010);

        Assert.Equal(0xFFFF_FFF0L, before);
        Assert.Equal(0x1_0000_0010L, after);
    }

    [Fact]
    public void Small_step_back_does_not_add_a_full_wrap()
    {
        var clock = new InputClock();

        Assert.Equal(1000L, clock.Normalize(1000));
        Assert.Equal(999L, clock.Normalize(999));
    }

    [Fact]
    public void Second_wrap_keeps_increasing()
    {
        var clock = new InputClock();

        clock.Normalize(0xFFFF_FFF0);
        var firstWrap = clock.Normalize(0x0000_0010);
        var beforeSecondWrap = clock.Normalize(0xFFFF_FFF0);
        var secondWrap = clock.Normalize(0x0000_0010);

        Assert.True(firstWrap > 0);
        Assert.True(beforeSecondWrap > firstWrap);
        Assert.Equal(0x2_0000_0010L, secondWrap);
        Assert.True(secondWrap > beforeSecondWrap);
    }

    [Fact]
    public void Wrapped_hook_time_keeps_the_fifty_millisecond_correlation_window()
    {
        var clock = new InputClock();
        var time = new FakeTimeProvider(T0);
        var policy = new InputPolicy(
            new InputConfig(
                [new InputDevice(
                    "VID_1915&PID_1025",
                    ActionSource.Remote,
                    new Dictionary<string, ActionKind> { ["0xAC"] = ActionKind.Home })],
                [GamepadButton.Back, GamepadButton.Start]),
            time,
            new InputRoutingState());

        // Raport Consumer 32 ms przed klawiszem, po zawinięciu 32-bitowego licznika.
        var reportMs = clock.Normalize(0xFFFF_FFF0);
        var downMs = clock.Normalize(0x0000_0010);
        policy.OnHidConsumer(new HidConsumerReport(DonglePath, reportMs, 0x0C, 0x0223));
        var down = policy.OnKey(new KeyStroke(0xAC, KeyTransition.Down, Injected: true, downMs));

        Assert.True(down.Swallow);
        var action = Assert.Single(down.Actions);
        Assert.Equal(ActionKind.Home, action.Kind);
        Assert.Equal(ActionPhase.Press, action.Phase);
        Assert.Equal(ActionSource.Remote, action.Source);
        Assert.Equal(downMs, action.TsMono);

        var upMs = clock.Normalize(0x0000_0080);
        var up = policy.OnKey(new KeyStroke(0xAC, KeyTransition.Up, Injected: true, upMs));

        Assert.Empty(up.Actions);
        Assert.True(upMs > reportMs);
    }
}
