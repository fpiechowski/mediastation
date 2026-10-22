using System.Text.Json;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using MediaStation.Core.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Adapter tylko-log wejścia (AD-13): jedno <c>action {kind, phase, source}</c> na Action i jedno
/// <c>inputHubError {operation[, errorCode]}</c> na porażkę — bez <c>tsMono</c> i bez surowych kodów
/// (<c>vk</c>, <c>scan</c>, <c>usage</c>) w całej linii logu.
/// </summary>
public class LogActionSinkTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_delivered_action_yields_one_entry_with_kind_phase_and_source()
    {
        var log = new RecordingLog();
        var sink = new LogActionSink(log);

        sink.Deliver(new Action(ActionKind.Ok, ActionPhase.Press, ActionSource.Keyboard, 1234));
        sink.Deliver(new Action(ActionKind.Home, ActionPhase.Long, ActionSource.Remote, 5678));

        var entries = log.Entries;
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal(LogLevel.Info, entry.Level);
            Assert.Equal(LogActionSink.Src, entry.Src);
            Assert.Equal("action", entry.Msg);
            Assert.Equal(["kind", "phase", "source"], entry.Data!.Keys.ToArray());
        });

        Assert.Equal(ActionKind.Ok, entries[0].Data!["kind"]);
        Assert.Equal(ActionPhase.Press, entries[0].Data!["phase"]);
        Assert.Equal(ActionSource.Keyboard, entries[0].Data!["source"]);
        Assert.Equal(ActionKind.Home, entries[1].Data!["kind"]);
        Assert.Equal(ActionPhase.Long, entries[1].Data!["phase"]);
        Assert.Equal(ActionSource.Remote, entries[1].Data!["source"]);

        var data = FormatData(entries[0]);
        Assert.Equal(["kind", "phase", "source"], data.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("ok", data.GetProperty("kind").GetString());
        Assert.Equal("press", data.GetProperty("phase").GetString());
        Assert.Equal("keyboard", data.GetProperty("source").GetString());
    }

    [Fact]
    public void Action_entries_carry_no_monotonic_time_and_no_raw_key_codes()
    {
        var log = new RecordingLog();
        var sink = new LogActionSink(log);

        sink.Deliver(new Action(ActionKind.Back, ActionPhase.Press, ActionSource.Remote, 987_654_321));

        var entry = Assert.Single(log.Entries);
        var line = Line(entry);
        Assert.DoesNotContain("tsMono", line);
        Assert.DoesNotContain("\"vk\"", line);
        Assert.DoesNotContain("\"scan\"", line);
        Assert.DoesNotContain("\"usage\"", line);
        Assert.DoesNotContain("987654321", line);
    }

    [Fact]
    public void Error_report_logs_operation_and_error_code()
    {
        var log = new RecordingLog();
        var sink = new LogActionSink(log);

        sink.Report(new InputHubError(InputHubOperations.RawInput, 87));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(LogActionSink.Src, entry.Src);
        Assert.Equal("inputHubError", entry.Msg);

        var data = FormatData(entry);
        Assert.Equal(["operation", "errorCode"], data.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("rawInput", data.GetProperty("operation").GetString());
        Assert.Equal(87, data.GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public void Error_report_without_code_omits_the_error_code_key()
    {
        var log = new RecordingLog();
        var sink = new LogActionSink(log);

        sink.Report(new InputHubError(InputHubOperations.KeyboardHook, null));

        var entry = Assert.Single(log.Entries);
        Assert.Equal("inputHubError", entry.Msg);
        Assert.False(entry.Data!.ContainsKey("errorCode"));

        var data = FormatData(entry);
        Assert.Equal(["operation"], data.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("keyboardHook", data.GetProperty("operation").GetString());
        Assert.False(data.TryGetProperty("errorCode", out _));
    }

    [Fact]
    public void Mapped_enter_before_the_long_threshold_gives_exactly_one_press_entry()
    {
        var log = new RecordingLog();
        var sink = new LogActionSink(log);
        var time = new FakeTimeProvider(T0);
        var policy = Policy(time);

        Deliver(sink, policy.OnKey(new KeyStroke(0x0D, KeyTransition.Down, Injected: false, TimeMs: 1000)));
        Deliver(sink, policy.OnKey(new KeyStroke(0x0D, KeyTransition.Up, Injected: false, TimeMs: 1200)));

        var entry = Assert.Single(log.Entries);
        Assert.Equal("action", entry.Msg);
        Assert.Equal(ActionKind.Ok, entry.Data!["kind"]);
        Assert.Equal(ActionPhase.Press, entry.Data["phase"]);
        Assert.Equal(ActionSource.Keyboard, entry.Data["source"]);

        var line = Line(entry);
        Assert.DoesNotContain("\"vk\"", line);
        Assert.DoesNotContain("\"scan\"", line);
        Assert.DoesNotContain("\"usage\"", line);
    }

    [Fact]
    public void Mapped_enter_past_the_long_threshold_gives_exactly_one_long_entry_and_no_press()
    {
        var log = new RecordingLog();
        var sink = new LogActionSink(log);
        var time = new FakeTimeProvider(T0);
        var policy = Policy(time);

        Deliver(sink, policy.OnKey(new KeyStroke(0x0D, KeyTransition.Down, Injected: false, TimeMs: 1000)));
        time.Advance(TimeSpan.FromMilliseconds(600));
        Deliver(sink, policy.OnTimer(time.GetUtcNow()));
        Deliver(sink, policy.OnKey(new KeyStroke(0x0D, KeyTransition.Up, Injected: false, TimeMs: 1700)));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(ActionKind.Ok, entry.Data!["kind"]);
        Assert.Equal(ActionPhase.Long, entry.Data["phase"]);
        Assert.Equal(ActionSource.Keyboard, entry.Data["source"]);
    }

    private static InputPolicy Policy(TimeProvider time) => new(
        new InputConfig(
            [new InputDevice(
                "VID_1915&PID_1025",
                ActionSource.Remote,
                new Dictionary<string, ActionKind> { ["0x0D"] = ActionKind.Ok })],
            [GamepadButton.Back, GamepadButton.Start])
        {
            Timing = new InputTiming { LongPressMs = 600 },
        },
        time);

    private static void Deliver(LogActionSink sink, InputDecision decision)
    {
        foreach (var action in decision.Actions)
        {
            sink.Deliver(action);
        }
    }

    private static string Line(LogEntry entry) => JsonLinesLog.Format(T0, entry.Level, entry.Src, entry.Msg, entry.Data, null);

    private static JsonElement FormatData(LogEntry entry) => JsonDocument.Parse(Line(entry)).RootElement.GetProperty("data");
}
