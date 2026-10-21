using MediaStation.App.Platform.System;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Księgowość adaptera: <c>powerSignal</c> na każdego kandydata, <c>systemResumed</c> i Event tylko dla
/// zwycięzcy, <c>powerSignalsRegistered</c> po starcie, no-op po <c>Dispose</c>.
/// </summary>
public class PowerSignalReporterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Winner_logs_power_signal_and_system_resumed_and_posts_event()
    {
        var (reporter, log, posted, _) = Create();

        reporter.Report(WakeSignalGate.ResumeAutomaticSource);

        Assert.Equal(["powerSignal", "systemResumed"], log.Messages());
        Assert.All(log.Entries, entry => Assert.Equal(PowerSignalReporter.Src, entry.Src));
        Assert.Equal(WakeSignalGate.ResumeAutomaticSource, log.Entries[0].Data!["source"]);
        Assert.Equal(WakeSignalGate.ResumeAutomaticSource, log.Entries[1].Data!["source"]);
        var resumed = Assert.IsType<SystemResumed>(Assert.Single(posted));
        Assert.Equal(WakeSignalGate.ResumeAutomaticSource, resumed.Source);
    }

    [Fact]
    public void Power_mode_changed_only_logs_power_signal_and_posts_nothing()
    {
        var (reporter, log, posted, _) = Create();

        reporter.Report("PowerModeChanged");

        Assert.Equal(["powerSignal"], log.Messages());
        Assert.Empty(posted);
    }

    [Fact]
    public void Duplicate_within_five_seconds_is_logged_but_not_reported_as_resume()
    {
        var (reporter, log, posted, time) = Create();

        reporter.Report(WakeSignalGate.ResumeAutomaticSource);
        time.Advance(TimeSpan.FromSeconds(1));
        reporter.Report(WakeSignalGate.ResumeAutomaticSource);

        Assert.Equal(["powerSignal", "systemResumed", "powerSignal"], log.Messages());
        Assert.Single(posted);
    }

    [Fact]
    public void Burst_of_three_candidates_yields_one_resume_and_one_event()
    {
        var (reporter, log, posted, time) = Create();

        reporter.Report(WakeSignalGate.ResumeAutomaticSource);
        time.Advance(TimeSpan.FromSeconds(1));
        reporter.Report(WakeSignalGate.DisplayStateSource);
        time.Advance(TimeSpan.FromSeconds(1));
        reporter.Report(WakeSignalGate.ResumeAutomaticSource);

        Assert.Equal(3, log.Entries.Count(entry => entry.Msg == "powerSignal"));
        Assert.Single(log.Entries, entry => entry.Msg == "systemResumed");
        Assert.Single(posted);
    }

    [Fact]
    public void Signal_after_dispose_is_ignored()
    {
        var (reporter, log, posted, _) = Create();
        reporter.Dispose();

        reporter.Report(WakeSignalGate.ResumeAutomaticSource);

        Assert.Empty(log.Entries);
        Assert.Empty(posted);
    }

    [Fact]
    public void Complete_registration_is_logged_as_info_with_all_three_sources()
    {
        var (reporter, log, _, _) = Create();

        reporter.ReportRegistration(displayState: true, windowHook: true, powerModeChanged: true);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal("powerSignalsRegistered", entry.Msg);
        Assert.Equal(true, entry.Data!["displayState"]);
        Assert.Equal(true, entry.Data!["windowHook"]);
        Assert.Equal(true, entry.Data!["powerModeChanged"]);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Incomplete_registration_is_logged_as_warn(bool displayState, bool windowHook, bool powerModeChanged)
    {
        var (reporter, log, _, _) = Create();

        reporter.ReportRegistration(displayState, windowHook, powerModeChanged);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("powerSignalsRegistered", entry.Msg);
    }

    private static (PowerSignalReporter Reporter, RecordingLog Log, List<IOrchestratorMessage> Posted, FakeTimeProvider Time) Create()
    {
        var log = new RecordingLog();
        var posted = new List<IOrchestratorMessage>();
        var time = new FakeTimeProvider(T0);
        var reporter = new PowerSignalReporter(log, new WakeSignalGate(time), posted.Add);
        return (reporter, log, posted, time);
    }
}
