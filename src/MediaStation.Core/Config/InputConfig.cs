using System.Collections.Generic;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Config;

/// <summary>
/// <c>config/input.json</c>: mapowanie wejścia klawiatury (sygnatura urządzenia i kod VK) oraz przycisków pada (AD-8).
/// </summary>
/// <param name="GamepadHome">Kombinacja przycisków XInput dająca Home z pada; co najmniej 2 różne przyciski.</param>
public sealed record InputConfig(IReadOnlyList<InputDevice> Devices, IReadOnlyList<GamepadButton> GamepadHome)
{
    /// <summary>Mapowanie przycisków pada na Actions; brak sekcji daje wartości domyślne.</summary>
    public InputGamepad Gamepad { get; init; } = new();

    /// <summary>Progi timingu polityki wejścia; brak sekcji daje wartości domyślne.</summary>
    public InputTiming Timing { get; init; } = new();
}

/// <summary>Progi timingu <c>config/input.json</c>; pola opcjonalne z wartościami domyślnymi.</summary>
public sealed record InputTiming
{
    /// <summary>Próg long-press OK w Shell w milisekundach; po nim OK daje samo <c>long</c> (AD-8).</summary>
    public int LongPressMs { get; init; } = 600;

    /// <summary>Zwłoka przed pierwszym <c>repeat</c> trzymanej strzałki w milisekundach (AD-8).</summary>
    public int RepeatDelayMs { get; init; } = 400;

    /// <summary>Odstęp między kolejnymi <c>repeat</c> trzymanej strzałki w milisekundach (AD-8).</summary>
    public int RepeatIntervalMs { get; init; } = 120;
}

/// <summary>
/// Mapowanie przycisków XInput na Actions; brak pola daje wartość domyślną, a jawne <c>null</c> wyłącza przycisk.
/// Domyślnie D-pad daje strzałki, A — <c>ok</c>, B — <c>back</c>, a Y — <c>keyboard</c> (AD-8).
/// </summary>
public sealed record InputGamepad
{
    /// <summary>D-pad w górę; domyślnie <c>up</c>.</summary>
    public ActionKind? DpadUp { get; init; } = ActionKind.Up;

    /// <summary>D-pad w dół; domyślnie <c>down</c>.</summary>
    public ActionKind? DpadDown { get; init; } = ActionKind.Down;

    /// <summary>D-pad w lewo; domyślnie <c>left</c>.</summary>
    public ActionKind? DpadLeft { get; init; } = ActionKind.Left;

    /// <summary>D-pad w prawo; domyślnie <c>right</c>.</summary>
    public ActionKind? DpadRight { get; init; } = ActionKind.Right;

    /// <summary>Start; domyślnie brak mapowania (składnik kombinacji Home).</summary>
    public ActionKind? Start { get; init; }

    /// <summary>Back; domyślnie brak mapowania (składnik kombinacji Home).</summary>
    public ActionKind? Back { get; init; }

    /// <summary>Wciśnięcie lewej gałki; domyślnie brak mapowania.</summary>
    public ActionKind? LeftThumb { get; init; }

    /// <summary>Wciśnięcie prawej gałki; domyślnie brak mapowania.</summary>
    public ActionKind? RightThumb { get; init; }

    /// <summary>Lewy bumper; domyślnie brak mapowania.</summary>
    public ActionKind? LeftShoulder { get; init; }

    /// <summary>Prawy bumper; domyślnie brak mapowania.</summary>
    public ActionKind? RightShoulder { get; init; }

    /// <summary>A; domyślnie <c>ok</c>.</summary>
    public ActionKind? A { get; init; } = ActionKind.Ok;

    /// <summary>B; domyślnie <c>back</c>.</summary>
    public ActionKind? B { get; init; } = ActionKind.Back;

    /// <summary>X; domyślnie brak mapowania.</summary>
    public ActionKind? X { get; init; }

    /// <summary>Y; domyślnie <c>keyboard</c>.</summary>
    public ActionKind? Y { get; init; } = ActionKind.Keyboard;

    /// <summary>Niepuste mapowania w kolejności <see cref="GamepadButton"/>.</summary>
    public IEnumerable<(GamepadButton Button, ActionKind Kind)> Mappings()
    {
        (GamepadButton Button, ActionKind? Kind)[] mappings =
        [
            (GamepadButton.DpadUp, DpadUp),
            (GamepadButton.DpadDown, DpadDown),
            (GamepadButton.DpadLeft, DpadLeft),
            (GamepadButton.DpadRight, DpadRight),
            (GamepadButton.Start, Start),
            (GamepadButton.Back, Back),
            (GamepadButton.LeftThumb, LeftThumb),
            (GamepadButton.RightThumb, RightThumb),
            (GamepadButton.LeftShoulder, LeftShoulder),
            (GamepadButton.RightShoulder, RightShoulder),
            (GamepadButton.A, A),
            (GamepadButton.B, B),
            (GamepadButton.X, X),
            (GamepadButton.Y, Y),
        ];

        foreach (var (button, kind) in mappings)
        {
            if (kind is { } value)
            {
                yield return (button, value);
            }
        }
    }
}

/// <summary>Jedno urządzenie wejścia rozpoznane przez Raw Input.</summary>
/// <param name="Signature">Sygnatura urządzenia, np. <c>VID_1915&amp;PID_1025</c>; unikalna.</param>
/// <param name="Keys">
/// Kod VK z hooka LL w formacie <c>0xNN</c> → logiczny klawisz. AppMenu to <c>ok</c> z <c>phase: long</c>
/// interpretowane w Shell (AD-8), nie osobny klawisz.
/// </param>
public sealed record InputDevice(string Signature, ActionSource Source, IReadOnlyDictionary<string, ActionKind> Keys)
{
    /// <summary>
    /// Raport HID Consumer → Action: klucz <c>0x&lt;strona&gt;:0x&lt;usage&gt;</c> (np. <c>0x000C:0x00CF</c>), wartość wyłącznie
    /// <c>keyboard</c>. Tylko dla urządzenia <c>remote</c>; brak sekcji oznacza brak wyzwalacza HID (AD-8).
    /// </summary>
    public IReadOnlyDictionary<string, ActionKind>? Hid { get; init; }

    /// <summary>
    /// Sygnatura urządzenia zastępczego: pasuje do każdej niepustej ścieżki Raw Input i przegrywa z każdym wpisem
    /// konkretnym; może mieć wyłącznie <c>source: keyboard</c> (AD-13).
    /// </summary>
    public const string WildcardSignature = "*";
}

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
