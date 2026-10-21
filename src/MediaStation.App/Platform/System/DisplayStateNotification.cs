using System;
using System.Buffers.Binary;

namespace MediaStation.App.Platform.System;

/// <summary>
/// Klasyfikacja <c>PBT_POWERSETTINGCHANGE</c> bez Win32: czyta <c>POWERBROADCAST_SETTING</c> jako bajty
/// (GUID na offsecie 0, <c>DataLength</c> na 16, <c>Data[0]</c> na 20). Wydzielone z shimu, bo to jedyna
/// hardware'owa mapka i musi być testowalna.
/// </summary>
internal static class DisplayStateNotification
{
    /// <summary>Offset <c>DataLength</c> (uint) w <c>POWERBROADCAST_SETTING</c>.</summary>
    public const int DataLengthOffset = 16;

    /// <summary>Offset <c>Data[0]</c> w <c>POWERBROADCAST_SETTING</c>.</summary>
    public const int DataOffset = 20;

    /// <summary><c>DataLength</c> wymagane przez <c>GUID_CONSOLE_DISPLAY_STATE</c>: payload to jeden DWORD.</summary>
    public const uint DisplayStateDataLength = sizeof(uint);

    /// <summary>Najmniejszy payload: GUID 16 B + <c>DataLength</c> 4 B + deklarowany DWORD.</summary>
    public const int MinimumPayloadLength = DataOffset + (int)DisplayStateDataLength;

    /// <summary>Wartość <c>Data[0]</c> dla włączonego wyświetlacza; <c>0</c> to off, a <c>2</c> dimmed.</summary>
    public const byte DisplayStateOn = 1;

    private const int GuidLength = 16;

    /// <summary>
    /// Czy notyfikacja jest wybudzeniem: właściwy GUID (<paramref name="setting"/>), <c>DataLength</c>
    /// równe <see cref="DisplayStateDataLength"/>, bufor pokrywający zadeklarowane dane i <c>Data[0]</c>
    /// równe <see cref="DisplayStateOn"/>. <paramref name="duringStartup"/> odrzuca echo rejestracji, bo
    /// system wysyła bieżący stan wyświetlacza zaraz po <c>RegisterPowerSettingNotification</c>.
    /// </summary>
    public static bool IsWake(ReadOnlySpan<byte> payload, Guid setting, bool duringStartup)
    {
        if (duringStartup || payload.Length < MinimumPayloadLength)
        {
            return false;
        }

        if (new Guid(payload[..GuidLength]) != setting)
        {
            return false;
        }

        var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(payload[DataLengthOffset..]);
        return dataLength == DisplayStateDataLength
            && payload.Length >= DataOffset + (int)dataLength
            && payload[DataOffset] == DisplayStateOn;
    }
}
