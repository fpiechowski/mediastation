using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Polityka wejścia (AD-8) na <see cref="FakeTimeProvider"/>: połknięcie i <c>kind</c> po VK, korelacja
/// <c>source</c> w oknie 50 ms, jedno naciśnięcie = jedna Action i reguła long-press OK w Shell.
/// </summary>
public class InputPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Ścieżka Raw Input dongla G20S Pro; config trzyma w niej sygnaturę <c>VID_1915&amp;PID_1025</c>.</summary>
    private const string DonglePath =
        @"\\?\HID#VID_1915&PID_1025&MI_01#7&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private static InputConfig Config(int longPressMs = 600) => new(
        [
            new InputDevice(
                "VID_1915&PID_1025",
                ActionSource.Remote,
                new Dictionary<string, ActionKind>
                {
                    ["0x25"] = ActionKind.Left,
                    ["0x26"] = ActionKind.Up,
                    ["0x27"] = ActionKind.Right,
                    ["0x28"] = ActionKind.Down,
                    ["0x0D"] = ActionKind.Ok,
                    ["0xA6"] = ActionKind.Back,
                    ["0xAC"] = ActionKind.Home,
                }),
        ],
        [GamepadButton.Back, GamepadButton.Start])
    {
        Timing = new InputTiming { LongPressMs = longPressMs },
    };

    private static (InputPolicy Policy, FakeTimeProvider Time) Create(
        InputMode mode = InputMode.Shell,
        int longPressMs = 600)
    {
        var time = new FakeTimeProvider(T0);
        return (new InputPolicy(Config(longPressMs), time, mode), time);
    }

    private static KeyStroke Down(int virtualKey, long timeMs, bool injected = false) =>
        new(virtualKey, KeyTransition.Down, injected, timeMs);

    private static KeyStroke Up(int virtualKey, long timeMs, bool injected = false) =>
        new(virtualKey, KeyTransition.Up, injected, timeMs);

    private static Action Press(ActionKind kind, ActionSource source, long tsMono) =>
        new(kind, ActionPhase.Press, source, tsMono);

    // ── Mapowanie i połknięcie ────────────────────────────────────────────────

    [Fact]
    public void Mapped_arrow_gives_one_press_on_key_down_and_is_swallowed()
    {
        var (policy, _) = Create();

        var down = policy.OnKey(Down(0x26, 1000));

        Assert.True(down.Swallow);
        Assert.Equal(Press(ActionKind.Up, ActionSource.Keyboard, 1000), Assert.Single(down.Actions));
        Assert.Null(policy.NextDeadline);

        var up = policy.OnKey(Up(0x26, 1080));
        Assert.True(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Mapped_ok_in_shell_is_swallowed_and_gives_press_on_key_up()
    {
        var (policy, _) = Create();

        var down = policy.OnKey(Down(0x0D, 1000));
        Assert.True(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.Equal(T0.AddMilliseconds(600), policy.NextDeadline);

        var up = policy.OnKey(Up(0x0D, 1200));
        Assert.True(up.Swallow);
        Assert.Equal(Press(ActionKind.Ok, ActionSource.Keyboard, 1200), Assert.Single(up.Actions));
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Autorepeat_is_swallowed_and_gives_no_action()
    {
        var (policy, _) = Create();

        var first = policy.OnKey(Down(0x25, 1000));
        var second = policy.OnKey(Down(0x25, 1030));

        Assert.Equal(Press(ActionKind.Left, ActionSource.Keyboard, 1000), Assert.Single(first.Actions));
        Assert.True(second.Swallow);
        Assert.Empty(second.Actions);
    }

    [Fact]
    public void Unknown_vk_is_not_swallowed_and_gives_no_action()
    {
        var (policy, _) = Create();

        var down = policy.OnKey(Down(0x71, 1000));
        var up = policy.OnKey(Up(0x71, 1050));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
    }

    // ── Long-press OK ─────────────────────────────────────────────────────────

    [Fact]
    public void Ok_in_shell_before_threshold_gives_press_only_on_key_up()
    {
        var (policy, time) = Create();

        Assert.True(policy.OnKey(Down(0x0D, 1000)).Swallow);

        // Press nie może zależeć od zegara TimeProvider: liczy się czas hooka.
        time.Advance(TimeSpan.FromMilliseconds(300));
        var up = policy.OnKey(Up(0x0D, 1300));

        Assert.True(up.Swallow);
        Assert.Equal(Press(ActionKind.Ok, ActionSource.Keyboard, 1300), Assert.Single(up.Actions));
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Ok_in_shell_after_threshold_gives_only_long_and_key_up_is_silent()
    {
        var (policy, time) = Create();

        policy.OnKey(Down(0x0D, 1000));
        time.Advance(TimeSpan.FromMilliseconds(600));

        var timer = policy.OnTimer(time.GetUtcNow());

        Assert.False(timer.Swallow);
        Assert.Equal(
            new Action(ActionKind.Ok, ActionPhase.Long, ActionSource.Keyboard, 1600),
            Assert.Single(timer.Actions));
        Assert.Null(policy.NextDeadline);

        var up = policy.OnKey(Up(0x0D, 1850));
        Assert.True(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Configured_long_press_threshold_drives_deadline_long_timestamp_and_press_window()
    {
        var (policy, time) = Create(longPressMs: 450);

        // Key-up przed progiem 450 ms daje jedno press, mimo że polityka nie używa wartości domyślnej.
        policy.OnKey(Down(0x0D, 1000));
        Assert.Equal(T0.AddMilliseconds(450), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(300));
        var up = policy.OnKey(Up(0x0D, 1300));
        Assert.Equal(Press(ActionKind.Ok, ActionSource.Keyboard, 1300), Assert.Single(up.Actions));
        Assert.Null(policy.NextDeadline);

        // Drugie naciśnięcie po progu 450 ms daje long z TsMono key-down + 450.
        policy.OnKey(Down(0x0D, 2000));
        Assert.Equal(T0.AddMilliseconds(750), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(450));
        var timer = policy.OnTimer(time.GetUtcNow());
        Assert.Equal(
            new Action(ActionKind.Ok, ActionPhase.Long, ActionSource.Keyboard, 2450),
            Assert.Single(timer.Actions));
    }

    [Fact]
    public void Key_up_press_uses_hook_time_not_the_provider_clock()
    {
        var (policy, time) = Create();

        policy.OnKey(Down(0x0D, 1000));

        // Zegar providera mija termin, ale OnTimer nie jest wołany; o press decyduje czas hooka.
        time.Advance(TimeSpan.FromMilliseconds(600));
        var up = policy.OnKey(Up(0x0D, 1400));

        Assert.True(up.Swallow);
        Assert.Equal(Press(ActionKind.Ok, ActionSource.Keyboard, 1400), Assert.Single(up.Actions));
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Ok_in_app_gives_press_on_key_down_and_arms_no_timer()
    {
        var (policy, _) = Create(InputMode.App);

        var down = policy.OnKey(Down(0x0D, 1000));

        Assert.True(down.Swallow);
        Assert.Equal(Press(ActionKind.Ok, ActionSource.Keyboard, 1000), Assert.Single(down.Actions));
        Assert.Null(policy.NextDeadline);

        var up = policy.OnKey(Up(0x0D, 1050));
        Assert.True(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Timer_before_deadline_keeps_waiting()
    {
        var (policy, time) = Create();

        policy.OnKey(Down(0x0D, 1000));
        time.Advance(TimeSpan.FromMilliseconds(599));

        var decision = policy.OnTimer(time.GetUtcNow());

        Assert.False(decision.Swallow);
        Assert.Empty(decision.Actions);
        Assert.Equal(T0.AddMilliseconds(600), policy.NextDeadline);
    }

    [Fact]
    public void Late_timer_after_key_up_gives_no_action()
    {
        var (policy, time) = Create();

        policy.OnKey(Down(0x0D, 1000));
        var up = policy.OnKey(Up(0x0D, 1100));
        Assert.Single(up.Actions);

        time.Advance(TimeSpan.FromMilliseconds(600));
        var late = policy.OnTimer(time.GetUtcNow());

        Assert.False(late.Swallow);
        Assert.Empty(late.Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Late_timer_after_long_gives_no_second_action()
    {
        var (policy, time) = Create();

        policy.OnKey(Down(0x0D, 1000));
        time.Advance(TimeSpan.FromMilliseconds(600));
        Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions);

        time.Advance(TimeSpan.FromMilliseconds(600));
        var late = policy.OnTimer(time.GetUtcNow());

        Assert.Empty(late.Actions);
        Assert.Null(policy.NextDeadline);
    }

    // ── Korelacja source ──────────────────────────────────────────────────────

    [Fact]
    public void Injected_home_after_consumer_report_30_ms_earlier_is_remote()
    {
        var (policy, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 970));
        var decision = policy.OnKey(Down(0xAC, 1000, injected: true));

        Assert.True(decision.Swallow);
        Assert.Equal(Press(ActionKind.Home, ActionSource.Remote, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Injected_home_without_consumer_report_or_raw_input_is_keyboard()
    {
        var (policy, _) = Create();

        var decision = policy.OnKey(Down(0xAC, 1000, injected: true));

        Assert.True(decision.Swallow);
        Assert.Equal(Press(ActionKind.Home, ActionSource.Keyboard, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Consumer_report_outside_the_window_is_ignored()
    {
        var (policy, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 949));

        Assert.Equal(
            Press(ActionKind.Home, ActionSource.Keyboard, 1000),
            Assert.Single(policy.OnKey(Down(0xAC, 1000, injected: true)).Actions));
    }

    [Fact]
    public void Consumer_report_after_the_key_is_ignored()
    {
        var (policy, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 1010));

        Assert.Equal(
            Press(ActionKind.Home, ActionSource.Keyboard, 1000),
            Assert.Single(policy.OnKey(Down(0xAC, 1000, injected: true)).Actions));
    }

    [Fact]
    public void Raw_keyboard_input_20_ms_earlier_gives_device_source_and_matches_path_case_insensitively()
    {
        var (policy, _) = Create();

        policy.OnRawKeyboard(new RawKeyboardInput(DonglePath.ToLowerInvariant(), 980));
        var decision = policy.OnKey(Down(0x26, 1000));

        Assert.Equal(Press(ActionKind.Up, ActionSource.Remote, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Raw_keyboard_input_from_a_device_outside_config_is_ignored()
    {
        var (policy, _) = Create();

        policy.OnRawKeyboard(new RawKeyboardInput(@"\\?\HID#VID_046D&PID_C52B#1", 980));

        Assert.Equal(
            Press(ActionKind.Up, ActionSource.Keyboard, 1000),
            Assert.Single(policy.OnKey(Down(0x26, 1000)).Actions));
    }

    [Fact]
    public void Mapping_is_merged_across_devices_and_source_comes_from_the_correlated_device()
    {
        var config = new InputConfig(
            [
                new InputDevice(
                    "VID_1915&PID_1025",
                    ActionSource.Remote,
                    new Dictionary<string, ActionKind> { ["0x0D"] = ActionKind.Ok }),
                new InputDevice(
                    "HID\\VID_046D&PID_C52B",
                    ActionSource.Keyboard,
                    new Dictionary<string, ActionKind> { ["0xA6"] = ActionKind.Back }),
            ],
            [GamepadButton.Back, GamepadButton.Start]);
        var policy = new InputPolicy(config, new FakeTimeProvider(T0));

        // VK pochodzi z drugiego urządzenia, a skorelowany WM_INPUT należy do pierwszego.
        policy.OnRawKeyboard(new RawKeyboardInput(DonglePath, 980));
        var decision = policy.OnKey(Down(0xA6, 1000));

        Assert.True(decision.Swallow);
        Assert.Equal(Press(ActionKind.Back, ActionSource.Remote, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Non_injected_key_ignores_a_fresh_consumer_report()
    {
        var (policy, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 990));

        var decision = policy.OnKey(Down(0x26, 1000));

        Assert.Equal(Press(ActionKind.Up, ActionSource.Keyboard, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Long_press_keeps_source_from_key_down()
    {
        var (policy, time) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 970));
        policy.OnKey(Down(0x0D, 1000, injected: true));
        time.Advance(TimeSpan.FromMilliseconds(600));

        var timer = policy.OnTimer(time.GetUtcNow());

        Assert.Equal(
            new Action(ActionKind.Ok, ActionPhase.Long, ActionSource.Remote, 1600),
            Assert.Single(timer.Actions));
    }
}
