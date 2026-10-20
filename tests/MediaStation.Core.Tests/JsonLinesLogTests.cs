using System.Text.Json;
using MediaStation.Core.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

public class JsonLinesLogTests
{
    private sealed class CollectingSink : ILogLineSink
    {
        public List<(string File, string Line)> Lines { get; } = [];

        public void Append(string fileName, string line) => Lines.Add((fileName, line));
    }

    private static (JsonLinesLog Log, CollectingSink Sink, FakeTimeProvider Time) Create(DateTimeOffset start)
    {
        var time = new FakeTimeProvider(start);
        var sink = new CollectingSink();
        return (new JsonLinesLog(time, sink), sink, time);
    }

    private static JsonElement Parse(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Writes_compact_line_to_daily_file()
    {
        var (log, sink, _) = Create(new DateTimeOffset(2026, 10, 20, 21, 15, 3, 123, TimeSpan.Zero));

        log.Info("app", "start");

        var (file, line) = Assert.Single(sink.Lines);
        Assert.Equal("mediastation-2026-10-20.jsonl", file);
        Assert.Equal("""{"ts":"2026-10-20T21:15:03.123Z","level":"info","src":"app","msg":"start"}""", line);
    }

    [Fact]
    public void Rotates_to_next_file_at_utc_midnight()
    {
        var (log, sink, time) = Create(new DateTimeOffset(2026, 10, 20, 23, 59, 59, 900, TimeSpan.Zero));

        log.Info("app", "before");
        time.Advance(TimeSpan.FromMilliseconds(200));
        log.Info("app", "after");

        Assert.Collection(
            sink.Lines,
            l =>
            {
                Assert.Equal("mediastation-2026-10-20.jsonl", l.File);
                Assert.Equal("2026-10-20T23:59:59.900Z", Parse(l.Line).GetProperty("ts").GetString());
            },
            l =>
            {
                Assert.Equal("mediastation-2026-10-21.jsonl", l.File);
                Assert.Equal("2026-10-21T00:00:00.100Z", Parse(l.Line).GetProperty("ts").GetString());
            });
    }

    [Fact]
    public void File_and_ts_follow_utc_when_local_time_zone_differs()
    {
        // 00:30 lokalnie w UTC+2 = 22:30 UTC poprzedniego dnia.
        var (log, sink, time) = Create(new DateTimeOffset(2026, 10, 20, 22, 30, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2"));
        Assert.Equal(21, time.GetLocalNow().Day);

        log.Info("app", "start");

        var (file, line) = Assert.Single(sink.Lines);
        Assert.Equal("mediastation-2026-10-20.jsonl", file);
        Assert.Equal("2026-10-20T22:30:00.000Z", Parse(line).GetProperty("ts").GetString());
    }

    [Fact]
    public void File_name_uses_utc_date_of_offset_time()
    {
        var localPastMidnight = new DateTimeOffset(2026, 10, 21, 0, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal("mediastation-2026-10-20.jsonl", LogFileName.For(localPastMidnight));
    }

    [Fact]
    public void Writes_data_and_error_fields()
    {
        var (log, sink, _) = Create(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        Exception error;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException ex)
        {
            error = ex;
        }

        log.Error("shell", "failed", error, new Dictionary<string, object?> { ["exitCode"] = 3, ["args"] = new[] { "--a" }, ["none"] = null });

        var root = Parse(Assert.Single(sink.Lines).Line);
        Assert.Equal("error", root.GetProperty("level").GetString());
        Assert.Equal("shell", root.GetProperty("src").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("exitCode").GetInt32());
        Assert.Equal("--a", data.GetProperty("args")[0].GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("none").ValueKind);
        var err = root.GetProperty("err");
        Assert.Equal("System.InvalidOperationException", err.GetProperty("type").GetString());
        Assert.Equal("boom", err.GetProperty("message").GetString());
        Assert.Contains(nameof(Writes_data_and_error_fields), err.GetProperty("stack").GetString());
    }

    [Fact]
    public void Omits_data_and_error_when_null()
    {
        var (log, sink, _) = Create(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        log.Write(LogLevel.Warn, "app", "plain");

        var root = Parse(Assert.Single(sink.Lines).Line);
        Assert.Equal("warn", root.GetProperty("level").GetString());
        Assert.False(root.TryGetProperty("data", out _));
        Assert.False(root.TryGetProperty("err", out _));
    }

    [Fact]
    public void Newlines_in_message_and_stack_stay_on_one_line()
    {
        var (log, sink, _) = Create(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        Exception error;
        try
        {
            throw new InvalidOperationException("line1\nline2");
        }
        catch (InvalidOperationException ex)
        {
            error = ex;
        }

        log.Error("app", "multi\r\nline msg", error);

        var line = Assert.Single(sink.Lines).Line;
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        var root = Parse(line);
        Assert.Equal("multi\r\nline msg", root.GetProperty("msg").GetString());
        Assert.Contains("line1\nline2", root.GetProperty("err").GetProperty("stack").GetString());
    }

    [Fact]
    public void Polish_characters_round_trip()
    {
        var (log, sink, _) = Create(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        log.Info("app", "zażółć \"gęślą\" jaźń");

        Assert.Equal("zażółć \"gęślą\" jaźń", Parse(Assert.Single(sink.Lines).Line).GetProperty("msg").GetString());
    }

    [Fact]
    public void Every_level_serializes_as_camel_case_string_with_utc_ts()
    {
        var (log, sink, time) = Create(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        foreach (var level in Enum.GetValues<LogLevel>())
        {
            log.Write(level, "app", "x");
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var levels = sink.Lines.Select(l => Parse(l.Line)).Select(root =>
        {
            Assert.EndsWith("Z", root.GetProperty("ts").GetString());
            return root.GetProperty("level").GetString();
        });
        Assert.Equal(["debug", "info", "warn", "error"], levels);
    }

    [Fact]
    public void Unserializable_data_keeps_entry()
    {
        var (log, sink, _) = Create(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var cyclic = new Dictionary<string, object?>();
        cyclic["self"] = cyclic;

        log.Info("app", "cyclic", cyclic);

        var root = Parse(Assert.Single(sink.Lines).Line);
        Assert.Equal("cyclic", root.GetProperty("msg").GetString());
        Assert.True(root.GetProperty("data").TryGetProperty("serializationError", out _));
    }

    [Fact]
    public void Nan_in_data_keeps_entry()
    {
        var (log, sink, _) = Create(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        log.Info("app", "nan", new Dictionary<string, object?> { ["value"] = double.NaN });

        var root = Parse(Assert.Single(sink.Lines).Line);
        Assert.Equal("nan", root.GetProperty("msg").GetString());
        Assert.True(root.GetProperty("data").TryGetProperty("serializationError", out _));
    }
}
