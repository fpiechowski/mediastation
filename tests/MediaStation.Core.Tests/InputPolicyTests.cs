using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Polityka wejścia (AD-8) na <see cref="FakeTimeProvider"/>: połknięcie i <c>kind</c> po VK, korelacja
/// <c>source</c> w oknie 50 ms, jedno naciśnięcie = jedna Action, reguła long-press OK w Shell, repeat trzymanej
/// strzałki (z liveness guardem) i fallback dopasowania urządzenia przez sygnaturę <c>*</c>.
/// </summary>
public class InputPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Ścieżka Raw Input dongla G20S Pro; config trzyma w niej sygnaturę <c>VID_1915&amp;PID_1025</c>.</summary>
    private const string DonglePath =
        @"\\?\HID#VID_1915&PID_1025&MI_01#7&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const int RemoteHomeUsage = 0x0223;

    private const int RemoteBackUsage = 0x0224;

    private static InputDevice RemoteDevice() => new(
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
        });

    /// <summary>Urządzenie zastępcze <c>*</c>: każda klawiatura bez własnego wpisu (source: keyboard).</summary>
    private static InputDevice WildcardDevice() => new(
        InputDevice.WildcardSignature,
        ActionSource.Keyboard,
        new Dictionary<string, ActionKind>
        {
            ["0x0D"] = ActionKind.Ok,
            ["0x1B"] = ActionKind.Back,
            ["0x24"] = ActionKind.Home,
            ["0x25"] = ActionKind.Left,
            ["0x26"] = ActionKind.Up,
            ["0x27"] = ActionKind.Right,
            ["0x28"] = ActionKind.Down,
            ["0x71"] = ActionKind.Keyboard,
        });

    private static InputConfig Config(
        int longPressMs = 600,
        int repeatDelayMs = 400,
        int repeatIntervalMs = 120) => new(
        [RemoteDevice()],
        [GamepadButton.Back, GamepadButton.Start])
    {
        Timing = new InputTiming
        {
            LongPressMs = longPressMs,
            RepeatDelayMs = repeatDelayMs,
            RepeatIntervalMs = repeatIntervalMs,
        },
    };

    private static InputConfig WildcardConfig(bool wildcardFirst = true) => new(
        wildcardFirst ? [WildcardDevice(), RemoteDevice()] : [RemoteDevice(), WildcardDevice()],
        [GamepadButton.Back, GamepadButton.Start]);

    /// <summary>Polityka z mapowaniem <c>*</c>, żeby wiersze routingu mogły użyć także klawisza Keyboard.</summary>
    private static (InputPolicy Policy, InputRoutingState Routing) RoutedWildcard(
        InputMode mode,
        AppType? appType)
    {
        var routing = new InputRoutingState();
        routing.Apply(Route(1, mode, appType));
        return (new InputPolicy(WildcardConfig(), new FakeTimeProvider(T0), routing), routing);
    }

    private static SetInputRouting Route(long epoch, InputMode mode, AppType? appType = null) =>
        new(epoch, mode, appType, PointerPolicy.Blocked, epoch);

    /// <summary>
    /// Polityka z lokalną kopią routingu: <paramref name="mode"/> i <paramref name="appType"/> to wiersz tabeli
    /// AD-8 zapisany jako <c>epoch</c> 1. Kolejne wiersze testy zapisują przez <see cref="InputRoutingState.Apply"/>.
    /// </summary>
    private static (InputPolicy Policy, FakeTimeProvider Time, InputRoutingState Routing) Create(
        InputMode mode = InputMode.Shell,
        AppType? appType = null,
        int longPressMs = 600,
        int repeatDelayMs = 400,
        int repeatIntervalMs = 120)
    {
        var time = new FakeTimeProvider(T0);
        var routing = new InputRoutingState();
        routing.Apply(Route(1, mode, appType));
        return (new InputPolicy(Config(longPressMs, repeatDelayMs, repeatIntervalMs), time, routing), time, routing);
    }

    private static KeyStroke Down(int virtualKey, long timeMs, bool injected = false, long extraInfo = 0) =>
        new(virtualKey, KeyTransition.Down, injected, timeMs, ExtraInfo: extraInfo);

    private static KeyStroke Up(int virtualKey, long timeMs, bool injected = false, long extraInfo = 0) =>
        new(virtualKey, KeyTransition.Up, injected, timeMs, ExtraInfo: extraInfo);

    private static Action Press(ActionKind kind, ActionSource source, long tsMono) =>
        new(kind, ActionPhase.Press, source, tsMono);

    private static Action Repeat(ActionKind kind, ActionSource source, long tsMono) =>
        new(kind, ActionPhase.Repeat, source, tsMono);

    // ── Mapowanie i połknięcie ────────────────────────────────────────────────

    [Fact]
    public void Mapped_arrow_gives_one_press_on_key_down_and_is_swallowed()
    {
        var (policy, _, _) = Create();

        var down = policy.OnKey(Down(0x26, 1000));

        Assert.True(down.Swallow);
        Assert.Equal(Press(ActionKind.Up, ActionSource.Keyboard, 1000), Assert.Single(down.Actions));
        Assert.Equal(T0.AddMilliseconds(400), policy.NextDeadline);

        var up = policy.OnKey(Up(0x26, 1080));
        Assert.True(up.Swallow);
        Assert.Empty(up.Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Mapped_ok_in_shell_is_swallowed_and_gives_press_on_key_up()
    {
        var (policy, _, _) = Create();

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
        var (policy, _, _) = Create();

        var first = policy.OnKey(Down(0x25, 1000));
        var second = policy.OnKey(Down(0x25, 1030));

        Assert.Equal(Press(ActionKind.Left, ActionSource.Keyboard, 1000), Assert.Single(first.Actions));
        Assert.True(second.Swallow);
        Assert.Empty(second.Actions);
    }

    [Fact]
    public void Unknown_vk_is_not_swallowed_and_gives_no_action()
    {
        var (policy, _, _) = Create();

        var down = policy.OnKey(Down(0x71, 1000));
        var up = policy.OnKey(Up(0x71, 1050));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
    }

    // ── Routing z lokalnej kopii (AD-8) ───────────────────────────────────────

    [Fact]
    public void Osk_ok_past_the_threshold_gives_only_long_and_no_press()
    {
        var (policy, time, _) = Create(InputMode.Osk);

        var down = policy.OnKey(Down(0x0D, 1000));
        Assert.True(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.Equal(T0.AddMilliseconds(600), policy.NextDeadline);

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
    public void Osk_ok_before_the_threshold_gives_press_on_key_up()
    {
        var (policy, _, _) = Create(InputMode.Osk);

        Assert.True(policy.OnKey(Down(0x0D, 1000)).Swallow);
        var up = policy.OnKey(Up(0x0D, 1200));

        Assert.True(up.Swallow);
        Assert.Equal(Press(ActionKind.Ok, ActionSource.Keyboard, 1200), Assert.Single(up.Actions));
    }

    [Theory]
    [InlineData(AppType.Moonlight, 0x25)]
    [InlineData(AppType.Moonlight, 0x0D)]
    [InlineData(AppType.Native, 0x28)]
    [InlineData(AppType.Native, 0x0D)]
    public void App_with_moonlight_or_native_passes_dpad_and_ok_without_actions_or_grid(
        AppType appType,
        int virtualKey)
    {
        var (policy, time, _) = Create(InputMode.App, appType);

        var down = policy.OnKey(Down(virtualKey, 1000));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.Null(policy.NextDeadline);

        // Minięte okno repeat nie produkuje nic: siatka nie jest uzbrojona dla przepuszczonego klawisza.
        time.Advance(TimeSpan.FromMilliseconds(640));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
        Assert.Null(policy.NextDeadline);

        var up = policy.OnKey(Up(virtualKey, 1700));
        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Theory]
    [InlineData(AppType.Moonlight, 0xA6, ActionKind.Back)]
    [InlineData(AppType.Moonlight, 0xAC, ActionKind.Home)]
    [InlineData(AppType.Moonlight, 0x71, ActionKind.Keyboard)]
    [InlineData(AppType.Native, 0xA6, ActionKind.Back)]
    [InlineData(AppType.Native, 0xAC, ActionKind.Home)]
    [InlineData(AppType.Native, 0x71, ActionKind.Keyboard)]
    public void App_with_moonlight_or_native_swallows_back_home_and_keyboard(
        AppType appType,
        int virtualKey,
        ActionKind kind)
    {
        var (policy, _) = RoutedWildcard(InputMode.App, appType);

        var down = policy.OnKey(Down(virtualKey, 1000));

        Assert.True(down.Swallow);
        Assert.Equal(Press(kind, ActionSource.Keyboard, 1000), Assert.Single(down.Actions));
    }

    [Theory]
    [InlineData(InputMode.Shell, null)]
    [InlineData(InputMode.Osk, null)]
    [InlineData(InputMode.App, AppType.Web)]
    [InlineData(InputMode.App, AppType.Moonlight)]
    [InlineData(InputMode.App, AppType.Native)]
    [InlineData(InputMode.App, null)]
    public void Home_is_swallowed_with_action_in_every_row(InputMode mode, AppType? appType)
    {
        var (policy, _, _) = Create(mode, appType);

        var down = policy.OnKey(Down(0xAC, 1000));

        Assert.True(down.Swallow);
        Assert.Equal(Press(ActionKind.Home, ActionSource.Keyboard, 1000), Assert.Single(down.Actions));
    }

    [Theory]
    [InlineData(0x25)]
    [InlineData(0x0D)]
    public void App_without_app_type_swallows_dpad_and_ok(int virtualKey)
    {
        var (policy, _, _) = Create(InputMode.App);

        var down = policy.OnKey(Down(virtualKey, 1000));

        Assert.True(down.Swallow);
        Assert.Single(down.Actions);
    }

    /// <summary>
    /// Wiersze połykające (<c>shell</c>, <c>osk</c> i <c>app</c> + <c>web</c>): D-pad, OK, Back, Home i Keyboard
    /// dają dokładnie jedną Action <c>press</c> (OK w <c>shell</c> i <c>osk</c> na key-up, reszta na key-down),
    /// a klawisz jest połknięty na key-down i na key-up.
    /// </summary>
    [Theory]
    [InlineData(InputMode.Shell, null, 0x26, ActionKind.Up)]
    [InlineData(InputMode.Shell, null, 0x0D, ActionKind.Ok)]
    [InlineData(InputMode.Shell, null, 0x1B, ActionKind.Back)]
    [InlineData(InputMode.Shell, null, 0x24, ActionKind.Home)]
    [InlineData(InputMode.Shell, null, 0x71, ActionKind.Keyboard)]
    [InlineData(InputMode.Osk, null, 0x26, ActionKind.Up)]
    [InlineData(InputMode.Osk, null, 0x0D, ActionKind.Ok)]
    [InlineData(InputMode.Osk, null, 0x1B, ActionKind.Back)]
    [InlineData(InputMode.Osk, null, 0x24, ActionKind.Home)]
    [InlineData(InputMode.Osk, null, 0x71, ActionKind.Keyboard)]
    [InlineData(InputMode.App, AppType.Web, 0x26, ActionKind.Up)]
    [InlineData(InputMode.App, AppType.Web, 0x0D, ActionKind.Ok)]
    [InlineData(InputMode.App, AppType.Web, 0x1B, ActionKind.Back)]
    [InlineData(InputMode.App, AppType.Web, 0x24, ActionKind.Home)]
    [InlineData(InputMode.App, AppType.Web, 0x71, ActionKind.Keyboard)]
    public void Swallowing_row_gives_exactly_one_press_action_per_key_press(
        InputMode mode,
        AppType? appType,
        int virtualKey,
        ActionKind kind)
    {
        var (policy, _) = RoutedWildcard(mode, appType);

        var down = policy.OnKey(Down(virtualKey, 1000));
        var up = policy.OnKey(Up(virtualKey, 1100));

        Assert.True(down.Swallow);
        Assert.True(up.Swallow);
        var action = Assert.Single(down.Actions.Concat(up.Actions).ToArray());
        Assert.Equal(kind, action.Kind);
        Assert.Equal(ActionPhase.Press, action.Phase);
    }

    [Fact]
    public void Rejected_stale_epoch_keeps_the_old_routing_row_for_the_policy()
    {
        var (policy, _, routing) = Create(InputMode.App, AppType.Moonlight);

        // Port odrzuca `epoch` równy i starszy, więc kopia zostaje przy wierszu `app` + `moonlight`.
        Assert.False(routing.Apply(Route(1, InputMode.Shell)));
        Assert.False(routing.Apply(Route(0, InputMode.Shell)));
        Assert.Equal(new InputRouting(InputMode.App, AppType.Moonlight, PointerPolicy.Blocked, 1), routing.Current);

        Assert.False(policy.OnKey(Down(0x26, 1000)).Swallow);
    }

    [Fact]
    public void Course_change_during_hold_keeps_key_up_and_autorepeat_swallowed_and_the_repeat_grid_alive()
    {
        var (policy, time, routing) = Create();

        Assert.Equal(Press(ActionKind.Up, ActionSource.Keyboard, 1000), Assert.Single(policy.OnKey(Down(0x26, 1000)).Actions));
        Assert.True(routing.Apply(Route(2, InputMode.App, AppType.Moonlight)));

        var autorepeat = policy.OnKey(Down(0x26, 1100));
        Assert.True(autorepeat.Swallow);
        Assert.Empty(autorepeat.Actions);
        Assert.Equal(T0.AddMilliseconds(400), policy.NextDeadline);

        // Siatka repeat uzbrojona przy key-down działa dalej mimo nowego wiersza.
        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(
            Repeat(ActionKind.Up, ActionSource.Keyboard, 1400),
            Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions));

        var up = policy.OnKey(Up(0x26, 1600));
        Assert.True(up.Swallow);
        Assert.Empty(up.Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Passed_key_stays_passed_after_the_row_changes_to_shell()
    {
        var (policy, _, routing) = Create(InputMode.App, AppType.Moonlight);

        var down = policy.OnKey(Down(0x26, 1000));
        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.Null(policy.NextDeadline);

        Assert.True(routing.Apply(Route(2, InputMode.Shell)));

        var autorepeat = policy.OnKey(Down(0x26, 1100));
        Assert.False(autorepeat.Swallow);
        Assert.Empty(autorepeat.Actions);

        var up = policy.OnKey(Up(0x26, 1200));
        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Passed_key_with_missing_key_up_is_stale_after_max_hold_and_swallows_again()
    {
        var (policy, _, routing) = Create(InputMode.App, AppType.Moonlight);

        // Key-up przepuszczonego klawisza ginie (secure desktop, UAC, odpięta klawiatura, usunięty hook).
        var down = policy.OnKey(Down(0x26, 1000));
        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);

        // Do granicy MaxRepeatHoldMs wpis jest jeszcze świeży: ten sam VK dalej przechodzi bez Actions.
        var beforeBound = policy.OnKey(Down(0x26, 1000 + InputPolicy.MaxRepeatHoldMs));
        Assert.False(beforeBound.Swallow);
        Assert.Empty(beforeBound.Actions);

        // Po granicy wpis jest nieświeży: key-down zaczyna nowe trzymanie według bieżącego wiersza (tutaj `shell`).
        Assert.True(routing.Apply(Route(2, InputMode.Shell)));
        var afterBound = policy.OnKey(Down(0x26, 1000 + InputPolicy.MaxRepeatHoldMs + 1));

        Assert.True(afterBound.Swallow);
        Assert.Equal(
            Press(ActionKind.Up, ActionSource.Keyboard, 1000 + InputPolicy.MaxRepeatHoldMs + 1),
            Assert.Single(afterBound.Actions));
        Assert.Equal(T0.AddMilliseconds(400), policy.NextDeadline);
    }

    [Fact]
    public void Swallowed_home_with_missing_key_up_is_stale_after_max_hold_and_acts_again()
    {
        var (policy, _, _) = Create(InputMode.Shell);

        // Key-up połkniętego Home ginie (secure desktop, zablokowana stacja): bez wygaszania Home byłby martwy.
        var first = policy.OnKey(Down(0xAC, 1000));
        Assert.True(first.Swallow);
        Assert.Single(first.Actions);

        // Do granicy MaxRepeatHoldMs wpis jest świeży: to autorepeat, połykany bez Actions.
        var beforeBound = policy.OnKey(Down(0xAC, 1000 + InputPolicy.MaxRepeatHoldMs));
        Assert.True(beforeBound.Swallow);
        Assert.Empty(beforeBound.Actions);

        // Po granicy key-down zaczyna nowe naciśnięcie i daje Action.
        var afterBound = policy.OnKey(Down(0xAC, 1000 + InputPolicy.MaxRepeatHoldMs + 1));
        Assert.True(afterBound.Swallow);
        Assert.Equal(
            Press(ActionKind.Home, ActionSource.Keyboard, 1000 + InputPolicy.MaxRepeatHoldMs + 1),
            Assert.Single(afterBound.Actions));
    }

    [Fact]
    public void Key_up_without_key_down_follows_the_current_row()
    {
        var (policy, _, routing) = Create(InputMode.App, AppType.Moonlight);

        Assert.False(policy.OnKey(Up(0x26, 1000)).Swallow);

        Assert.True(routing.Apply(Route(2, InputMode.Shell)));
        Assert.True(policy.OnKey(Up(0x27, 1100)).Swallow);
    }

    [Theory]
    [InlineData(InputMode.Shell, null)]
    [InlineData(InputMode.Osk, null)]
    [InlineData(InputMode.App, AppType.Web)]
    [InlineData(InputMode.App, AppType.Moonlight)]
    public void Unmapped_key_passes_through_without_actions_in_every_row(InputMode mode, AppType? appType)
    {
        var (policy, _, _) = Create(mode, appType);

        var down = policy.OnKey(Down(0x70, 1000));
        var up = policy.OnKey(Up(0x70, 1050));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
    }

    // ── Long-press OK ─────────────────────────────────────────────────────────

    [Fact]
    public void Ok_in_shell_before_threshold_gives_press_only_on_key_up()
    {
        var (policy, time, _) = Create();

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
        var (policy, time, _) = Create();

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
        var (policy, time, _) = Create(longPressMs: 450);

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
        var (policy, time, _) = Create();

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
        var (policy, _, _) = Create(InputMode.App, AppType.Web);

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
        var (policy, time, _) = Create();

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
        var (policy, time, _) = Create();

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
        var (policy, time, _) = Create();

        policy.OnKey(Down(0x0D, 1000));
        time.Advance(TimeSpan.FromMilliseconds(600));
        Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions);

        time.Advance(TimeSpan.FromMilliseconds(600));
        var late = policy.OnTimer(time.GetUtcNow());

        Assert.Empty(late.Actions);
        Assert.Null(policy.NextDeadline);
    }

    // ── Repeat trzymanej strzałki ─────────────────────────────────────────────

    [Fact]
    public void Held_arrow_repeats_after_delay_and_interval_until_key_up()
    {
        var (policy, time, _) = Create();

        Assert.Equal(Press(ActionKind.Up, ActionSource.Keyboard, 1000), Assert.Single(policy.OnKey(Down(0x26, 1000)).Actions));
        Assert.Equal(T0.AddMilliseconds(400), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(399));
        var early = policy.OnTimer(time.GetUtcNow());
        Assert.False(early.Swallow);
        Assert.Empty(early.Actions);
        Assert.Equal(T0.AddMilliseconds(400), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(1));
        var first = policy.OnTimer(time.GetUtcNow());
        Assert.False(first.Swallow);
        Assert.Equal(Repeat(ActionKind.Up, ActionSource.Keyboard, 1400), Assert.Single(first.Actions));
        Assert.Equal(T0.AddMilliseconds(520), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(120));
        var second = policy.OnTimer(time.GetUtcNow());
        Assert.Equal(Repeat(ActionKind.Up, ActionSource.Keyboard, 1520), Assert.Single(second.Actions));
        Assert.Equal(T0.AddMilliseconds(640), policy.NextDeadline);

        var up = policy.OnKey(Up(0x26, 1650));
        Assert.True(up.Swallow);
        Assert.Empty(up.Actions);
        Assert.Null(policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(120));
        var late = policy.OnTimer(time.GetUtcNow());
        Assert.False(late.Swallow);
        Assert.Empty(late.Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Repeat_thresholds_come_from_config()
    {
        var (policy, time, _) = Create(repeatDelayMs: 300, repeatIntervalMs: 80);

        policy.OnKey(Down(0x28, 1000));
        Assert.Equal(T0.AddMilliseconds(300), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(Repeat(ActionKind.Down, ActionSource.Keyboard, 1300), Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions));
        Assert.Equal(T0.AddMilliseconds(380), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(80));
        Assert.Equal(Repeat(ActionKind.Down, ActionSource.Keyboard, 1380), Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions));
    }

    [Fact]
    public void Late_timer_gives_one_repeat_and_snaps_the_grid_to_the_latest_deadline()
    {
        var (policy, time, _) = Create();

        policy.OnKey(Down(0x26, 1000));

        // 250 ms po pierwszym terminie 400: minęły trzy terminy siatki (400, 520 i 640), więc tsMono 1640
        // to trzeci repeat (1000 + 400 + 2 * 120), a siatka staje na 760.
        time.Advance(TimeSpan.FromMilliseconds(650));
        var timer = policy.OnTimer(time.GetUtcNow());

        Assert.False(timer.Swallow);
        Assert.Equal(Repeat(ActionKind.Up, ActionSource.Keyboard, 1640), Assert.Single(timer.Actions));
        Assert.Equal(T0.AddMilliseconds(760), policy.NextDeadline);
    }

    [Fact]
    public void Two_due_keys_repeat_in_deadline_order()
    {
        var (policy, time, _) = Create();

        policy.OnKey(Down(0x26, 1000));
        time.Advance(TimeSpan.FromMilliseconds(50));
        policy.OnKey(Down(0x25, 1050));

        // Terminy 400 (Up) i 450 (Left) minęły w jednym OnTimer; kolejność wynika z terminów, nie z VK.
        time.Advance(TimeSpan.FromMilliseconds(400));
        var timer = policy.OnTimer(time.GetUtcNow());

        Assert.False(timer.Swallow);
        Assert.Collection(
            timer.Actions,
            action => Assert.Equal(Repeat(ActionKind.Up, ActionSource.Keyboard, 1400), action),
            action => Assert.Equal(Repeat(ActionKind.Left, ActionSource.Keyboard, 1450), action));
        Assert.Equal(T0.AddMilliseconds(520), policy.NextDeadline);
    }

    [Fact]
    public void Autorepeat_does_not_shift_the_repeat_grid()
    {
        var (policy, time, _) = Create();

        policy.OnKey(Down(0x25, 1000));
        time.Advance(TimeSpan.FromMilliseconds(200));

        var autorepeat = policy.OnKey(Down(0x25, 1200));
        Assert.True(autorepeat.Swallow);
        Assert.Empty(autorepeat.Actions);
        Assert.Equal(T0.AddMilliseconds(400), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(
            Repeat(ActionKind.Left, ActionSource.Keyboard, 1400),
            Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions));
    }

    [Theory]
    [InlineData(0xA6, ActionKind.Back)]
    [InlineData(0xAC, ActionKind.Home)]
    public void Held_non_arrow_key_never_repeats(int virtualKey, ActionKind kind)
    {
        var (policy, time, _) = Create();

        Assert.Equal(Press(kind, ActionSource.Keyboard, 1000), Assert.Single(policy.OnKey(Down(virtualKey, 1000)).Actions));
        Assert.Null(policy.NextDeadline);

        // repeatDelayMs + dwa interwały: gdyby siatka była uzbrojona, byłyby tu dwa repeat.
        time.Advance(TimeSpan.FromMilliseconds(640));
        var timer = policy.OnTimer(time.GetUtcNow());

        Assert.False(timer.Swallow);
        Assert.Empty(timer.Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Held_ok_in_shell_keeps_long_and_gets_no_repeat()
    {
        var (policy, time, _) = Create();

        Assert.True(policy.OnKey(Down(0x0D, 1000)).Swallow);

        time.Advance(TimeSpan.FromMilliseconds(600));
        var longDecision = policy.OnTimer(time.GetUtcNow());
        Assert.Equal(
            new Action(ActionKind.Ok, ActionPhase.Long, ActionSource.Keyboard, 1600),
            Assert.Single(longDecision.Actions));

        time.Advance(TimeSpan.FromMilliseconds(160));
        var timer = policy.OnTimer(time.GetUtcNow());

        Assert.Empty(timer.Actions);
        Assert.Null(policy.NextDeadline);

        var up = policy.OnKey(Up(0x0D, 1800));
        Assert.True(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Held_keyboard_key_never_repeats()
    {
        var time = new FakeTimeProvider(T0);
        var policy = new InputPolicy(WildcardConfig(), time, new InputRoutingState());

        var down = policy.OnKey(Down(0x71, 1000));
        Assert.True(down.Swallow);
        Assert.Equal(Press(ActionKind.Keyboard, ActionSource.Keyboard, 1000), Assert.Single(down.Actions));
        Assert.Null(policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(640));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Theory]
    [InlineData(0, 120)]
    [InlineData(400, 0)]
    [InlineData(-1, 120)]
    [InlineData(400, -1)]
    public void Non_positive_repeat_thresholds_arm_no_grid(int delay, int interval)
    {
        var (policy, time, _) = Create(repeatDelayMs: delay, repeatIntervalMs: interval);

        policy.OnKey(Down(0x26, 1000));
        Assert.Null(policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(640));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Missing_key_up_stops_repeats_after_max_hold_and_keeps_the_key_swallowed()
    {
        var (policy, time, _) = Create();

        Assert.True(policy.OnKey(Down(0x26, 1000)).Swallow);
        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(Repeat(ActionKind.Up, ActionSource.Keyboard, 1400), Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions));

        // Key-up nie dociera (blokada pulpitu, UAC, odpięta klawiatura): guard kończy siatkę bez emisji.
        time.Advance(TimeSpan.FromMilliseconds(InputPolicy.MaxRepeatHoldMs));
        var guard = policy.OnTimer(time.GetUtcNow());

        Assert.False(guard.Swallow);
        Assert.Empty(guard.Actions);
        Assert.Null(policy.NextDeadline);

        // Klawisz zostaje połknięty do key-up, jak przed tą story.
        var autorepeat = policy.OnKey(Down(0x26, 70000));
        Assert.True(autorepeat.Swallow);
        Assert.Empty(autorepeat.Actions);
        Assert.Null(policy.NextDeadline);

        var up = policy.OnKey(Up(0x26, 80000));
        Assert.True(up.Swallow);
        Assert.Empty(up.Actions);
        Assert.Null(policy.NextDeadline);
    }

    // ── Korelacja source ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(RemoteHomeUsage, 0xAC, ActionKind.Home)]
    [InlineData(RemoteBackUsage, 0xA6, ActionKind.Back)]
    public void Exact_remote_consumer_and_browser_vk_pair_is_remote(int usage, int vk, ActionKind kind)
    {
        var (policy, _, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 950, 0x0C, usage));
        var decision = policy.OnKey(Down(vk, 1000, injected: true));

        Assert.True(decision.Swallow);
        Assert.Equal(Press(kind, ActionSource.Remote, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Injected_home_without_consumer_report_or_raw_input_is_keyboard()
    {
        var (policy, _, _) = Create();

        var decision = policy.OnKey(Down(0xAC, 1000, injected: true));

        Assert.True(decision.Swallow);
        Assert.Equal(Press(ActionKind.Home, ActionSource.Keyboard, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Consumer_report_outside_the_window_is_ignored()
    {
        var (policy, _, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 949, 0x0C, RemoteHomeUsage));

        Assert.Equal(
            Press(ActionKind.Home, ActionSource.Keyboard, 1000),
            Assert.Single(policy.OnKey(Down(0xAC, 1000, injected: true)).Actions));

        policy.OnKey(Up(0xAC, 1005, injected: true));

        Assert.Equal(
            Press(ActionKind.Home, ActionSource.Keyboard, 1020),
            Assert.Single(policy.OnKey(Down(0xAC, 1020, injected: true)).Actions));
    }

    [Fact]
    public void Consumer_report_after_the_key_is_ignored()
    {
        var (policy, _, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 1010, 0x0C, RemoteHomeUsage));

        Assert.Equal(
            Press(ActionKind.Home, ActionSource.Keyboard, 1000),
            Assert.Single(policy.OnKey(Down(0xAC, 1000, injected: true)).Actions));
    }

    [Fact]
    public void Raw_keyboard_input_from_remote_row_does_not_change_a_mapped_key_source()
    {
        var (policy, _, _) = Create();

        policy.OnRawKeyboard(new RawKeyboardInput(DonglePath.ToLowerInvariant(), 980, 0x26, 0x48, Down: true));
        var decision = policy.OnKey(Down(0x26, 1000));

        Assert.Equal(Press(ActionKind.Up, ActionSource.Keyboard, 1000), Assert.Single(decision.Actions));
    }

    [Theory]
    [InlineData(@"\\?\HID#VID_046D&PID_C52B#1", 0x0C, RemoteHomeUsage)]
    [InlineData(DonglePath, 0x01, RemoteHomeUsage)]
    [InlineData(DonglePath, 0x0C, 0x00B0)]
    public void Consumer_report_from_wrong_device_page_or_usage_is_ignored(string path, int usagePage, int usage)
    {
        var (policy, _, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(path, 990, usagePage, usage));

        Assert.Equal(
            Press(ActionKind.Home, ActionSource.Keyboard, 1000),
            Assert.Single(policy.OnKey(Down(0xAC, 1000, injected: true)).Actions));
    }

    [Theory]
    [InlineData(RemoteHomeUsage, 0xA6, ActionKind.Back)]
    [InlineData(RemoteBackUsage, 0xAC, ActionKind.Home)]
    public void A_consumer_usage_with_the_wrong_home_back_pair_falls_back_to_keyboard(int usage, int vk, ActionKind kind)
    {
        var (policy, _, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 990, 0x0C, usage));
        var decision = policy.OnKey(Down(vk, 1000, injected: true));

        Assert.True(decision.Swallow);
        Assert.Equal(Press(kind, ActionSource.Keyboard, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void One_consumer_report_cannot_authorize_two_home_presses()
    {
        var (policy, _, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 990, 0x0C, RemoteHomeUsage));
        var first = policy.OnKey(Down(0xAC, 1000, injected: true));
        policy.OnKey(Up(0xAC, 1010, injected: true));
        var second = policy.OnKey(Down(0xAC, 1020, injected: true));

        Assert.Equal(Press(ActionKind.Home, ActionSource.Remote, 1000), Assert.Single(first.Actions));
        Assert.Equal(Press(ActionKind.Home, ActionSource.Keyboard, 1020), Assert.Single(second.Actions));
    }

    [Fact]
    public void Non_injected_key_ignores_a_fresh_consumer_report()
    {
        var (policy, _, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 990, 0x0C, RemoteHomeUsage));

        var decision = policy.OnKey(Down(0x26, 1000));

        Assert.Equal(Press(ActionKind.Up, ActionSource.Keyboard, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Long_press_keeps_source_from_key_down()
    {
        var (policy, time, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 970, 0x0C, RemoteHomeUsage));
        policy.OnKey(Down(0x0D, 1000, injected: true));
        time.Advance(TimeSpan.FromMilliseconds(600));

        var timer = policy.OnTimer(time.GetUtcNow());

        Assert.Equal(
            new Action(ActionKind.Ok, ActionPhase.Long, ActionSource.Keyboard, 1600),
            Assert.Single(timer.Actions));
    }

    [Fact]
    public void Remote_input_row_mapping_still_produces_keyboard_source_without_a_home_back_pair()
    {
        var config = new InputConfig(
            [
                new InputDevice(
                    "VID_1915&PID_1025",
                    ActionSource.Remote,
                    new Dictionary<string, ActionKind> { ["0x5D"] = ActionKind.Keyboard }),
            ],
            [GamepadButton.Back, GamepadButton.Start]);
        var policy = new InputPolicy(config, new FakeTimeProvider(T0), new InputRoutingState());

        // Sam wpis `source: remote` w input.json nie stanowi dowodu, że to naciśnięcie pochodziło z pilota.
        policy.OnRawKeyboard(new RawKeyboardInput(DonglePath, 980, 0x5D, 0x2B, Down: true));
        var down = policy.OnKey(Down(0x5D, 1000));

        Assert.True(down.Swallow);

        var log = new RecordingLog();
        new LogActionSink(log).Deliver(Assert.Single(down.Actions));
        var entry = Assert.Single(log.Entries);
        Assert.Equal("action", entry.Msg);
        Assert.Equal(ActionKind.Keyboard, entry.Data!["kind"]);
        Assert.Equal(ActionPhase.Press, entry.Data["phase"]);
        Assert.Equal(ActionSource.Keyboard, entry.Data["source"]);
    }

    [Fact]
    public void Repeat_keeps_source_from_key_down()
    {
        var (policy, time, _) = Create();

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 970, 0x0C, RemoteHomeUsage));
        policy.OnKey(Down(0x26, 1000, injected: true));
        time.Advance(TimeSpan.FromMilliseconds(400));

        var timer = policy.OnTimer(time.GetUtcNow());

        Assert.False(timer.Swallow);
        Assert.Equal(Repeat(ActionKind.Up, ActionSource.Keyboard, 1400), Assert.Single(timer.Actions));
    }

    // ── Własne wstrzyknięcia (znacznik) ───────────────────────────────────────

    /// <summary>
    /// Wiersz macierzy „zdarzenie ze znacznikiem”: własne wstrzyknięcie MediaStation nie daje Actions i nie jest
    /// połknięte, niezależnie od key-down/key-up i od wiersza AD-8. Home jest połknięty w każdym wierszu, więc bez
    /// znacznika każde z tych zdarzeń dałoby Action — dowód, że reguła stoi przed mapowaniem, a nie w `ShouldSwallow`.
    /// </summary>
    [Theory]
    [InlineData(InputMode.Shell, null)]
    [InlineData(InputMode.Osk, null)]
    [InlineData(InputMode.App, AppType.Web)]
    [InlineData(InputMode.App, AppType.Moonlight)]
    [InlineData(InputMode.App, AppType.Native)]
    public void Marked_event_gives_no_action_and_is_not_swallowed_in_every_routing_row(InputMode mode, AppType? appType)
    {
        var (policy, _, _) = Create(mode, appType);
        var marker = InjectionMarker.MediaStation;

        var down = policy.OnKey(Down(0xAC, 1000, extraInfo: marker));
        var up = policy.OnKey(Up(0xAC, 1100, extraInfo: marker));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Marked_key_up_leaves_real_hold_untouched()
    {
        var (policy, _, _) = Create();
        var marker = InjectionMarker.MediaStation;

        // Prawdziwy key-down OK uzbraja termin `long`; oznaczony key-up tego samego VK nie może go zamknąć.
        Assert.True(policy.OnKey(Down(0x0D, 1000)).Swallow);
        Assert.Equal(T0.AddMilliseconds(600), policy.NextDeadline);

        var markedUp = policy.OnKey(Up(0x0D, 1100, extraInfo: marker));
        Assert.False(markedUp.Swallow);
        Assert.Empty(markedUp.Actions);
        Assert.Equal(T0.AddMilliseconds(600), policy.NextDeadline);

        // Dopiero prawdziwy key-up zamyka trzymanie i daje press.
        var up = policy.OnKey(Up(0x0D, 1200));
        Assert.True(up.Swallow);
        Assert.Equal(Press(ActionKind.Ok, ActionSource.Keyboard, 1200), Assert.Single(up.Actions));
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Marked_key_down_does_not_arm_long_press_or_repeat()
    {
        var (policy, _, _) = Create();

        var down = policy.OnKey(Down(0x0D, 1000, extraInfo: InjectionMarker.MediaStation));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Injected_without_marker_is_still_mapped_and_swallowed()
    {
        var policy = new InputPolicy(WildcardConfig(), new FakeTimeProvider(T0), new InputRoutingState());

        // Sama flaga LLKHF_INJECTED nie jest ignorowana: brak znacznika to obce wstrzyknięcie (albo pilot),
        // więc trafia do mapowania i zostaje połknięte.
        var down = policy.OnKey(Down(0xAC, 1000, injected: true));

        Assert.True(down.Swallow);
        Assert.Equal(Press(ActionKind.Home, ActionSource.Keyboard, 1000), Assert.Single(down.Actions));
    }

    // ── Wildcard `*` ──────────────────────────────────────────────────────────

    [Fact]
    public void Wildcard_maps_a_keyboard_without_its_own_entry()
    {
        var policy = new InputPolicy(WildcardConfig(), new FakeTimeProvider(T0), new InputRoutingState());

        var down = policy.OnKey(Down(0x71, 1000));

        Assert.True(down.Swallow);
        Assert.Equal(Press(ActionKind.Keyboard, ActionSource.Keyboard, 1000), Assert.Single(down.Actions));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Raw_keyboard_device_cannot_make_an_arrow_remote(bool wildcardFirst)
    {
        var policy = new InputPolicy(WildcardConfig(wildcardFirst), new FakeTimeProvider(T0), new InputRoutingState());

        policy.OnRawKeyboard(new RawKeyboardInput(DonglePath, 980, 0x26, 0x48, Down: true));

        var decision = policy.OnKey(Down(0x26, 1000));

        Assert.Equal(Press(ActionKind.Up, ActionSource.Keyboard, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Unrelated_consumer_device_does_not_match_remote_signature()
    {
        var policy = new InputPolicy(WildcardConfig(wildcardFirst: true), new FakeTimeProvider(T0), new InputRoutingState());

        // Inny HID z tym samym usage nie może przypisać remote do wpisu dongla.
        policy.OnHidConsumer(new HidConsumerReport(@"\\?\HID#VID_046D&PID_C52B#1", 970, 0x0C, 0x00B0));
        policy.OnRawKeyboard(new RawKeyboardInput(DonglePath, 980, 0xAC, 0x45, Down: true));

        var decision = policy.OnKey(Down(0xAC, 1000, injected: true));

        Assert.True(decision.Swallow);
        Assert.Equal(Press(ActionKind.Home, ActionSource.Keyboard, 1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Device_signature_wildcard_matches_any_non_empty_path()
    {
        Assert.True(DeviceSignature.Matches(DonglePath, InputDevice.WildcardSignature));
        Assert.True(DeviceSignature.Matches(@"\\?\HID#VID_046D&PID_C52B#1", InputDevice.WildcardSignature));
        Assert.False(DeviceSignature.Matches("", InputDevice.WildcardSignature));
        Assert.False(DeviceSignature.Matches(null, InputDevice.WildcardSignature));
        Assert.False(DeviceSignature.Matches(DonglePath, null));
    }
}
