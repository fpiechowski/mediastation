using System;
using MediaStation.Core.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Wspólny helper wstrzyknięcia klawisza (AD-3, AD-8): jedno <c>SendInput</c> z dwóch zdarzeń — key-down i key-up
/// — z <see cref="InjectionMarker.MediaStation"/> w <c>dwExtraInfo</c>, więc polityka rozpozna własny klawisz, gdy
/// wróci z hooka. To jedyne miejsce z P/Invoke wstrzyknięć: epic-osk (Unicode) i epic-windows-audio (Alt) rozszerzą
/// ten helper, zachowując ten sam znacznik. Brak walidacji zakresu <c>vk</c>: robi to pompa, a helper zakłada
/// poprawny kod (spoza zakresu <c>SendInput</c> nic nie wstawia i zwraca <c>0</c>).
/// </summary>
internal static class KeyInjector
{
    /// <summary>
    /// Wysyła key-down i key-up <paramref name="virtualKey"/> w jednym <c>SendInput</c>; <c>true</c> tylko wtedy,
    /// gdy system wstawił oba zdarzenia.
    /// </summary>
    /// <param name="virtualKey">Kod wirtualnego klawisza z komendy <c>SendKey</c>.</param>
    public static unsafe bool TrySendKey(int virtualKey)
    {
        var marker = (nuint)InjectionMarker.MediaStation;

        var down = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        down.Anonymous.ki = new KEYBDINPUT
        {
            wVk = (VIRTUAL_KEY)virtualKey,
            dwExtraInfo = marker,
        };

        var up = down;
        up.Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;

        ReadOnlySpan<INPUT> inputs = [down, up];
        var sent = PInvoke.SendInput(inputs, sizeof(INPUT));
        return sent == (uint)inputs.Length;
    }
}
