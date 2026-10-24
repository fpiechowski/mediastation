using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Bramka foreground (AD-8): klawiatura i pad dają Actions i połykają wyłącznie nad oknem MediaStation, a jedynym
/// wyjątkiem jest Home z urządzenia <c>remote</c>. Pokrywa wiersze macierzy I/O planu AD-8 oraz
/// <see cref="ManagedWindows"/> i <see cref="ManagedForegroundProbe"/>.
/// </summary>
public class ForegroundGateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private const string DonglePath =
        @"\\?\HID#VID_1915&PID_1025&MI_01#7&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private sealed class FakeProbe(bool managed) : IForegroundProbe
    {
        public bool Managed { get; set; } = managed;

        public bool IsManaged() => Managed;
    }

    private sealed class FakeForegroundWindow : IForegroundWindow
    {
        public ForegroundWindowInfo? Value { get; set; }

        public ForegroundWindowInfo? Current() => Value;
    }

    private static InputConfig Config() => new(
        [
            new InputDevice(
                "VID_1915&PID_1025",
                ActionSource.Remote,
                new Dictionary<string, ActionKind>
                {
                    ["0xAC"] = ActionKind.Home,
                    ["0xA6"] = ActionKind.Back,
                    ["0x0D"] = ActionKind.Ok,
                }),
            new InputDevice(
                InputDevice.WildcardSignature,
                ActionSource.Keyboard,
                new Dictionary<string, ActionKind>
                {
                    ["0x0D"] = ActionKind.Ok,
                    ["0x1B"] = ActionKind.Back,
                    ["0x26"] = ActionKind.Up,
                    ["0x24"] = ActionKind.Home,
                }),
        ],
        [GamepadButton.Back, GamepadButton.Start]);

    private static (InputPolicy Policy, FakeProbe Probe) Create(InputMode mode, AppType? appType, bool managed)
    {
        var routing = new InputRoutingState();
        routing.Apply(new SetInputRouting(1, mode, appType, PointerPolicy.Blocked, 1));
        var probe = new FakeProbe(managed);
        return (new InputPolicy(Config(), new FakeTimeProvider(T0), routing, probe), probe);
    }

    private static KeyStroke Down(int vk, long timeMs, bool injected = false) =>
        new(vk, KeyTransition.Down, injected, timeMs);

    private static KeyStroke Up(int vk, long timeMs) => new(vk, KeyTransition.Up, false, timeMs);

    // ── Klawiatura: wiersze macierzy ──────────────────────────────────────────

    [Theory]
    [InlineData(InputMode.Shell, null)]
    [InlineData(InputMode.Osk, null)]
    [InlineData(InputMode.App, AppType.Web)]
    [InlineData(InputMode.App, AppType.Moonlight)]
    [InlineData(InputMode.App, AppType.Native)]
    public void Foreign_foreground_passes_enter_escape_and_arrows_without_actions_in_every_mode(InputMode mode, AppType? appType)
    {
        var (policy, _) = Create(mode, appType, managed: false);

        foreach (var vk in new[] { 0x0D, 0x1B, 0x26 })
        {
            var down = policy.OnKey(Down(vk, 1000));
            var up = policy.OnKey(Up(vk, 1050));

            Assert.False(down.Swallow);
            Assert.Empty(down.Actions);
            Assert.False(up.Swallow);
            Assert.Empty(up.Actions);
        }

        Assert.Null(policy.NextDeadline);
    }

    [Fact]
    public void Managed_foreground_keeps_todays_table()
    {
        var (policy, _) = Create(InputMode.Shell, null, managed: true);

        var down = policy.OnKey(Down(0x0D, 1000));

        Assert.True(down.Swallow);
        Assert.NotNull(policy.NextDeadline);
        Assert.Equal(
            new Contracts.Action(ActionKind.Ok, ActionPhase.Press, ActionSource.Keyboard, 1100),
            Assert.Single(policy.OnKey(Up(0x0D, 1100)).Actions));

        var arrow = policy.OnKey(Down(0x26, 2000));
        Assert.True(arrow.Swallow);
        Assert.Equal(ActionKind.Up, Assert.Single(arrow.Actions).Kind);
    }

    [Fact]
    public void Remote_home_over_foreign_window_is_swallowed_and_yields_home()
    {
        var (policy, _) = Create(InputMode.Shell, null, managed: false);

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 970, 0x0C, 0x0223));
        var down = policy.OnKey(Down(0xAC, 1000, injected: true));
        var up = policy.OnKey(Up(0xAC, 1050));

        Assert.True(down.Swallow);
        Assert.Equal(
            new Contracts.Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1000),
            Assert.Single(down.Actions));
        Assert.True(up.Swallow);
    }

    [Fact]
    public void Keyboard_home_over_foreign_window_passes_without_action()
    {
        var (policy, _) = Create(InputMode.Shell, null, managed: false);

        var down = policy.OnKey(Down(0x24, 1000));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
    }

    [Fact]
    public void Remote_back_over_foreign_window_passes_without_action()
    {
        var (policy, _) = Create(InputMode.Shell, null, managed: false);

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 970, 0x0C, 0x0224));
        var down = policy.OnKey(Down(0xA6, 1000, injected: true));
        var up = policy.OnKey(Up(0xA6, 1050));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Remote_non_home_key_over_foreign_window_passes()
    {
        var (policy, _) = Create(InputMode.Shell, null, managed: false);

        policy.OnHidConsumer(new HidConsumerReport(DonglePath, 970, 0x0C, 0x0223));
        var down = policy.OnKey(Down(0x0D, 1000, injected: true));

        Assert.False(down.Swallow);
        Assert.Empty(down.Actions);
    }

    [Fact]
    public void Key_up_follows_the_key_down_decision_after_a_window_switch()
    {
        var (policy, probe) = Create(InputMode.Shell, null, managed: true);

        var down = policy.OnKey(Down(0x1B, 1000));
        probe.Managed = false;
        var up = policy.OnKey(Up(0x1B, 1100));

        Assert.True(down.Swallow);
        Assert.Equal(ActionKind.Back, Assert.Single(down.Actions).Kind);
        Assert.True(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Key_up_of_a_passed_key_passes_after_returning_to_the_shell()
    {
        var (policy, probe) = Create(InputMode.Shell, null, managed: false);

        var down = policy.OnKey(Down(0x1B, 1000));
        probe.Managed = true;
        var up = policy.OnKey(Up(0x1B, 1100));

        Assert.False(down.Swallow);
        Assert.False(up.Swallow);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Key_up_without_key_down_over_foreign_window_passes()
    {
        var (policy, _) = Create(InputMode.Shell, null, managed: false);

        Assert.False(policy.OnKey(Up(0x1B, 1000)).Swallow);
    }

    [Fact]
    public void Policy_without_probe_acts_as_always_managed()
    {
        var routing = new InputRoutingState();
        var policy = new InputPolicy(Config(), new FakeTimeProvider(T0), routing);

        Assert.True(policy.OnKey(Down(0x1B, 1000)).Swallow);
    }

    [Fact]
    public void Pointer_policy_ignores_the_gate()
    {
        var (policy, _) = Create(InputMode.Shell, null, managed: false);

        var decision = policy.OnPointer(new PointerStroke(PointerMessage.ButtonDown, PointerButton.Left, 10, 10, 0, false, 1000), clip: null);

        Assert.False(decision.Swallow);
    }

    // ── Pad ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Gamepad_over_foreign_window_gives_no_actions_and_re_adopts_the_baseline_on_return()
    {
        var (policy, probe) = Create(InputMode.Shell, null, managed: false);

        Assert.Empty(policy.OnGamepad(new GamepadSnapshot(1000, [GamepadButton.A])).Actions);
        Assert.Empty(policy.OnGamepad(new GamepadSnapshot(1100, [GamepadButton.Back, GamepadButton.Start])).Actions);

        probe.Managed = true;

        // A trzymany od czasu nieobecności jest stanem początkowym: bez krawędzi, więc bez Actions.
        Assert.Empty(policy.OnGamepad(new GamepadSnapshot(1200, [GamepadButton.A])).Actions);
        Assert.Empty(policy.OnGamepad(new GamepadSnapshot(1300, [])).Actions);

        // Nowe naciśnięcie po zwolnieniu daje krawędź: OK w shellu to `press` na zwolnieniu.
        Assert.Empty(policy.OnGamepad(new GamepadSnapshot(1400, [GamepadButton.A])).Actions);
        Assert.Equal(ActionKind.Ok, Assert.Single(policy.OnGamepad(new GamepadSnapshot(1450, [])).Actions).Kind);
    }

    [Fact]
    public void Gamepad_home_combo_over_foreign_window_gives_no_home()
    {
        var (policy, _) = Create(InputMode.Shell, null, managed: false);

        var decision = policy.OnGamepad(new GamepadSnapshot(1000, [GamepadButton.Back, GamepadButton.Start]));

        Assert.Empty(decision.Actions);
        Assert.False(decision.Swallow);
    }

    [Fact]
    public void Gamepad_switching_to_a_foreign_window_drops_a_held_button_without_actions()
    {
        var (policy, probe) = Create(InputMode.Shell, null, managed: true);

        Assert.Empty(policy.OnGamepad(new GamepadSnapshot(1000, [GamepadButton.A])).Actions);
        Assert.NotNull(policy.NextDeadline);

        probe.Managed = false;
        Assert.Empty(policy.OnGamepad(new GamepadSnapshot(1100, [GamepadButton.A])).Actions);

        Assert.Null(policy.NextDeadline);
    }

    // ── ManagedWindows i sonda ────────────────────────────────────────────────

    [Fact]
    public void Managed_windows_match_shell_osk_app_hwnd_and_root_pid()
    {
        var managed = new ManagedWindows();
        managed.SetShell(100);
        managed.Apply(new SetManagedWindows(1, 200, [new ManagedWindow(300, 4321)], 1));

        Assert.True(managed.Contains(100, 1));
        Assert.True(managed.Contains(200, 2));
        Assert.True(managed.Contains(300, 3));
        Assert.True(managed.Contains(999, 4321));
        Assert.False(managed.Contains(999, 5));
        Assert.False(managed.Contains(0, 0));
    }

    [Fact]
    public void Managed_windows_apply_only_a_strictly_newer_epoch_and_keep_the_shell()
    {
        var managed = new ManagedWindows();
        managed.SetShell(100);

        Assert.True(managed.Apply(new SetManagedWindows(1, null, [new ManagedWindow(300, 4321)], 2)));
        Assert.False(managed.Apply(new SetManagedWindows(2, null, [], 2)));
        Assert.False(managed.Apply(new SetManagedWindows(3, null, [], 1)));
        Assert.True(managed.Contains(300, 0));

        Assert.True(managed.Apply(new SetManagedWindows(4, null, [], 3)));
        Assert.False(managed.Contains(300, 4321));
        Assert.True(managed.Contains(100, 0));
    }

    [Fact]
    public void Probe_treats_unknown_foreground_as_foreign_and_matches_known_windows()
    {
        var window = new FakeForegroundWindow();
        var managed = new ManagedWindows();
        managed.SetShell(100);
        var probe = new ManagedForegroundProbe(window, managed);

        Assert.False(probe.IsManaged());

        window.Value = new ForegroundWindowInfo(100, 7);
        Assert.True(probe.IsManaged());

        window.Value = new ForegroundWindowInfo(555, 8);
        Assert.False(probe.IsManaged());
    }
}
