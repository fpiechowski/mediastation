using MediaStation.App.Platform.System;

namespace MediaStation.Core.Tests;

/// <summary>
/// Klasyfikacja <c>PBT_POWERSETTINGCHANGE</c> z bajtów <c>POWERBROADCAST_SETTING</c>: GUID, <c>DataLength</c>,
/// <c>Data[0]</c> i echo rejestracji. Fixture'y są zapisane literałami (offsety 16/20 i bajty GUID Win32),
/// żeby zły offset albo endianness w produkcji nie przechodził na zielono.
/// </summary>
public class DisplayStateNotificationTests
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

    [Fact]
    public void Display_on_with_dword_data_length_is_a_wake()
    {
        var payload = Payload(ConsoleDisplayStateBytes, DataLengthDword, data0: 1);

        Assert.True(DisplayStateNotification.IsWake(payload, ConsoleDisplayState, duringStartup: false));
    }

    [Theory]
    [InlineData(0)] // off
    [InlineData(2)] // dimmed
    public void Off_and_dimmed_display_are_not_wakes(byte data0)
    {
        var payload = Payload(ConsoleDisplayStateBytes, DataLengthDword, data0);

        Assert.False(DisplayStateNotification.IsWake(payload, ConsoleDisplayState, duringStartup: false));
    }

    [Fact]
    public void Other_power_setting_guid_is_not_a_wake()
    {
        var payload = Payload(OtherSettingBytes, DataLengthDword, data0: 1);

        Assert.False(DisplayStateNotification.IsWake(payload, ConsoleDisplayState, duringStartup: false));
    }

    [Fact]
    public void Zero_data_length_is_not_a_wake()
    {
        var payload = Payload(ConsoleDisplayStateBytes, DataLengthZero, data0: 1);

        Assert.False(DisplayStateNotification.IsWake(payload, ConsoleDisplayState, duringStartup: false));
    }

    [Fact]
    public void Data_length_other_than_a_dword_is_not_a_wake()
    {
        var payload = Payload(ConsoleDisplayStateBytes, DataLengthOne, data0: 1);

        Assert.False(DisplayStateNotification.IsWake(payload, ConsoleDisplayState, duringStartup: false));
    }

    [Fact]
    public void Payload_shorter_than_claimed_data_is_not_a_wake()
    {
        var full = Payload(ConsoleDisplayStateBytes, DataLengthDword, data0: 1);

        Assert.False(DisplayStateNotification.IsWake(full.AsSpan(0, 20), ConsoleDisplayState, duringStartup: false));
        Assert.False(DisplayStateNotification.IsWake(full.AsSpan(0, 23), ConsoleDisplayState, duringStartup: false));
        Assert.False(DisplayStateNotification.IsWake(ReadOnlySpan<byte>.Empty, ConsoleDisplayState, duringStartup: false));
    }

    [Fact]
    public void Registration_echo_during_startup_is_not_a_wake()
    {
        var payload = Payload(ConsoleDisplayStateBytes, DataLengthDword, data0: 1);

        Assert.False(DisplayStateNotification.IsWake(payload, ConsoleDisplayState, duringStartup: true));
    }

    private static byte[] Payload(byte[] setting, byte[] dataLength, byte data0)
    {
        var payload = new byte[PayloadLength];
        setting.CopyTo(payload, 0);
        dataLength.CopyTo(payload, DataLengthOffset);
        payload[DataOffset] = data0;
        return payload;
    }
}
