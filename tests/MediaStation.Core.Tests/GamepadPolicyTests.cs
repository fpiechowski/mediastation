using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Pad w <see cref="InputPolicy"/> (AD-8) na <see cref="FakeTimeProvider"/>: jedna Action na krawędź naciśnięcia,
/// wspólna siatka <c>repeat</c>/<c>long</c> z klawiaturą, kombinacja Home i wiersze routingu, w których pad nie ma
/// połknięcia — wiersz decyduje wyłącznie o zestawie Actions.
/// </summary>
public class GamepadPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static SetInputRouting Route(long epoch, InputMode mode, AppType? appType = null) =>
        new(epoch, mode, appType, PointerPolicy.Blocked, epoch);

    /// <summary>
    /// Polityka z lokalną kopią routingu i domyślnym mapowaniem pada (D-pad, A, B, Y); config urządzeń jest pusty,
    /// bo ten zestaw testów nie dotyka klawiatury.
    /// </summary>
    private static (InputPolicy Policy, FakeTimeProvider Time, InputRoutingState Routing) Create(
        InputMode mode = InputMode.Shell,
        AppType? appType = null,
        InputGamepad? gamepad = null,
        int longPressMs = 600,
        int repeatDelayMs = 400,
        int repeatIntervalMs = 120)
    {
        var time = new FakeTimeProvider(T0);
        var routing = new InputRoutingState();
        routing.Apply(Route(1, mode, appType));
        var config = new InputConfig([], [GamepadButton.Back, GamepadButton.Start])
        {
            Gamepad = gamepad ?? new InputGamepad(),
            Timing = new InputTiming
            {
                LongPressMs = longPressMs,
                RepeatDelayMs = repeatDelayMs,
                RepeatIntervalMs = repeatIntervalMs,
            },
        };
        return (new InputPolicy(config, time, routing), time, routing);
    }

    private static GamepadSnapshot Snap(long timeMs, params GamepadButton[] buttons) => new(timeMs, buttons);

    private static GamepadSnapshot Unavailable(long timeMs) => new(timeMs, [], Available: false);

    private static Action Press(ActionKind kind, long tsMono) =>
        new(kind, ActionPhase.Press, ActionSource.Gamepad, tsMono);

    private static Action Repeat(ActionKind kind, long tsMono) =>
        new(kind, ActionPhase.Repeat, ActionSource.Gamepad, tsMono);

    private static Action Long(ActionKind kind, long tsMono) =>
        new(kind, ActionPhase.Long, ActionSource.Gamepad, tsMono);

    // ── Jedna Action na naciśnięcie i long OK ─────────────────────────────────

    [Fact]
    public void Shell_a_before_threshold_gives_one_ok_press_on_release()
    {
        var (policy, _, _) = Create();

        var down = policy.OnGamepad(Snap(1000, GamepadButton.A));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.Equal(T0.AddMilliseconds(600), policy.NextDeadline);

        var up = policy.OnGamepad(Snap(1200));

        Assert.False(up.Swallow);
        Assert.Equal(Press(ActionKind.Ok, 1200), Assert.Single(up.Actions));
        Assert.Null(policy.NextDeadline);
    }

    [Theory]
    [InlineData(InputMode.Shell)]
    [InlineData(InputMode.Osk)]
    public void A_past_threshold_gives_only_long_and_no_press(InputMode mode)
    {
        var (policy, time, _) = Create(mode);

        policy.OnGamepad(Snap(1000, GamepadButton.A));
        time.Advance(TimeSpan.FromMilliseconds(600));

        var timer = policy.OnTimer(time.GetUtcNow());

        Assert.False(timer.Swallow);
        Assert.Equal(Long(ActionKind.Ok, 1600), Assert.Single(timer.Actions));
        Assert.Null(policy.NextDeadline);

        Assert.Empty(policy.OnGamepad(Snap(1800)).Actions);
    }

    [Fact]
    public void Repeated_snapshot_without_change_gives_no_second_action()
    {
        var (policy, _, _) = Create();

        Assert.Equal(
            Press(ActionKind.Down, 1000),
            Assert.Single(policy.OnGamepad(Snap(1000, GamepadButton.DpadDown)).Actions));

        var repeated = policy.OnGamepad(Snap(1010, GamepadButton.DpadDown));

        Assert.False(repeated.Swallow);
        Assert.Empty(repeated.Actions);
    }

    [Fact]
    public void Custom_mapping_replaces_the_default_one()
    {
        var (policy, _, _) = Create(gamepad: new InputGamepad { A = ActionKind.Back, B = null, Y = null });

        var down = policy.OnGamepad(Snap(1000, GamepadButton.A));

        Assert.Equal(Press(ActionKind.Back, 1000), Assert.Single(down.Actions));
        Assert.Null(policy.NextDeadline);
    }

    // ── Repeat trzymanego D-pada ──────────────────────────────────────────────

    [Fact]
    public void Held_dpad_repeats_after_delay_and_interval_until_release()
    {
        var (policy, time, _) = Create();

        Assert.Equal(
            Press(ActionKind.Up, 1000),
            Assert.Single(policy.OnGamepad(Snap(1000, GamepadButton.DpadUp)).Actions));
        Assert.Equal(T0.AddMilliseconds(400), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
        Assert.Equal(T0.AddMilliseconds(400), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(
            Repeat(ActionKind.Up, 1400),
            Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions));
        Assert.Equal(T0.AddMilliseconds(520), policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(120));
        Assert.Equal(
            Repeat(ActionKind.Up, 1520),
            Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions));

        var up = policy.OnGamepad(Snap(1650));

        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
        Assert.Null(policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(120));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Theory]
    [InlineData(GamepadButton.B, ActionKind.Back)]
    [InlineData(GamepadButton.Y, ActionKind.Keyboard)]
    public void Held_non_dpad_button_has_no_repeat_grid(GamepadButton button, ActionKind kind)
    {
        var (policy, time, _) = Create();

        Assert.Equal(Press(kind, 1000), Assert.Single(policy.OnGamepad(Snap(1000, button)).Actions));
        Assert.Null(policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(640));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Mode_change_during_hold_keeps_press_decision_and_repeat_grid()
    {
        var (policy, time, routing) = Create();

        Assert.Equal(
            Press(ActionKind.Up, 1000),
            Assert.Single(policy.OnGamepad(Snap(1000, GamepadButton.DpadUp)).Actions));

        Assert.True(routing.Apply(Route(2, InputMode.App, AppType.Moonlight)));

        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(
            Repeat(ActionKind.Up, 1400),
            Assert.Single(policy.OnTimer(time.GetUtcNow()).Actions));

        var up = policy.OnGamepad(Snap(1500));
        Assert.Empty(up.Actions);
        Assert.Null(policy.NextDeadline);
    }

    // ── Wiersze routingu AD-8 ─────────────────────────────────────────────────

    [Theory]
    [InlineData(GamepadButton.DpadUp, ActionKind.Up)]
    [InlineData(GamepadButton.A, ActionKind.Ok)]
    [InlineData(GamepadButton.B, ActionKind.Back)]
    [InlineData(GamepadButton.Y, ActionKind.Keyboard)]
    public void Web_row_gives_actions_for_every_mapped_button(GamepadButton button, ActionKind kind)
    {
        var (policy, _, _) = Create(InputMode.App, AppType.Web);

        var down = policy.OnGamepad(Snap(1000, button));

        Assert.Equal(Press(kind, 1000), Assert.Single(down.Actions));
    }

    [Theory]
    [InlineData(GamepadButton.DpadUp)]
    [InlineData(GamepadButton.DpadRight)]
    [InlineData(GamepadButton.A)]
    [InlineData(GamepadButton.B)]
    [InlineData(GamepadButton.Y)]
    public void Moonlight_row_gives_no_actions_for_any_mapped_button(GamepadButton button)
    {
        var (policy, time, _) = Create(InputMode.App, AppType.Moonlight);

        var down = policy.OnGamepad(Snap(1000, button));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.Null(policy.NextDeadline);

        // Minięte okno repeat i progu long nie produkuje nic: wiersz nie uzbraja siatki ani terminu long.
        time.Advance(TimeSpan.FromMilliseconds(640));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
        Assert.Null(policy.NextDeadline);

        var up = policy.OnGamepad(Snap(1700));
        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Theory]
    [InlineData(GamepadButton.DpadUp)]
    [InlineData(GamepadButton.DpadRight)]
    [InlineData(GamepadButton.A)]
    public void Native_row_passes_dpad_and_ok_without_actions(GamepadButton button)
    {
        var (policy, time, _) = Create(InputMode.App, AppType.Native);

        Assert.Empty(policy.OnGamepad(Snap(1000, button)).Actions);
        Assert.Null(policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(640));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);

        Assert.Empty(policy.OnGamepad(Snap(1700)).Actions);
    }

    [Theory]
    [InlineData(GamepadButton.B, ActionKind.Back)]
    [InlineData(GamepadButton.Y, ActionKind.Keyboard)]
    public void Native_row_gives_actions_for_back_and_keyboard_buttons(GamepadButton button, ActionKind kind)
    {
        var (policy, _, _) = Create(InputMode.App, AppType.Native);

        var down = policy.OnGamepad(Snap(1000, button));

        Assert.False(down.Swallow);
        Assert.Equal(Press(kind, 1000), Assert.Single(down.Actions));

        Assert.Empty(policy.OnGamepad(Snap(1100)).Actions);
    }

    [Fact]
    public void Native_row_gives_back_then_keyboard_for_b_then_y()
    {
        var (policy, _, _) = Create(InputMode.App, AppType.Native);

        var b = policy.OnGamepad(Snap(1000, GamepadButton.B));
        Assert.Equal(Press(ActionKind.Back, 1000), Assert.Single(b.Actions));
        Assert.Empty(policy.OnGamepad(Snap(1100)).Actions);

        var y = policy.OnGamepad(Snap(1200, GamepadButton.Y));
        Assert.Equal(Press(ActionKind.Keyboard, 1200), Assert.Single(y.Actions));
        Assert.Empty(policy.OnGamepad(Snap(1300)).Actions);
    }

    [Fact]
    public void Mapped_home_button_is_delivered_in_moonlight()
    {
        var (policy, _, _) = Create(
            InputMode.App,
            AppType.Moonlight,
            new InputGamepad { X = ActionKind.Home });

        var down = policy.OnGamepad(Snap(1000, GamepadButton.X));

        Assert.Equal(Press(ActionKind.Home, 1000), Assert.Single(down.Actions));
    }

    [Theory]
    [InlineData(GamepadButton.DpadUp, ActionKind.Up)]
    [InlineData(GamepadButton.A, ActionKind.Ok)]
    public void App_without_app_type_gives_actions_like_shell(GamepadButton button, ActionKind kind)
    {
        var (policy, _, _) = Create(InputMode.App);

        var down = policy.OnGamepad(Snap(1000, button));

        Assert.Equal(Press(kind, 1000), Assert.Single(down.Actions));
    }

    // ── Home z kombinacji ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(InputMode.Shell, null)]
    [InlineData(InputMode.Osk, null)]
    [InlineData(InputMode.App, AppType.Web)]
    [InlineData(InputMode.App, AppType.Moonlight)]
    [InlineData(InputMode.App, AppType.Native)]
    [InlineData(InputMode.App, null)]
    public void Home_combo_gives_exactly_one_action_per_full_composition(InputMode mode, AppType? appType)
    {
        var (policy, _, _) = Create(mode, appType);

        var down = policy.OnGamepad(Snap(1000, GamepadButton.Back, GamepadButton.Start));

        Assert.False(down.Swallow);
        Assert.Equal(Press(ActionKind.Home, 1000), Assert.Single(down.Actions));

        // Trzymanie pełnej kombinacji nie daje kolejnych Actions ani Actions składników.
        Assert.Empty(policy.OnGamepad(Snap(1100, GamepadButton.Back, GamepadButton.Start)).Actions);

        // Zwolnienie jednego składnika przerywa kombinację i uzbraja kolejne złożenie.
        Assert.Empty(policy.OnGamepad(Snap(1200, GamepadButton.Back)).Actions);
        Assert.Equal(
            Press(ActionKind.Home, 1300),
            Assert.Single(policy.OnGamepad(Snap(1300, GamepadButton.Back, GamepadButton.Start)).Actions));
    }

    [Fact]
    public void Home_combo_needs_both_buttons()
    {
        var (policy, _, _) = Create();

        Assert.Empty(policy.OnGamepad(Snap(1000, GamepadButton.Start)).Actions);
        Assert.Empty(policy.OnGamepad(Snap(1100, GamepadButton.Back)).Actions);
        Assert.Equal(
            Press(ActionKind.Home, 1200),
            Assert.Single(policy.OnGamepad(Snap(1200, GamepadButton.Back, GamepadButton.Start)).Actions));
    }

    // ── Przyciski niezmapowane i brak pada ────────────────────────────────────

    [Fact]
    public void Unmapped_button_gives_no_action_and_no_held_entry()
    {
        var (policy, time, _) = Create();

        var down = policy.OnGamepad(Snap(1000, GamepadButton.X));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.Null(policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(640));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
        Assert.Null(policy.NextDeadline);

        Assert.Empty(policy.OnGamepad(Snap(1700)).Actions);
    }

    [Fact]
    public void Unavailable_snapshot_drops_a_held_button_without_action()
    {
        var (policy, time, _) = Create();

        Assert.Single(policy.OnGamepad(Snap(1000, GamepadButton.DpadUp)).Actions);
        Assert.Equal(T0.AddMilliseconds(400), policy.NextDeadline);

        var unavailable = policy.OnGamepad(Unavailable(1100));

        Assert.False(unavailable.Swallow);
        Assert.Empty(unavailable.Actions);
        Assert.Null(policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
    }

    [Fact]
    public void Unavailable_snapshot_drops_a_held_a_without_press()
    {
        var (policy, _, _) = Create();

        Assert.Empty(policy.OnGamepad(Snap(1000, GamepadButton.A)).Actions);
        Assert.Equal(T0.AddMilliseconds(600), policy.NextDeadline);

        var unavailable = policy.OnGamepad(Unavailable(1100));

        Assert.False(unavailable.Swallow);
        Assert.Empty(unavailable.Actions);
        Assert.Null(policy.NextDeadline);

        // Reconnect z wciąż trzymanym A: pierwsza migawka to nowy stan początkowy, bez press i bez long.
        Assert.Empty(policy.OnGamepad(Snap(1200, GamepadButton.A)).Actions);
        Assert.Null(policy.NextDeadline);

        // Zwolnienie baseline'owego A jest ciche, a kolejne naciśnięcie działa normalnie.
        Assert.Empty(policy.OnGamepad(Snap(1300)).Actions);
        Assert.Empty(policy.OnGamepad(Snap(1400, GamepadButton.A)).Actions);
        Assert.Equal(Press(ActionKind.Ok, 1500), Assert.Single(policy.OnGamepad(Snap(1500)).Actions));
    }

    [Fact]
    public void Reconnect_with_a_held_home_combo_does_not_emit_second_home()
    {
        var (policy, _, _) = Create();

        Assert.Equal(
            Press(ActionKind.Home, 1000),
            Assert.Single(policy.OnGamepad(Snap(1000, GamepadButton.Back, GamepadButton.Start)).Actions));

        Assert.Empty(policy.OnGamepad(Unavailable(1100)).Actions);

        // Reconnect: pełna kombinacja jest baseline'em, więc nie daje drugiego Home.
        Assert.Empty(policy.OnGamepad(Snap(1200, GamepadButton.Back, GamepadButton.Start)).Actions);
        Assert.Empty(policy.OnGamepad(Snap(1300, GamepadButton.Back, GamepadButton.Start)).Actions);

        // Przerwanie i ponowne złożenie po reconnect daje kolejne Home.
        Assert.Empty(policy.OnGamepad(Snap(1400, GamepadButton.Back)).Actions);
        Assert.Equal(
            Press(ActionKind.Home, 1500),
            Assert.Single(policy.OnGamepad(Snap(1500, GamepadButton.Back, GamepadButton.Start)).Actions));
    }

    [Fact]
    public void Reconnect_with_a_held_dpad_does_not_emit_press_or_arm_repeat()
    {
        var (policy, time, _) = Create();

        Assert.Equal(
            Press(ActionKind.Up, 1000),
            Assert.Single(policy.OnGamepad(Snap(1000, GamepadButton.DpadUp)).Actions));

        Assert.Empty(policy.OnGamepad(Unavailable(1100)).Actions);

        Assert.Empty(policy.OnGamepad(Snap(1200, GamepadButton.DpadUp)).Actions);
        Assert.Null(policy.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(640));
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
        Assert.Null(policy.NextDeadline);

        Assert.Empty(policy.OnGamepad(Snap(1300)).Actions);
    }

    [Fact]
    public void Gamepad_action_logs_source_gamepad()
    {
        var (policy, _, _) = Create(InputMode.App, AppType.Native);

        var down = policy.OnGamepad(Snap(1000, GamepadButton.B));

        var log = new RecordingLog();
        new LogActionSink(log).Deliver(Assert.Single(down.Actions));
        var entry = Assert.Single(log.Entries);
        Assert.Equal("action", entry.Msg);
        Assert.Equal(ActionKind.Back, entry.Data!["kind"]);
        Assert.Equal(ActionPhase.Press, entry.Data["phase"]);
        Assert.Equal(ActionSource.Gamepad, entry.Data["source"]);
    }
}
