using MediaStation.App.Platform.System;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>
/// Decyzja adaptera power wydzielona z shimu Win32: okno echa rejestracji <c>GUID_CONSOLE_DISPLAY_STATE</c>
/// i mapowanie trzech źródeł na <see cref="PowerSignalReporter"/>. Fixture'y payloadu są zapisane literałami
/// (offsety 16/20 i bajty GUID Win32), tak jak w <see cref="DisplayStateNotificationTests"/>.
/// </summary>
public class PowerSignalRouterTests
{
    /// <summary>GUID_CONSOLE_DISPLAY_STATE {6FE69556-704A-47A0-8F24-C28D936FDA47} w układzie Win32.</summary>
    private static readonly byte[] ConsoleDisplayStateBytes =
        [0x56, 0x95, 0xE6, 0x6F, 0x4A, 0x70, 0xA0, 0x47, 0x8F, 0x24, 0xC2, 0x8D, 0x93, 0x6F, 0xDA, 0x47];

    private static readonly byte[] OtherSettingBytes =
        [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    private const int DataLengthOffset = 16;

    private const int DataOffset = 20;

    private const int PayloadLength = DataOffset + 4;

    private static readonly byte[] DataLengthDword = [0x04, 0x00, 0x00, 0x00];

    private static readonly byte[] DataLengthZero = [0x00, 0x00, 0x00, 0x00];

    private static readonly byte[] DataLengthOne = [0x01, 0x00, 0x00, 0x00];

    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Display_state_echo_inside_the_registration_window_is_ignored()
    {
        var (router, log, posted, _) = Create();
        router.BeginRegistration();

        router.OnDisplayStateChanged(Payload(ConsoleDisplayStateBytes, DataLengthDword, data0: 1), ConsoleDisplayState);

        Assert.Empty(log.Entries);
        Assert.Empty(posted);
    }

    [Fact]
    public void Display_state_after_registration_logs_and_posts_one_resume()
    {
        var (router, log, posted, _) = Create();
        router.BeginRegistration();
        router.CompleteRegistration(displayState: true, windowHook: true, powerModeChanged: true);

        router.OnDisplayStateChanged(Payload(ConsoleDisplayStateBytes, DataLengthDword, data0: 1), ConsoleDisplayState);

        Assert.Equal(["powerSignalsRegistered", "powerSignal", "systemResumed"], log.Messages());
        Assert.Equal(WakeSignalGate.DisplayStateSource, log.Entries[1].Data!["source"]);
        Assert.Equal(WakeSignalGate.DisplayStateSource, log.Entries[2].Data!["source"]);
        var resumed = Assert.IsType<SystemResumed>(Assert.Single(posted));
        Assert.Equal(WakeSignalGate.DisplayStateSource, resumed.Source);
    }

    [Fact]
    public void Resume_automatic_inside_the_registration_window_still_wins()
    {
        var (router, log, posted, _) = Create();
        router.BeginRegistration();

        router.OnResumeAutomatic();

        Assert.Equal(["powerSignal", "systemResumed"], log.Messages());
        Assert.Equal(WakeSignalGate.ResumeAutomaticSource, log.Entries[0].Data!["source"]);
        Assert.Equal(WakeSignalGate.ResumeAutomaticSource, log.Entries[1].Data!["source"]);
        var resumed = Assert.IsType<SystemResumed>(Assert.Single(posted));
        Assert.Equal(WakeSignalGate.ResumeAutomaticSource, resumed.Source);
    }

    [Fact]
    public void Power_mode_resume_after_registration_only_logs_a_candidate()
    {
        var (router, log, posted, _) = Create();
        router.BeginRegistration();
        router.CompleteRegistration(displayState: true, windowHook: true, powerModeChanged: true);

        router.OnPowerModeResumed();

        Assert.Equal(["powerSignalsRegistered", "powerSignal"], log.Messages());
        Assert.Equal(PowerSignalRouter.PowerModeChangedSource, log.Entries[1].Data!["source"]);
        Assert.Empty(posted);
    }

    [Fact]
    public void Power_mode_resume_logs_the_wire_source_literal()
    {
        // Wartość sznurka jest kontraktem logu, więc test pinuje ją literałem, a nie stałą routera.
        var (router, log, _, _) = Create();

        router.OnPowerModeResumed();

        var entry = Assert.Single(log.Entries);
        Assert.Equal("powerSignal", entry.Msg);
        Assert.Equal("PowerModeChanged", entry.Data!["source"]);
    }

    [Theory]
    [InlineData(0)] // off
    [InlineData(2)] // dimmed
    public void Display_off_or_dimmed_after_registration_is_ignored(byte data0)
    {
        var (router, log, posted, _) = Create();
        router.BeginRegistration();
        router.CompleteRegistration(displayState: true, windowHook: true, powerModeChanged: true);

        router.OnDisplayStateChanged(Payload(ConsoleDisplayStateBytes, DataLengthDword, data0), ConsoleDisplayState);

        Assert.Equal(["powerSignalsRegistered"], log.Messages());
        Assert.Empty(posted);
    }

    [Fact]
    public void Other_power_setting_guid_after_registration_is_ignored()
    {
        var (router, log, posted, _) = Create();
        router.BeginRegistration();
        router.CompleteRegistration(displayState: true, windowHook: true, powerModeChanged: true);

        router.OnDisplayStateChanged(Payload(OtherSettingBytes, DataLengthDword, data0: 1), ConsoleDisplayState);

        Assert.Equal(["powerSignalsRegistered"], log.Messages());
        Assert.Empty(posted);
    }

    [Fact]
    public void Zero_data_length_after_registration_is_ignored()
    {
        var (router, log, posted, _) = Create();
        router.BeginRegistration();
        router.CompleteRegistration(displayState: true, windowHook: true, powerModeChanged: true);

        router.OnDisplayStateChanged(Payload(ConsoleDisplayStateBytes, DataLengthZero, data0: 1), ConsoleDisplayState);

        Assert.Equal(["powerSignalsRegistered"], log.Messages());
        Assert.Empty(posted);
    }

    [Fact]
    public void Data_length_other_than_a_dword_after_registration_is_ignored()
    {
        var (router, log, posted, _) = Create();
        router.BeginRegistration();
        router.CompleteRegistration(displayState: true, windowHook: true, powerModeChanged: true);

        router.OnDisplayStateChanged(Payload(ConsoleDisplayStateBytes, DataLengthOne, data0: 1), ConsoleDisplayState);

        Assert.Equal(["powerSignalsRegistered"], log.Messages());
        Assert.Empty(posted);
    }

    [Fact]
    public void Payload_shorter_than_claimed_data_after_registration_is_ignored()
    {
        var (router, log, posted, _) = Create();
        router.BeginRegistration();
        router.CompleteRegistration(displayState: true, windowHook: true, powerModeChanged: true);
        var full = Payload(ConsoleDisplayStateBytes, DataLengthDword, data0: 1);

        router.OnDisplayStateChanged(full.AsSpan(0, 20), ConsoleDisplayState);
        router.OnDisplayStateChanged(full.AsSpan(0, 23), ConsoleDisplayState);
        router.OnDisplayStateChanged(ReadOnlySpan<byte>.Empty, ConsoleDisplayState);

        Assert.Equal(["powerSignalsRegistered"], log.Messages());
        Assert.Empty(posted);
    }

    [Fact]
    public void Complete_registration_with_all_three_sources_is_logged_as_info()
    {
        var (router, log, _, _) = Create();
        router.BeginRegistration();

        router.CompleteRegistration(displayState: true, windowHook: true, powerModeChanged: true);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal("powerSignalsRegistered", entry.Msg);
        Assert.Equal(true, entry.Data!["displayState"]);
        Assert.Equal(true, entry.Data!["windowHook"]);
        Assert.Equal(true, entry.Data!["powerModeChanged"]);
    }

    [Fact]
    public void Incomplete_registration_is_logged_as_warn_with_all_three_flags()
    {
        var (router, log, _, _) = Create();
        router.BeginRegistration();

        router.CompleteRegistration(displayState: false, windowHook: true, powerModeChanged: true);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("powerSignalsRegistered", entry.Msg);
        Assert.Equal(false, entry.Data!["displayState"]);
        Assert.Equal(true, entry.Data!["windowHook"]);
        Assert.Equal(true, entry.Data!["powerModeChanged"]);
    }

    private static byte[] Payload(byte[] setting, byte[] dataLength, byte data0)
    {
        var payload = new byte[PayloadLength];
        setting.CopyTo(payload, 0);
        dataLength.CopyTo(payload, DataLengthOffset);
        payload[DataOffset] = data0;
        return payload;
    }

    private static (PowerSignalRouter Router, RecordingLog Log, List<IOrchestratorMessage> Posted, FakeTimeProvider Time) Create()
    {
        var log = new RecordingLog();
        var posted = new List<IOrchestratorMessage>();
        var time = new FakeTimeProvider(T0);
        var reporter = new PowerSignalReporter(log, new WakeSignalGate(time), posted.Add);
        return (new PowerSignalRouter(reporter), log, posted, time);
    }
}
