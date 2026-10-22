using System.Text.Json;
using MediaStation.Core.Input;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>
/// Adapter tylko-log kalibracji (AD-13): jedno <c>rawInput {signature, vk, scan, usagePage, usage, injected,
/// hasVk, llBeforeRawMs}</c> na naciśnięcie, kody jako <c>0x..</c>/<c>0x....</c>, brakujące źródła jako
/// <c>null</c> przy stałym kształcie ośmiu kluczy. Porażki huba idą jak w <see cref="LogActionSink"/>.
/// </summary>
public class LogRawInputSinkTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entry_has_eight_keys_in_pinned_order_and_hex_codes()
    {
        var log = new RecordingLog();
        var sink = new LogRawInputSink(log);

        sink.Deliver(new RawInputEntry(
            "VID_1915&PID_1025",
            VirtualKey: 0x0D,
            Scan: 0x1C,
            UsagePage: 0x0C,
            Usage: 0x00B0,
            Injected: true,
            HasVk: true,
            LlBeforeRawMs: 5));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal(LogRawInputSink.Src, entry.Src);
        Assert.Equal("rawInput", entry.Msg);
        Assert.Equal(
            ["signature", "vk", "scan", "usagePage", "usage", "injected", "hasVk", "llBeforeRawMs"],
            entry.Data!.Keys.ToArray());

        var data = FormatData(entry);
        Assert.Equal(
            ["signature", "vk", "scan", "usagePage", "usage", "injected", "hasVk", "llBeforeRawMs"],
            data.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("VID_1915&PID_1025", data.GetProperty("signature").GetString());
        Assert.Equal("0x0D", data.GetProperty("vk").GetString());
        Assert.Equal("0x1C", data.GetProperty("scan").GetString());
        Assert.Equal("0x000C", data.GetProperty("usagePage").GetString());
        Assert.Equal("0x00B0", data.GetProperty("usage").GetString());
        Assert.True(data.GetProperty("injected").GetBoolean());
        Assert.True(data.GetProperty("hasVk").GetBoolean());
        Assert.Equal(5, data.GetProperty("llBeforeRawMs").GetInt32());
    }

    [Fact]
    public void Entry_without_vk_keeps_all_keys_and_null_values()
    {
        var log = new RecordingLog();
        var sink = new LogRawInputSink(log);

        sink.Deliver(new RawInputEntry(
            "VID_1915&PID_1025",
            VirtualKey: null,
            Scan: null,
            UsagePage: 0x0C,
            Usage: 0x00B0,
            Injected: null,
            HasVk: false,
            LlBeforeRawMs: null));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(
            ["signature", "vk", "scan", "usagePage", "usage", "injected", "hasVk", "llBeforeRawMs"],
            entry.Data!.Keys.ToArray());
        Assert.Null(entry.Data["vk"]);
        Assert.False((bool)entry.Data["hasVk"]!);

        var data = FormatData(entry);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("vk").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("scan").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("injected").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("llBeforeRawMs").ValueKind);
        Assert.Equal("0x00B0", data.GetProperty("usage").GetString());
    }

    [Fact]
    public void Negative_ll_before_raw_means_hook_after_raw_input()
    {
        var log = new RecordingLog();
        var sink = new LogRawInputSink(log);

        sink.Deliver(new RawInputEntry("VID_1915&PID_1025", null, null, null, null, false, false, -12));

        var data = FormatData(Assert.Single(log.Entries));
        Assert.Equal(-12, data.GetProperty("llBeforeRawMs").GetInt32());
    }

    [Fact]
    public void Error_report_logs_operation_and_error_code()
    {
        var log = new RecordingLog();
        var sink = new LogRawInputSink(log);

        sink.Report(new InputHubError(InputHubOperations.RawInput, 87));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(LogRawInputSink.Src, entry.Src);
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
        var sink = new LogRawInputSink(log);

        sink.Report(new InputHubError(InputHubOperations.KeyboardHook, null));

        var entry = Assert.Single(log.Entries);
        Assert.Equal("inputHubError", entry.Msg);
        Assert.False(entry.Data!.ContainsKey("errorCode"));

        var data = FormatData(entry);
        Assert.Equal(["operation"], data.EnumerateObject().Select(property => property.Name).ToArray());
    }

    private static string Line(LogEntry entry) => JsonLinesLog.Format(T0, entry.Level, entry.Src, entry.Msg, entry.Data, null);

    private static JsonElement FormatData(LogEntry entry) => JsonDocument.Parse(Line(entry)).RootElement.GetProperty("data");
}
