using System;
using System.Buffers.Binary;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Odczyt ciała raportu Raw Input bez Win32 (AD-3), żeby parsowanie pokrywały testy w <c>MediaStation.Core.Tests</c>.
/// Ciało zaczyna się za <c>RAWINPUTHEADER</c>, a jego długość pochodzi z liczby bajtów zwróconej przez
/// <c>GetRawInputData</c> (nie z deklarowanych rozmiarów w nagłówku raportu).
/// </summary>
internal static class RawInputBody
{
    /// <summary><c>RAWKEYBOARD</c> do odczytu <c>MakeCode</c> (0), <c>Flags</c> (2) i <c>VKey</c> (6).</summary>
    private const int KeyboardBodyMinBytes = 8;

    /// <summary><c>RI_KEY_BREAK</c>: bit 0 flag klawiatury oznacza key-up.</summary>
    private const ushort KeyboardBreakFlag = 0x01;

    /// <summary><c>RAWHID</c>: <c>dwSizeHid</c> (4) + <c>dwCount</c> (4) przed danymi raportu.</summary>
    private const int HidHeaderBytes = 8;

    /// <summary>
    /// Czyta klawiaturę: <paramref name="scan"/> z <c>MakeCode</c>, <paramref name="virtualKey"/> z <c>VKey</c>,
    /// a <paramref name="down"/> z braku flagi <c>RI_KEY_BREAK</c>. Za krótkie ciało daje <c>false</c>.
    /// </summary>
    internal static bool TryReadKeyboard(ReadOnlySpan<byte> body, out int virtualKey, out int scan, out bool down)
    {
        virtualKey = 0;
        scan = 0;
        down = false;

        if (body.Length < KeyboardBodyMinBytes)
        {
            return false;
        }

        scan = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
        virtualKey = BinaryPrimitives.ReadUInt16LittleEndian(body[6..]);
        down = (flags & KeyboardBreakFlag) == 0;
        return true;
    }

    /// <summary>
    /// Czyta usage pierwszego raportu HID Consumer: <c>dwSizeHid</c>/<c>dwCount</c> z nagłówka, bajty raportu
    /// z ciała, a usage jako LE16. Raport dłuższy niż 2 bajty ma na początku report ID i jest pomijany
    /// (wzór <c>spike/win/HomeLogger.cs</c>); za krótkie albo puste ciało daje <c>false</c>.
    /// </summary>
    internal static bool TryReadHidUsage(ReadOnlySpan<byte> body, out int usage)
    {
        usage = 0;

        if (body.Length < HidHeaderBytes)
        {
            return false;
        }

        var reportSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(body);
        var reportCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
        if (reportSize <= 0 || reportCount <= 0)
        {
            return false;
        }

        var data = body[HidHeaderBytes..];
        if (reportSize > data.Length || reportSize < 2)
        {
            return false;
        }

        var report = data[..reportSize];
        usage = reportSize > 2
            ? report[1] | report[2] << 8
            : report[0] | report[1] << 8;
        return true;
    }
}
