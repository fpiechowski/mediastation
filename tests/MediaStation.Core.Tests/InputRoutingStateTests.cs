using MediaStation.Core.Contracts;
using MediaStation.Core.Input;

namespace MediaStation.Core.Tests;

/// <summary>
/// Lokalna kopia routingu (AD-8): stan początkowy <c>shell</c> z zablokowanym wskaźnikiem i <c>epoch</c> 0,
/// przyjęcie wyłącznie <c>epoch</c> ściśle większego (wszystkie pola naraz) oraz odrzucenie równego i starszego
/// bez zmiany kopii.
/// </summary>
public class InputRoutingStateTests
{
    [Fact]
    public void Initial_copy_is_shell_with_blocked_pointer_and_epoch_zero()
    {
        var routing = new InputRoutingState();

        Assert.Equal(new InputRouting(InputMode.Shell, null, PointerPolicy.Blocked, 0), routing.Current);
    }

    [Fact]
    public void Strictly_greater_epoch_replaces_every_field()
    {
        var routing = new InputRoutingState();

        var applied = routing.Apply(
            new SetInputRouting(7, InputMode.App, AppType.Moonlight, PointerPolicy.Allowed, 3));

        Assert.True(applied);
        Assert.Equal(new InputRouting(InputMode.App, AppType.Moonlight, PointerPolicy.Allowed, 3), routing.Current);
    }

    [Theory]
    [InlineData(3L)]
    [InlineData(2L)]
    public void Equal_or_older_epoch_is_rejected_without_changing_the_copy(long epoch)
    {
        var routing = new InputRoutingState();
        routing.Apply(new SetInputRouting(1, InputMode.App, AppType.Web, PointerPolicy.Allowed, 3));
        var before = routing.Current;

        var applied = routing.Apply(
            new SetInputRouting(2, InputMode.Shell, null, PointerPolicy.Blocked, epoch));

        Assert.False(applied);
        Assert.Equal(before, routing.Current);
    }

    [Fact]
    public void Successive_greater_epochs_replace_the_row_in_order()
    {
        var routing = new InputRoutingState();

        routing.Apply(new SetInputRouting(1, InputMode.App, AppType.Native, PointerPolicy.Allowed, 5));
        var applied = routing.Apply(new SetInputRouting(2, InputMode.Osk, null, PointerPolicy.Allowed, 8));

        Assert.True(applied);
        Assert.Equal(new InputRouting(InputMode.Osk, null, PointerPolicy.Allowed, 8), routing.Current);
    }

    [Fact]
    public void Clip_hwnd_is_copied_into_the_local_copy()
    {
        var routing = new InputRoutingState();

        routing.Apply(new SetInputRouting(1, InputMode.Osk, null, PointerPolicy.Allowed, 2, ClipHwnd: 0x1234));

        Assert.Equal(new InputRouting(InputMode.Osk, null, PointerPolicy.Allowed, 2, 0x1234), routing.Current);
    }

    [Fact]
    public void Accepted_row_raises_changed_once_with_the_applied_row()
    {
        var routing = new InputRoutingState();
        var changed = new List<InputRouting>();
        routing.Changed += changed.Add;

        routing.Apply(new SetInputRouting(1, InputMode.Osk, null, PointerPolicy.Allowed, 4, ClipHwnd: 99));

        var applied = Assert.Single(changed);
        Assert.Equal(new InputRouting(InputMode.Osk, null, PointerPolicy.Allowed, 4, 99), applied);
        Assert.Equal(routing.Current, applied);
    }

    [Theory]
    [InlineData(3L)]
    [InlineData(2L)]
    public void Rejected_epoch_does_not_raise_changed(long epoch)
    {
        var routing = new InputRoutingState();
        routing.Apply(new SetInputRouting(1, InputMode.Osk, null, PointerPolicy.Allowed, 3, ClipHwnd: 7));
        var raised = 0;
        routing.Changed += _ => raised++;

        routing.Apply(new SetInputRouting(2, InputMode.Shell, null, PointerPolicy.Blocked, epoch));

        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData(InputMode.Osk, 0x1234L, 0x1234L)]
    [InlineData(InputMode.Osk, 0L, null)]
    [InlineData(InputMode.Osk, null, null)]
    [InlineData(InputMode.Shell, 0x1234L, null)]
    [InlineData(InputMode.App, 0x1234L, null)]
    public void Pointer_clip_applies_only_to_osk_with_non_zero_window(InputMode mode, long? clipHwnd, long? expected)
    {
        var routing = new InputRouting(mode, null, PointerPolicy.Allowed, 1, clipHwnd);

        Assert.Equal(expected, PointerClip.WindowOf(routing));
    }
}
