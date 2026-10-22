using System;

namespace MediaStation.Core.Input;

/// <summary>
/// Porażka operacji InputHubu (AD-13): <paramref name="Operation"/> to jedna z <see cref="InputHubOperations"/>,
/// a <paramref name="ErrorCode"/> to kod Win32, gdy operacja go zwróciła (inaczej <c>null</c>).
/// Z założenia nie niesie kodów klawiszy: log <c>inputHubError</c> nigdy nie zawiera surowych zdarzeń wejścia.
/// </summary>
public sealed record InputHubError(string Operation, int? ErrorCode);

/// <summary>Nazwy operacji InputHubu w logu <c>inputHubError {operation[, errorCode]}</c>.</summary>
public static class InputHubOperations
{
    /// <summary>Rejestracja hooka <c>WH_KEYBOARD_LL</c>.</summary>
    public const string KeyboardHook = "keyboardHook";

    /// <summary>Rejestracja Raw Input klawiatury i HID Consumer (<c>RIDEV_INPUTSINK</c>) oraz odczyt <c>WM_INPUT</c>.</summary>
    public const string RawInput = "rawInput";

    /// <summary>Utworzenie okna message-only (klasa i <c>CreateWindowEx</c>), pętla <c>GetMessage</c> i timer progu long-press.</summary>
    public const string MessageWindow = "messageWindow";

    /// <summary>Callback hooka i timer progu long-press przewrócone wyjątkiem.</summary>
    public const string HookCallback = "hookCallback";

    /// <summary>Wywołanie <c>IActionSink.Deliver</c> przewrócone wyjątkiem sinka.</summary>
    public const string ActionSink = "actionSink";
}
