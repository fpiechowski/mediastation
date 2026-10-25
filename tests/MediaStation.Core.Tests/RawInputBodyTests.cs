using System.Buffers.Binary;
using MediaStation.App.Platform.Input;

namespace MediaStation.Core.Tests;

/// <summary>
/// Parsowanie ciała raportu Raw Input bez Win32: klawiatura czyta <c>MakeCode</c>, <c>VKey</c> i flagę
/// <c>RI_KEY_BREAK</c> z układu <c>RAWKEYBOARD</c>, a HID Consumer usage jako LE16 z pierwszego raportu
/// (<c>dwSizeHid</c>/<c>dwCount</c> przed danymi, report ID pomijany przy raporcie dłuższym niż 2 bajty).
/// Za krótkie albo puste ciało jest odrzucane, żeby hub zaraportował <c>inputHubError {rawInput}</c>.
/// </summary>
public class RawInputBodyTests
{
    [Fact]
    public void Keyboard_body_yields_vk_scan_and_key_down()
    {
        var body = KeyboardBody(makeCode: 0x1C, flags: 0x00, virtualKey: 0x0D);

        Assert.True(RawInputBody.TryReadKeyboard(body, out var virtualKey, out var scan, out var down));

        Assert.Equal(0x0D, virtualKey);
        Assert.Equal(0x1C, scan);
        Assert.True(down);
    }

    [Fact]
    public void Keyboard_break_flag_yields_key_up()
    {
        var body = KeyboardBody(makeCode: 0x1C, flags: 0x01, virtualKey: 0x0D);

        Assert.True(RawInputBody.TryReadKeyboard(body, out _, out _, out var down));

        Assert.False(down);
    }

    [Fact]
    public void Keyboard_body_shorter_than_the_header_is_rejected()
    {
        Assert.False(RawInputBody.TryReadKeyboard(new byte[7], out _, out _, out _));
    }

    [Fact]
    public void Hid_report_with_report_id_skips_the_first_byte()
    {
        var body = HidBody(reportSize: 3, [0x02, 0xB0, 0x00]);

        Assert.True(RawInputBody.TryReadHidUsage(body, out var usage));

        Assert.Equal(0x00B0, usage);
    }

    [Fact]
    public void Hid_release_report_with_zero_usage_is_read_so_the_hub_forwards_it()
    {
        var body = HidBody(reportSize: 3, [0x02, 0x00, 0x00]);

        Assert.True(RawInputBody.TryReadHidUsage(body, out var usage));

        Assert.Equal(0, usage);
    }

    [Fact]
    public void Hid_two_byte_report_has_no_report_id()
    {
        var body = HidBody(reportSize: 2, [0xB0, 0x00]);

        Assert.True(RawInputBody.TryReadHidUsage(body, out var usage));

        Assert.Equal(0x00B0, usage);
    }

    [Fact]
    public void Hid_body_shorter_than_the_rawwhid_header_is_rejected()
    {
        Assert.False(RawInputBody.TryReadHidUsage(new byte[7], out _));
    }

    [Fact]
    public void Hid_report_shorter_than_two_bytes_is_rejected()
    {
        Assert.False(RawInputBody.TryReadHidUsage(HidBody(reportSize: 1, [0x02]), out _));
    }

    [Fact]
    public void Hid_empty_report_count_is_rejected()
    {
        var body = HidBody(reportSize: 3, [0x02, 0xB0, 0x00]);
        body[4] = 0;

        Assert.False(RawInputBody.TryReadHidUsage(body, out _));
    }

    [Fact]
    public void Hid_truncated_report_is_rejected()
    {
        // dwSizeHid mówi o 4 bajtach, a ciało niesie tylko 2 bajty danych.
        Assert.False(RawInputBody.TryReadHidUsage(HidBody(reportSize: 4, [0xB0, 0x00]), out _));
    }

    /// <summary>Ciało <c>RAWKEYBOARD</c>: MakeCode, Flags, Reserved, VKey, Message, ExtraInformation.</summary>
    private static byte[] KeyboardBody(ushort makeCode, ushort flags, ushort virtualKey)
    {
        var body = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(body, makeCode);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6), virtualKey);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), 0x0100);
        return body;
    }

    /// <summary>Ciało <c>RAWHID</c>: <c>dwSizeHid</c>, <c>dwCount = 1</c>, a potem jeden raport.</summary>
    private static byte[] HidBody(uint reportSize, byte[] report)
    {
        var body = new byte[8 + report.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(body, reportSize);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 1);
        report.CopyTo(body, 8);
        return body;
    }
}
