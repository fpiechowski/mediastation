using MediaStation.App.Platform.Input;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;

namespace MediaStation.Core.Tests;

/// <summary>
/// Stan clipu kursora (AD-8) bez Win32: wiersz <c>osk</c> z <c>clipHwnd</c> więzi kursor w prostokącie okna, każdy
/// inny wiersz zwalnia clip, a porażka <c>GetWindowRect</c>/<c>ClipCursor</c> zwalnia clip i zgłasza jedną porażkę do
/// <c>inputHubError {operation: clipCursor}</c> (szew <c>measure</c>/<c>apply</c>).
/// </summary>
public class ClipCursorGateTests
{
    /// <summary>Prostokąt okna klawiatury ekranowej: <c>(10,20)-(110,220)</c>.</summary>
    private static readonly ScreenRectangle Rect = new(10, 20, 110, 220);

    [Fact]
    public void Osk_row_measures_the_window_and_applies_the_clip()
    {
        var measured = new List<long>();
        var applied = new List<ScreenRectangle?>();
        var gate = new ClipCursorGate(
            handle =>
            {
                measured.Add(handle);
                return Rect;
            },
            rectangle =>
            {
                applied.Add(rectangle);
                return true;
            });

        var outcome = gate.Apply(Osk());

        Assert.Equal(ClipOutcome.Changed, outcome);
        Assert.Equal([0x1234L], measured);
        Assert.Equal([Rect], applied);
        Assert.Equal(Rect, gate.Current);
    }

    [Fact]
    public void Same_epoch_measures_and_clips_only_once()
    {
        var calls = 0;
        var gate = new ClipCursorGate(
            _ =>
            {
                calls++;
                return Rect;
            },
            _ =>
            {
                calls++;
                return true;
            });

        gate.Apply(Osk());
        var outcome = gate.Apply(Osk());

        Assert.Equal(ClipOutcome.Unchanged, outcome);
        Assert.Equal(2, calls);
        Assert.Equal(Rect, gate.Current);
    }

    [Fact]
    public void Shell_row_releases_the_previous_clip_once()
    {
        var applied = new List<ScreenRectangle?>();
        var gate = new ClipCursorGate(
            _ => Rect,
            rectangle =>
            {
                applied.Add(rectangle);
                return true;
            });

        gate.Apply(Osk());
        var outcome = gate.Apply(Shell());

        Assert.Equal(ClipOutcome.Changed, outcome);
        Assert.Equal([Rect, null], applied);
        Assert.Null(gate.Current);
    }

    [Fact]
    public void Row_without_clip_and_without_a_previous_clip_does_not_call_ClipCursor()
    {
        var applied = 0;
        var gate = new ClipCursorGate(
            _ => Rect,
            _ =>
            {
                applied++;
                return true;
            });

        var outcome = gate.Apply(Shell());

        Assert.Equal(ClipOutcome.Unchanged, outcome);
        Assert.Equal(0, applied);
        Assert.Null(gate.Current);
    }

    [Theory]
    [InlineData(InputMode.Osk, null)]
    [InlineData(InputMode.Osk, 0L)]
    [InlineData(InputMode.App, 0x1234L)]
    [InlineData(InputMode.Shell, 0x1234L)]
    public void Row_without_a_window_never_measures(InputMode mode, long? clipHwnd)
    {
        var measured = 0;
        var gate = new ClipCursorGate(
            _ =>
            {
                measured++;
                return Rect;
            },
            _ => true);

        var outcome = gate.Apply(new InputRouting(mode, AppType.Web, PointerPolicy.Allowed, 1, clipHwnd));

        Assert.Equal(ClipOutcome.Unchanged, outcome);
        Assert.Equal(0, measured);
        Assert.Null(gate.Current);
    }

    [Fact]
    public void Measure_failure_releases_the_clip_and_reports_failure()
    {
        var measured = true;
        var applied = new List<ScreenRectangle?>();
        var gate = new ClipCursorGate(
            _ => measured ? Rect : null,
            rectangle =>
            {
                applied.Add(rectangle);
                return true;
            });

        gate.Apply(Osk());
        measured = false;
        var outcome = gate.Apply(Osk(epoch: 2));

        Assert.Equal(ClipOutcome.Failed, outcome);
        Assert.Equal([Rect, null], applied);
        Assert.Null(gate.Current);
    }

    [Fact]
    public void ClipCursor_failure_leaves_no_rectangle_and_reports_failure()
    {
        var applied = new List<ScreenRectangle?>();
        var gate = new ClipCursorGate(
            _ => Rect,
            rectangle =>
            {
                applied.Add(rectangle);

                // Porażką jest tylko ustawienie prostokąta; zwolnienie clipu w porażce już się udaje.
                return rectangle is null;
            });

        var outcome = gate.Apply(Osk());

        Assert.Equal(ClipOutcome.Failed, outcome);
        Assert.Equal([Rect, null], applied);
        Assert.Null(gate.Current);
    }

    [Fact]
    public void Force_release_always_calls_ClipCursor()
    {
        var applied = new List<ScreenRectangle?>();
        var gate = new ClipCursorGate(
            _ => Rect,
            rectangle =>
            {
                applied.Add(rectangle);
                return true;
            });

        Assert.True(gate.ForceRelease());

        Assert.Equal([null], applied);
        Assert.Null(gate.Current);
    }

    [Fact]
    public void Force_release_reports_a_failure()
    {
        var gate = new ClipCursorGate(_ => Rect, _ => false);

        Assert.False(gate.ForceRelease());
        Assert.Null(gate.Current);
    }

    private static InputRouting Osk(long? clipHwnd = 0x1234, long epoch = 1) =>
        new(InputMode.Osk, null, PointerPolicy.Allowed, epoch, clipHwnd);

    private static InputRouting Shell(long epoch = 2) =>
        new(InputMode.Shell, null, PointerPolicy.Blocked, epoch);
}
