using System.Diagnostics;
using MediaStation.App.Diagnostics;
using MediaStation.Core.Diagnostics;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>
/// Zawieszenie UI na żądanie (S10): parser zmiennej w Core bez I/O i bez wyjątków oraz jeden wpis
/// <c>warn debugHangUi {seconds}</c> i blokada wątku w <c>UiHang</c>, wykonywane dopiero na dispatcherze.
/// </summary>
public class DebugHangUiTests
{
    [Fact]
    public void Environment_variable_name_matches_the_documented_one()
    {
        Assert.Equal("MEDIASTATION_DEBUG_HANG_UI", DebugHangUi.EnvVarName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e12")]
    [InlineData("2147483.648")]
    [InlineData("600s")]
    public void Missing_or_invalid_value_never_schedules_a_hang(string? value)
    {
        Assert.Null(DebugHangUi.Parse(value));
    }

    [Theory]
    [InlineData("600", 600)]
    [InlineData("0.5", 0.5)]
    [InlineData(" 600 ", 600)]
    [InlineData("2147483.646", 2147483.646)]
    [InlineData("2147483.647", 2147483.647)]
    public void Valid_value_is_returned_in_seconds(string value, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), DebugHangUi.Parse(value));
    }

    [Fact]
    public void Schedule_posts_one_callback_that_logs_the_entry_and_blocks_the_posted_thread()
    {
        var log = new RecordingLog();
        var posted = new List<System.Action>();

        UiHang.Schedule(TimeSpan.FromMilliseconds(200), log, posted.Add);

        var action = Assert.Single(posted);
        var watch = Stopwatch.StartNew();
        action();
        watch.Stop();

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal(UiHang.Src, entry.Src);
        Assert.Equal("debugHangUi", entry.Msg);
        Assert.NotNull(entry.Data);
        Assert.Equal(TimeSpan.FromMilliseconds(200).TotalSeconds, Assert.IsType<double>(entry.Data!["seconds"]));
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(100), $"Blocked only {watch.Elapsed}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_hang_never_posts_and_never_logs(int milliseconds)
    {
        var log = new RecordingLog();
        var posted = 0;

        UiHang.Schedule(TimeSpan.FromMilliseconds(milliseconds), log, _ => posted++);

        Assert.Equal(0, posted);
        Assert.Empty(log.Entries);
    }
}
