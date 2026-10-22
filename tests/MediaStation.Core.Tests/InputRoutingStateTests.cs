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
}
