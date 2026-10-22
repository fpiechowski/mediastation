using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using MediaStation.Core.Ports;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Kalibracja wejścia (AD-13, AD-14) na <see cref="FakeTimeProvider"/>: jedno naciśnięcie przycisku urządzenia
/// <c>remote</c> daje jeden wpis z surowymi kodami hooka, HID Consumer i <c>WM_INPUT</c>, zwykła klawiatura nie
/// trafia do logu, Esc i 60 s kończą tryb, a tryb nigdy nie połyka klawiszy.
/// </summary>
public class InputCalibrationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Ścieżka Raw Input dongla G20S Pro; config trzyma w niej sygnaturę <c>VID_1915&amp;PID_1025</c>.</summary>
    private const string DonglePath =
        @"\\?\HID#VID_1915&PID_1025&MI_01#7&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    /// <summary>Ścieżka zwykłej klawiatury; config ma ją jako <c>source: keyboard</c>.</summary>
    private const string KeyboardPath = @"\\?\HID#VID_046D&PID_C52B#1";

    private const string DongleSignature = "VID_1915&PID_1025";

    private static (InputCalibration Calibration, FakeTimeProvider Time, RecordingRawSink Sink) Create()
    {
        var time = new FakeTimeProvider(T0);
        var sink = new RecordingRawSink();
        return (new InputCalibration(Config(), time, sink), time, sink);
    }

    private static InputConfig Config() => new(
        [
            new InputDevice(
                DongleSignature,
                ActionSource.Remote,
                new Dictionary<string, ActionKind> { ["0x0D"] = ActionKind.Ok }),
            new InputDevice(
                "VID_046D&PID_C52B",
                ActionSource.Keyboard,
                new Dictionary<string, ActionKind> { ["0x41"] = ActionKind.Keyboard }),
        ],
        [GamepadButton.Back, GamepadButton.Start]);

    private static KeyStroke Down(int virtualKey, long timeMs, bool injected = false, int scan = 0x1C) =>
        new(virtualKey, KeyTransition.Down, injected, timeMs, scan);

    [Fact]
    public void Gamepad_snapshot_is_passed_through_without_an_entry()
    {
        var (calibration, _, sink) = Create();

        var decision = calibration.OnGamepad(
            new GamepadSnapshot(1000, [GamepadButton.DpadUp, GamepadButton.A]));

        Assert.False(decision.Swallow);
        Assert.Empty(decision.Actions);
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void Pointer_event_is_passed_through_without_an_entry()
    {
        var (calibration, _, sink) = Create();

        var decision = calibration.OnPointer(
            new PointerStroke(PointerMessage.ButtonDown, PointerButton.Left, 10, 10, 0, Injected: false, TimeMs: 1000),
            new ScreenRectangle(0, 0, 100, 100));

        Assert.False(decision.Swallow);
        Assert.Empty(decision.Actions);
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void Remote_press_with_hid_hook_and_raw_keyboard_yields_one_entry_with_all_fields()
    {
        var (calibration, time, sink) = Create();

        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));
        calibration.OnKey(Down(0x0D, 1005, injected: true));
        calibration.OnRawKeyboard(new RawKeyboardInput(DonglePath, 1010, 0x0D, 0x1C, Down: true));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(DongleSignature, entry.Signature);
        Assert.Equal(0x0D, entry.VirtualKey);
        Assert.Equal(0x1C, entry.Scan);
        Assert.Equal(0x0C, entry.UsagePage);
        Assert.Equal(0x00B0, entry.Usage);
        Assert.True(entry.Injected);
        Assert.True(entry.HasVk);
        Assert.Equal(5, entry.LlBeforeRawMs);
    }

    [Fact]
    public void Hook_before_the_device_event_is_attached_from_the_buffer()
    {
        var (calibration, time, sink) = Create();

        // Hook LL działa przed WM_INPUT: w chwili hooka nie znamy jeszcze urządzenia, więc zdarzenie czeka w buforze.
        calibration.OnKey(Down(0x0D, 1000, injected: true));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1004, 0x0C, 0x00B0));
        calibration.OnRawKeyboard(new RawKeyboardInput(DonglePath, 1010, 0x0D, 0x1C, Down: true));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(0x0D, entry.VirtualKey);
        Assert.True(entry.Injected);
        Assert.True(entry.HasVk);
        Assert.Equal(10, entry.LlBeforeRawMs);
    }

    [Fact]
    public void Hid_press_without_vk_yields_entry_with_null_vk_and_has_vk_false()
    {
        var (calibration, time, sink) = Create();

        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        var entry = Assert.Single(sink.Entries);
        Assert.Null(entry.VirtualKey);
        Assert.Null(entry.Scan);
        Assert.Null(entry.Injected);
        Assert.False(entry.HasVk);
        Assert.Null(entry.LlBeforeRawMs);
        Assert.Equal(0x00B0, entry.Usage);
    }

    [Fact]
    public void Ordinary_keyboard_and_its_hook_never_produce_an_entry()
    {
        var (calibration, time, sink) = Create();

        calibration.OnKey(Down(0x41, 1000, injected: false, scan: 0x1E));
        calibration.OnRawKeyboard(new RawKeyboardInput(KeyboardPath, 1005, 0x41, 0x1E, Down: true));

        time.Advance(TimeSpan.FromSeconds(InputCalibration.DurationSeconds));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void Keyboard_outside_the_config_and_its_hook_never_produce_an_entry()
    {
        var (calibration, time, sink) = Create();

        calibration.OnRawKeyboard(new RawKeyboardInput(@"\\?\HID#VID_1234&PID_5678#1", 1000, 0x72, 0x3D, Down: true));
        calibration.OnKey(Down(0x72, 1000, injected: false, scan: 0x3D));

        time.Advance(TimeSpan.FromSeconds(InputCalibration.DurationSeconds));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void Hook_outside_the_merge_window_is_not_attached()
    {
        var (calibration, time, sink) = Create();

        calibration.OnKey(Down(0x0D, 900, injected: true));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        var entry = Assert.Single(sink.Entries);
        Assert.Null(entry.VirtualKey);
        Assert.False(entry.HasVk);
    }

    [Fact]
    public void Hid_usage_zero_is_a_release_and_never_produces_an_entry()
    {
        var (calibration, time, sink) = Create();

        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1010, 0x0C, 0x00B0));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());
        Assert.Single(sink.Entries);

        // Zwolnienie przycisku (usage 0) nie tworzy wpisu i pozwala zalogować kolejne naciśnięcie tego samego usage.
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 2000, 0x0C, 0x0000));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 2010, 0x0C, 0x00B0));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Equal(2, sink.Entries.Count);
    }

    [Fact]
    public void Repeated_hid_usage_while_held_is_deduplicated()
    {
        var (calibration, time, sink) = Create();

        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1020, 0x0C, 0x00B0));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1040, 0x0C, 0x00B0));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Single(sink.Entries);
    }

    [Fact]
    public void New_press_after_the_window_closes_the_previous_entry()
    {
        var (calibration, time, sink) = Create();

        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1100, 0x0C, 0x00B1));
        Assert.Single(sink.Entries);

        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Equal(2, sink.Entries.Count);
        Assert.Equal(0x00B0, sink.Entries[0].Usage);
        Assert.Equal(0x00B1, sink.Entries[1].Usage);
    }

    [Fact]
    public void Raw_keyboard_press_without_hid_yields_vk_entry_without_ll_before_raw()
    {
        var (calibration, time, sink) = Create();

        calibration.OnRawKeyboard(new RawKeyboardInput(DonglePath, 1000, 0x0D, 0x1C, Down: true));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(0x0D, entry.VirtualKey);
        Assert.True(entry.HasVk);
        Assert.Null(entry.Injected);
        Assert.Null(entry.LlBeforeRawMs);
    }

    [Fact]
    public void Repeated_raw_keyboard_down_while_held_is_deduplicated()
    {
        var (calibration, time, sink) = Create();

        calibration.OnRawKeyboard(new RawKeyboardInput(DonglePath, 1000, 0x0D, 0x1C, Down: true));
        calibration.OnRawKeyboard(new RawKeyboardInput(DonglePath, 1020, 0x0D, 0x1C, Down: true));
        calibration.OnRawKeyboard(new RawKeyboardInput(DonglePath, 1040, 0x0D, 0x1C, Down: true));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Single(sink.Entries);
    }

    [Fact]
    public void Key_up_releases_the_markers_so_the_next_press_is_logged_again()
    {
        var (calibration, time, sink) = Create();

        calibration.OnRawKeyboard(new RawKeyboardInput(DonglePath, 1000, 0x0D, 0x1C, Down: true));
        calibration.OnKey(new KeyStroke(0x0D, KeyTransition.Up, Injected: true, TimeMs: 1050, Scan: 0x1C));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        calibration.OnRawKeyboard(new RawKeyboardInput(DonglePath, 2000, 0x0D, 0x1C, Down: true));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Equal(2, sink.Entries.Count);
    }

    [Fact]
    public void Physical_escape_ends_the_mode_without_swallowing_and_injected_escape_does_not()
    {
        var (calibration, time, _) = Create();
        var completed = 0;
        calibration.Completed += () => completed++;

        var injected = calibration.OnKey(new KeyStroke(0x1B, KeyTransition.Down, Injected: true, TimeMs: 1000, Scan: 0x01));
        Assert.False(injected.Swallow);
        Assert.Empty(injected.Actions);
        Assert.Equal(0, completed);

        var physical = calibration.OnKey(new KeyStroke(0x1B, KeyTransition.Down, Injected: false, TimeMs: 1100, Scan: 0x01));
        Assert.False(physical.Swallow);
        Assert.Empty(physical.Actions);
        Assert.Equal(1, completed);
        Assert.Null(calibration.NextDeadline);

        // Po zakończeniu kolejne zdarzenia nic nie zmieniają i nadal nie są połykane.
        Assert.False(calibration.OnKey(Down(0x0D, 1200)).Swallow);
        calibration.OnTimer(time.GetUtcNow().AddSeconds(InputCalibration.DurationSeconds));
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Mode_ends_after_sixty_seconds_without_escape()
    {
        var (calibration, time, _) = Create();
        var completed = 0;
        calibration.Completed += () => completed++;

        Assert.Equal(InputCalibration.DurationSeconds, 60);
        Assert.Equal(T0.AddSeconds(InputCalibration.DurationSeconds), calibration.NextDeadline);

        time.Advance(TimeSpan.FromSeconds(InputCalibration.DurationSeconds - 1));
        calibration.OnTimer(time.GetUtcNow());
        Assert.Equal(0, completed);

        time.Advance(TimeSpan.FromSeconds(1));
        var decision = calibration.OnTimer(time.GetUtcNow());

        Assert.False(decision.Swallow);
        Assert.Empty(decision.Actions);
        Assert.Equal(1, completed);
        Assert.Null(calibration.NextDeadline);
    }

    [Fact]
    public void Open_entry_is_flushed_before_the_mode_ends()
    {
        var (calibration, time, sink) = Create();

        // Naciśnięcie na chwilę przed końcem trybu nie może zginąć: timer końca domyka wpis i dopiero potem kończy.
        time.Advance(TimeSpan.FromSeconds(InputCalibration.DurationSeconds - 1));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));
        var completed = 0;
        calibration.Completed += () => completed++;

        time.Advance(TimeSpan.FromSeconds(1));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Single(sink.Entries);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Two_different_buttons_in_the_merge_window_give_two_entries()
    {
        var (calibration, time, sink) = Create();

        // Dwa różne usage w oknie 50 ms to dwa przyciski: scalenie ich zgubiłoby jeden z logu.
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1010, 0x0C, 0x00B1));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Equal(2, sink.Entries.Count);
        Assert.Equal(0x00B0, sink.Entries[0].Usage);
        Assert.Equal(0x00B1, sink.Entries[1].Usage);
    }

    [Fact]
    public void Key_up_of_the_ordinary_keyboard_does_not_clear_the_held_remote_usage()
    {
        var (calibration, time, sink) = Create();

        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());
        Assert.Single(sink.Entries);

        // Klawisz zwykłej klawiatury: jego key-up nie należy do przycisku pilota, więc nie zdejmuje usage.
        calibration.OnKey(new KeyStroke(0x41, KeyTransition.Down, Injected: false, TimeMs: 1050, Scan: 0x1E));
        calibration.OnKey(new KeyStroke(0x41, KeyTransition.Up, Injected: false, TimeMs: 1100, Scan: 0x1E));

        // Powtórzony raport HID trzymanego przycisku pilota nadal jest deduplikowany.
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1200, 0x0C, 0x00B0));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Single(sink.Entries);
    }

    [Fact]
    public void Next_deadline_is_the_earlier_of_the_press_flush_and_the_mode_end()
    {
        var (calibration, time, _) = Create();

        Assert.Equal(T0.AddSeconds(InputCalibration.DurationSeconds), calibration.NextDeadline);

        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));

        Assert.Equal(T0.AddMilliseconds(InputCalibration.MergeWindowMs), calibration.NextDeadline);

        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Equal(T0.AddSeconds(InputCalibration.DurationSeconds), calibration.NextDeadline);
    }

    [Fact]
    public void Non_injected_key_down_is_not_attached_to_a_remote_hid_press()
    {
        var (calibration, time, sink) = Create();

        // Litera z fizycznej klawiatury w oknie raportu pilota: nie jest wstrzyknięta, więc nie może zostać
        // vk/scan przycisku pilota ani zmienić hasVk (AD-13).
        calibration.OnKey(Down(0x41, 1000, injected: false, scan: 0x1E));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1005, 0x0C, 0x00B0));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        var entry = Assert.Single(sink.Entries);
        Assert.Null(entry.VirtualKey);
        Assert.Null(entry.Scan);
        Assert.False(entry.HasVk);
    }

    [Fact]
    public void Press_in_the_last_merge_window_is_flushed_when_the_mode_ends()
    {
        var (calibration, time, sink) = Create();
        var completed = 0;
        calibration.Completed += () => completed++;

        // Naciśnięcie 10 ms przed końcem: jego flushAt wypada już po terminie trybu.
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.DurationSeconds * 1000 - 10));
        calibration.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00B0));

        time.Advance(TimeSpan.FromMilliseconds(10));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Single(sink.Entries);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Raw_keyboard_event_without_vk_produces_no_entry()
    {
        var (calibration, time, sink) = Create();

        // Hub oddaje VK 0, gdy ciało RAWKEYBOARD jest nieczytelne; taki key-down nie jest przyciskiem pilota.
        calibration.OnRawKeyboard(new RawKeyboardInput(DonglePath, 1000, VirtualKey: 0, Scan: 0, Down: true));
        time.Advance(TimeSpan.FromMilliseconds(InputCalibration.MergeWindowMs));
        calibration.OnTimer(time.GetUtcNow());

        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        var time = new FakeTimeProvider(T0);

        Assert.Throws<ArgumentNullException>(() => new InputCalibration(null!, time, new RecordingRawSink()));
        Assert.Throws<ArgumentNullException>(() => new InputCalibration(Config(), null!, new RecordingRawSink()));
        Assert.Throws<ArgumentNullException>(() => new InputCalibration(Config(), time, null!));
    }

    /// <summary>Sink testowy kalibracji: zapisuje wpisy w kolejności dostarczenia.</summary>
    private sealed class RecordingRawSink : IRawInputSink
    {
        public List<RawInputEntry> Entries { get; } = [];

        public void Deliver(RawInputEntry entry) => Entries.Add(entry);
    }
}
