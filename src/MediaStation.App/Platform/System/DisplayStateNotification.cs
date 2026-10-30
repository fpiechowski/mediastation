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
    /// Czy notyfikacja jest poprawną notyfikacją display-state, w dowolnym stanie (on, off, dimmed): właściwy
    /// GUID (<paramref name="setting"/>), <c>DataLength</c> równe <see cref="DisplayStateDataLength"/> i bufor
    /// pokrywający zadeklarowane dane.
    /// </summary>
    public static bool IsDisplayState(ReadOnlySpan<byte> payload, Guid setting)
    {
        if (payload.Length < MinimumPayloadLength)
        {
            return false;
        }

        if (new Guid(payload[..GuidLength]) != setting)
        {
            return false;
        }

        var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(payload[DataLengthOffset..]);
        return dataLength == DisplayStateDataLength
            && payload.Length >= DataOffset + (int)dataLength;
    }

    /// <summary>
    /// Czy notyfikacja jest wybudzeniem: poprawna według <see cref="IsDisplayState"/> i <c>Data[0]</c>
    /// równe <see cref="DisplayStateOn"/>. <paramref name="isRegistrationEcho"/> odrzuca echo rejestracji, bo
    /// system wysyła bieżący stan wyświetlacza zaraz po <c>RegisterPowerSettingNotification</c>.
    /// </summary>
    public static bool IsWake(ReadOnlySpan<byte> payload, Guid setting, bool isRegistrationEcho) =>
        !isRegistrationEcho
        && IsDisplayState(payload, setting)
        && payload[DataOffset] == DisplayStateOn;
}
