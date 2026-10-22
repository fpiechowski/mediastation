using System.Collections.Generic;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Config;

/// <summary>
/// <c>config/input.json</c>: mapowanie wejścia kluczowane sygnaturą urządzenia i kodem VK (AD-8).
/// </summary>
/// <param name="GamepadHome">Kombinacja przycisków XInput dająca Home z pada; co najmniej 2 różne przyciski.</param>
public sealed record InputConfig(IReadOnlyList<InputDevice> Devices, IReadOnlyList<GamepadButton> GamepadHome)
{
    /// <summary>Progi timingu polityki wejścia; brak sekcji daje wartości domyślne.</summary>
    public InputTiming Timing { get; init; } = new();
}

/// <summary>Progi timingu <c>config/input.json</c>; pola opcjonalne z wartościami domyślnymi.</summary>
public sealed record InputTiming
{
    /// <summary>Próg long-press OK w Shell w milisekundach; po nim OK daje samo <c>long</c> (AD-8).</summary>
    public int LongPressMs { get; init; } = 600;
}

/// <summary>Jedno urządzenie wejścia rozpoznane przez Raw Input.</summary>
/// <param name="Signature">Sygnatura urządzenia, np. <c>VID_1915&amp;PID_1025</c>; unikalna.</param>
/// <param name="Keys">
/// Kod VK z hooka LL w formacie <c>0xNN</c> → logiczny klawisz. <c>appMenu</c> jest niedozwolone: AppMenu to
/// <c>ok</c> z <c>phase: long</c> interpretowane w Shell (AD-8).
/// </param>
public sealed record InputDevice(string Signature, ActionSource Source, IReadOnlyDictionary<string, ActionKind> Keys);

/// <summary>Przyciski XInput (<c>XINPUT_GAMEPAD_*</c>).</summary>
public enum GamepadButton
{
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    Start,
    Back,
    LeftThumb,
    RightThumb,
    LeftShoulder,
    RightShoulder,
    A,
    B,
    X,
    Y,
}
