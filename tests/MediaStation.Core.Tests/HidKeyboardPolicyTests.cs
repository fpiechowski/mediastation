using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>Action <c>Keyboard</c> z raportu HID Consumer urządzenia <c>remote</c> (AD-8, bug 3.14).</summary>
public class HidKeyboardPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private const string DonglePath =
        @"\\?\HID#VID_1915&PID_1025&MI_01#7&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const string OtherPath = @"\\?\HID#VID_046D&PID_C52B#1";
    private const int AssistantUsage = 0x00CF;

    private sealed class FakeProbe(bool managed) : IForegroundProbe
    {
        public bool Managed { get; set; } = managed;

        public bool IsManaged() => Managed;
    }

    private static InputConfig Config() => new(
        [
            new InputDevice(
                "VID_1915&PID_1025",
                ActionSource.Remote,
                new Dictionary<string, ActionKind> { ["0xAC"] = ActionKind.Home, ["0xA6"] = ActionKind.Back })
            {
                Hid = new Dictionary<string, ActionKind> { ["0x000C:0x00CF"] = ActionKind.Keyboard },
            },
            new InputDevice(
                "VID_046D&PID_C52B",
                ActionSource.Keyboard,
                new Dictionary<string, ActionKind> { ["0x26"] = ActionKind.Up }),
        ],
        [GamepadButton.Back, GamepadButton.Start]);

    private static (InputPolicy Policy, FakeTimeProvider Time, FakeProbe Probe) Create(bool managed = true)
    {
        var time = new FakeTimeProvider(T0);
        var probe = new FakeProbe(managed);
        var policy = new InputPolicy(Config(), time, new InputRoutingState(), probe);
        return (policy, time, probe);
    }

    private static HidConsumerReport Report(long timeMs, int usage, string path = DonglePath, int page = 0x0C) =>
        new(path, timeMs, page, usage);

    private static Contracts.Action Keyboard(long timeMs) =>
        new(ActionKind.Keyboard, ActionPhase.Press, ActionSource.Remote, timeMs);

    [Fact]
    public void Press_yields_one_remote_keyboard_action_without_swallow()
    {
        var (policy, _, _) = Create();

        var decision = policy.OnHidConsumer(Report(1000, AssistantUsage));

        Assert.False(decision.Swallow);
        Assert.Equal(Keyboard(1000), Assert.Single(decision.Actions));
    }

    [Fact]
    public void Repeated_report_without_release_yields_no_action()
    {
        var (policy, _, _) = Create();
        policy.OnHidConsumer(Report(1000, AssistantUsage));

        var again = policy.OnHidConsumer(Report(1100, AssistantUsage));

        Assert.False(again.Swallow);
        Assert.Empty(again.Actions);
    }

    [Fact]
    public void Release_then_press_yields_second_action()
    {
        var (policy, _, _) = Create();
        policy.OnHidConsumer(Report(1000, AssistantUsage));

        Assert.Empty(policy.OnHidConsumer(Report(1200, 0)).Actions);
        var second = policy.OnHidConsumer(Report(2000, AssistantUsage));

        Assert.Equal(Keyboard(2000), Assert.Single(second.Actions));
    }

    [Fact]
    public void Another_usage_ends_the_hold_so_the_next_press_yields_action()
    {
        var (policy, _, _) = Create();
        policy.OnHidConsumer(Report(1000, AssistantUsage));

        Assert.Empty(policy.OnHidConsumer(Report(1100, 0x00B0)).Actions);
        var again = policy.OnHidConsumer(Report(1200, AssistantUsage));

        Assert.Equal(Keyboard(1200), Assert.Single(again.Actions));
    }

    [Fact]
    public void Holding_gives_no_repeat_or_long_and_no_deadline()
    {
        var (policy, time, _) = Create();
        policy.OnHidConsumer(Report(1000, AssistantUsage));
        for (var t = 1100; t < 5000; t += 100)
        {
            Assert.Empty(policy.OnHidConsumer(Report(t, AssistantUsage)).Actions);
        }

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Null(policy.NextDeadline);
        Assert.Empty(policy.OnTimer(time.GetUtcNow()).Actions);
    }

    [Fact]
    public void Stale_hold_older_than_max_repeat_hold_starts_a_new_press()
    {
        var (policy, _, _) = Create();
        policy.OnHidConsumer(Report(1000, AssistantUsage));

        var decision = policy.OnHidConsumer(Report(1000 + InputPolicy.MaxRepeatHoldMs + 1, AssistantUsage));

        Assert.Single(decision.Actions);
    }

    [Fact]
    public void Continuous_reports_keep_the_hold_fresh_past_max_repeat_hold()
    {
        var (policy, _, _) = Create();
        policy.OnHidConsumer(Report(1000, AssistantUsage));

        for (var t = 1000 + 30_000; t <= 1000 + 90_000; t += 30_000)
        {
            Assert.Empty(policy.OnHidConsumer(Report(t, AssistantUsage)).Actions);
        }
    }

    [Fact]
    public void Foreign_window_yields_no_action_and_the_hold_is_still_tracked()
    {
        var (policy, _, probe) = Create(managed: false);

        Assert.Empty(policy.OnHidConsumer(Report(1000, AssistantUsage)).Actions);

        probe.Managed = true;
        Assert.Empty(policy.OnHidConsumer(Report(1100, AssistantUsage)).Actions);
        policy.OnHidConsumer(Report(1200, 0));
        Assert.Single(policy.OnHidConsumer(Report(1300, AssistantUsage)).Actions);
    }

    [Theory]
    [InlineData(DonglePath, 0x0C, 0x00B0)]
    [InlineData(DonglePath, 0x01, AssistantUsage)]
    [InlineData(OtherPath, 0x0C, AssistantUsage)]
    [InlineData(@"\\?\HID#VID_FFFF&PID_FFFF#1", 0x0C, AssistantUsage)]
    public void Usage_outside_hid_other_page_or_other_device_yields_no_action(string path, int page, int usage)
    {
        var (policy, _, _) = Create();

        var decision = policy.OnHidConsumer(Report(1000, usage, path, page));

        Assert.False(decision.Swallow);
        Assert.Empty(decision.Actions);
    }

    [Fact]
    public void Home_after_consumer_0223_and_vk_0xAC_is_still_one_action_from_the_vk()
    {
        var (policy, _, _) = Create();

        var hid = policy.OnHidConsumer(Report(950, 0x0223));
        var key = policy.OnKey(new KeyStroke(0xAC, KeyTransition.Down, true, 1000));

        Assert.Empty(hid.Actions);
        Assert.Equal(
            new Contracts.Action(ActionKind.Home, ActionPhase.Press, ActionSource.Remote, 1000),
            Assert.Single(key.Actions));
    }

    [Fact]
    public void Back_after_consumer_0224_and_vk_0xA6_is_still_one_action_from_the_vk()
    {
        var (policy, _, _) = Create();

        policy.OnHidConsumer(Report(950, 0x0224));
        var key = policy.OnKey(new KeyStroke(0xA6, KeyTransition.Down, true, 1000));

        Assert.Equal(
            new Contracts.Action(ActionKind.Back, ActionPhase.Press, ActionSource.Remote, 1000),
            Assert.Single(key.Actions));
    }
}
