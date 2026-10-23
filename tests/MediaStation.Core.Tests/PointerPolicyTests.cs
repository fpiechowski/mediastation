using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Polityka wskaźnika (AD-8) na lokalnej kopii routingu: wskaźnik nie jest połykany w żadnym wierszu (także przy
/// <c>pointer: blocked</c>, bo fizyczna mysz musi zawsze działać), a w <c>osk</c> z prostokątem clipu połykane jest
/// tylko kliknięcie poza nim. Własne wstrzyknięcie (<see cref="InjectionMarker"/>) i zmiana wiersza routingu
/// (zwolnienie clipu) mają osobne przypadki.
/// </summary>
public class PointerPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Prostokąt klawiatury ekranowej: <c>(0,0)-(100,100)</c>, prawa i dolna krawędź wyłączna.</summary>
    private static readonly ScreenRectangle Clip = new(0, 0, 100, 100);

    private static (InputPolicy Policy, InputRoutingState Routing) Create(
        InputMode mode,
        AppType? appType,
        PointerPolicy pointer,
        long? clipHwnd = null)
    {
        var routing = new InputRoutingState();
        routing.Apply(new SetInputRouting(1, mode, appType, pointer, 1, clipHwnd));

        var policy = new InputPolicy(new InputConfig([], []), new FakeTimeProvider(T0), routing);
        return (policy, routing);
    }

    private static PointerStroke Stroke(
        PointerMessage message,
        int x = 50,
        int y = 50,
        PointerButton button = PointerButton.None,
        int wheelDelta = 0,
        bool injected = false,
        long extraInfo = 0) =>
        new(message, button, x, y, wheelDelta, injected, 1000, extraInfo);

    private static void AssertPassed(InputDecision decision)
    {
        Assert.False(decision.Swallow);
        Assert.Empty(decision.Actions);
    }

    private static void AssertSwallowed(InputDecision decision)
    {
        Assert.True(decision.Swallow);
        Assert.Empty(decision.Actions);
    }

    // ── Wiersze bez clipu ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(PointerMessage.Move, PointerButton.None)]
    [InlineData(PointerMessage.ButtonDown, PointerButton.Left)]
    [InlineData(PointerMessage.ButtonUp, PointerButton.Left)]
    [InlineData(PointerMessage.Wheel, PointerButton.None)]
    public void Blocked_row_passes_movement_clicks_and_wheel_so_a_physical_mouse_always_works(PointerMessage message, PointerButton button)
    {
        var (policy, _) = Create(InputMode.Shell, null, PointerPolicy.Blocked);

        AssertPassed(policy.OnPointer(Stroke(message, button: button), clip: null));
    }

    [Fact]
    public void Blocked_row_in_app_web_passes_the_whole_pointer_matrix()
    {
        var (policy, _) = Create(InputMode.App, AppType.Web, PointerPolicy.Blocked);

        AssertPassed(policy.OnPointer(Stroke(PointerMessage.Move), clip: null));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, button: PointerButton.Left), clip: null));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, button: PointerButton.Right), clip: null));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, button: PointerButton.Middle), clip: null));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, button: PointerButton.XButton1), clip: null));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, button: PointerButton.XButton2), clip: null));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.Wheel, wheelDelta: -120), clip: null));
    }

    [Fact]
    public void Blocked_row_in_osk_still_swallows_only_a_click_outside_the_clip()
    {
        var (policy, _) = Create(InputMode.Osk, null, PointerPolicy.Blocked, clipHwnd: 0x1234);

        AssertPassed(policy.OnPointer(Stroke(PointerMessage.Move, x: 5000, y: 5000), Clip));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, x: 50, y: 50, button: PointerButton.Left), Clip));
        AssertSwallowed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, x: 5000, y: 5000, button: PointerButton.Left), Clip));
    }

    [Theory]
    [InlineData(AppType.Moonlight)]
    [InlineData(AppType.Native)]
    public void Allowed_app_row_passes_the_whole_pointer_matrix(AppType appType)
    {
        var (policy, _) = Create(InputMode.App, appType, PointerPolicy.Allowed);

        AssertPassed(policy.OnPointer(Stroke(PointerMessage.Move), clip: null));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, x: 5000, y: 5000, button: PointerButton.Left), clip: null));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonUp, x: 5000, y: 5000, button: PointerButton.Left), clip: null));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.Wheel, wheelDelta: 120), clip: null));
    }

    [Fact]
    public void Osk_without_clip_passes_clicks_because_the_cursor_is_not_confined()
    {
        var (policy, _) = Create(InputMode.Osk, null, PointerPolicy.Allowed);

        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, x: 5000, y: 5000, button: PointerButton.Left), clip: null));
    }

    // ── Clip w osk ────────────────────────────────────────────────────────────

    [Fact]
    public void Click_inside_the_clip_rectangle_is_passed()
    {
        var (policy, _) = Create(InputMode.Osk, null, PointerPolicy.Allowed, clipHwnd: 0x1234);

        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, x: 99, y: 99, button: PointerButton.Left), Clip));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.ButtonUp, x: 0, y: 0, button: PointerButton.Left), Clip));
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(50, 100)]
    [InlineData(-1, 50)]
    [InlineData(50, -1)]
    public void Click_outside_the_clip_rectangle_is_swallowed(int x, int y)
    {
        var (policy, _) = Create(InputMode.Osk, null, PointerPolicy.Allowed, clipHwnd: 0x1234);

        AssertSwallowed(policy.OnPointer(Stroke(PointerMessage.ButtonDown, x, y, PointerButton.Left), Clip));
        AssertSwallowed(policy.OnPointer(Stroke(PointerMessage.ButtonUp, x, y, PointerButton.Left), Clip));
    }

    [Fact]
    public void Movement_and_wheel_are_passed_even_outside_the_clip_rectangle()
    {
        var (policy, _) = Create(InputMode.Osk, null, PointerPolicy.Allowed, clipHwnd: 0x1234);

        // Kursor i tak jest uwięziony przez ClipCursor, więc ruch i kółko nie wymagają połknięcia.
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.Move, x: 5000, y: 5000), Clip));
        AssertPassed(policy.OnPointer(Stroke(PointerMessage.Wheel, x: 5000, y: 5000, wheelDelta: 120), Clip));
    }

    [Fact]
    public void Clip_rectangle_excludes_its_right_and_bottom_edges()
    {
        Assert.True(Clip.Contains(0, 0));
        Assert.True(Clip.Contains(99, 99));
        Assert.False(Clip.Contains(100, 50));
        Assert.False(Clip.Contains(50, 100));
    }

    // ── Znacznik własnego wstrzyknięcia ───────────────────────────────────────

    [Fact]
    public void MediaStation_injected_pointer_event_is_passed_even_in_a_blocked_row()
    {
        var (policy, _) = Create(InputMode.Shell, null, PointerPolicy.Blocked);

        AssertPassed(policy.OnPointer(
            Stroke(PointerMessage.ButtonDown, button: PointerButton.Left, injected: true, extraInfo: InjectionMarker.MediaStation),
            clip: null));
    }

    [Fact]
    public void Foreign_injection_without_the_marker_is_passed_in_a_blocked_row()
    {
        var (policy, _) = Create(InputMode.Shell, null, PointerPolicy.Blocked);

        // Wskaźnik nie jest połykany, więc znacznik nie ma tu znaczenia; test pilnuje, że flaga `injected` nic nie zmienia.
        AssertPassed(policy.OnPointer(
            Stroke(PointerMessage.ButtonDown, button: PointerButton.Left, injected: true, extraInfo: 0),
            clip: null));
    }

    // ── Zmiana wiersza routingu ───────────────────────────────────────────────

    [Fact]
    public void Pointer_events_pass_before_and_after_a_routing_row_change()
    {
        var (policy, routing) = Create(InputMode.Shell, null, PointerPolicy.Blocked);
        var move = Stroke(PointerMessage.Move);
        var click = Stroke(PointerMessage.ButtonDown, button: PointerButton.Left);
        var wheel = Stroke(PointerMessage.Wheel, wheelDelta: 120);

        AssertPassed(policy.OnPointer(move, clip: null));
        AssertPassed(policy.OnPointer(click, clip: null));
        AssertPassed(policy.OnPointer(wheel, clip: null));

        routing.Apply(new SetInputRouting(2, InputMode.Osk, null, PointerPolicy.Allowed, 2));

        AssertPassed(policy.OnPointer(move, clip: null));
        AssertPassed(policy.OnPointer(click, clip: null));
        AssertPassed(policy.OnPointer(wheel, clip: null));
    }

    [Fact]
    public void Switching_from_osk_clip_to_shell_releases_the_clip_rule()
    {
        var (policy, routing) = Create(InputMode.Osk, null, PointerPolicy.Allowed, clipHwnd: 0x1234);
        var outside = Stroke(PointerMessage.ButtonDown, x: 5000, y: 5000, button: PointerButton.Left);

        AssertSwallowed(policy.OnPointer(outside, Clip));
        Assert.Equal(0x1234, PointerClip.WindowOf(routing.Current));

        routing.Apply(new SetInputRouting(2, InputMode.Shell, null, PointerPolicy.Allowed, 2));

        // Nowy wiersz nie ma clipu: hub woła ClipCursor(null), więc ten sam klik przechodzi.
        Assert.Null(PointerClip.WindowOf(routing.Current));
        AssertPassed(policy.OnPointer(outside, clip: null));
    }
}
