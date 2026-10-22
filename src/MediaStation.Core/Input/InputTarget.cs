using System;
using MediaStation.Core.Config;

namespace MediaStation.Core.Input;

/// <summary>
/// Cel zdarzeń wejścia dla <c>InputHub</c> (AD-8): hub nie zna konkretnej polityki, tylko ten kontrakt. Tryb główny
/// dostaje <see cref="InputPolicy"/>, a <c>--calibrate-input</c> — <see cref="InputCalibration"/>; jeden hub i jeden
/// zestaw źródeł Win32 obsługują oba tryby. Implementacja nie jest wątkowo bezpieczna: App woła ją wyłącznie
/// z wątku Input (callback hooka i timer na tym samym wątku).
/// </summary>
public interface IInputTarget
{
    /// <summary>Najbliższy termin (long-press, repeat albo koniec kalibracji); <c>null</c>, gdy nic nie czeka.</summary>
    DateTimeOffset? NextDeadline { get; }

    /// <summary>Decyzja dla jednego zdarzenia hooka: czy połknąć klawisz i jakie Actions dostarczyć.</summary>
    InputDecision OnKey(KeyStroke key);

    /// <summary>
    /// Migawka stanu pada XInput (indeks 0); decyzja nigdy nie połyka, bo XInput jest tylko odpytywany i aplikacja
    /// i tak widzi przyciski — wiersz AD-8 decyduje wyłącznie o zestawie Actions. Migawka <c>Available=false</c>
    /// nie jest zwolnieniem przycisków: trzymania pada znikają bez Actions, a pierwsza dostępna migawka po przerwie
    /// jest nowym stanem początkowym.
    /// </summary>
    InputDecision OnGamepad(GamepadSnapshot snapshot);

    /// <summary>Raport HID Consumer z Raw Input; nie decyduje o połknięciu.</summary>
    void OnHidConsumer(HidConsumerReport report);

    /// <summary>
    /// Zdarzenie wskaźnika z hooka myszy: czy połknąć ruch, kliknięcie albo kółko na podstawie wiersza
    /// <c>pointer</c>. Decyzja nigdy nie niesie Actions — wskaźnik nie jest źródłem akcji MediaStation.
    /// <paramref name="clip"/> to aktualny prostokąt <c>ClipCursor</c> (piksele fizyczne) albo <c>null</c>, gdy
    /// clip nie obowiązuje; tylko w <c>osk</c> klik poza nim jest połykany.
    /// </summary>
    InputDecision OnPointer(PointerStroke pointer, ScreenRectangle? clip);

    /// <summary><c>WM_INPUT</c> klawiatury; nie decyduje o połknięciu.</summary>
    void OnRawKeyboard(RawKeyboardInput input);

    /// <summary>Callback terminu; zwrócona decyzja nigdy nie połyka, bo timer nie jest zdarzeniem wejścia.</summary>
    InputDecision OnTimer(DateTimeOffset now);
}

/// <summary>
/// Dopasowanie ścieżki Raw Input do sygnatury urządzenia z <c>input.json</c> bez rozróżniania wielkości liter.
/// Sygnatura <see cref="InputDevice.WildcardSignature"/> pasuje do każdej niepustej ścieżki. Wspólne dla
/// <see cref="InputPolicy"/> i <see cref="InputCalibration"/>, żeby filtr urządzeń miał jedną definicję.
/// </summary>
internal static class DeviceSignature
{
    /// <summary>
    /// Czy <paramref name="devicePath"/> pasuje do <paramref name="signature"/>? Pusta ścieżka i pusta sygnatura
    /// nie pasują do niczego, także do wildcardu.
    /// </summary>
    internal static bool Matches(string? devicePath, string? signature) =>
        !string.IsNullOrEmpty(devicePath)
        && !string.IsNullOrEmpty(signature)
        && (signature == InputDevice.WildcardSignature
            || devicePath.Contains(signature, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Czy <paramref name="signature"/> zawiera <c>*</c>, ale nie jest dokładnie <see cref="InputDevice.WildcardSignature"/>
    /// ani pusta? Taka sygnatura nigdy nie dopasuje ścieżki Raw Input, bo <see cref="Matches"/> traktuje <c>*</c>
    /// specjalnie tylko jako całą sygnaturę; walidacja zamienia ten martwy wpis w błąd configu (DW-8).
    /// </summary>
    internal static bool HasEmbeddedWildcard(string? signature) =>
        !string.IsNullOrEmpty(signature)
        && signature != InputDevice.WildcardSignature
        && signature.Contains('*');
}
