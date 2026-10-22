using System;

namespace MediaStation.Core.Input;

/// <summary>
/// Cel zdarzeń wejścia dla <c>InputHub</c> (AD-8): hub nie zna konkretnej polityki, tylko ten kontrakt. Tryb główny
/// dostaje <see cref="InputPolicy"/>, a <c>--calibrate-input</c> — <see cref="InputCalibration"/>; jeden hub i jeden
/// zestaw źródeł Win32 obsługują oba tryby. Implementacja nie jest wątkowo bezpieczna: App woła ją wyłącznie
/// z wątku Input (callback hooka i timer na tym samym wątku).
/// </summary>
public interface IInputTarget
{
    /// <summary>Najbliższy termin (long-press albo koniec kalibracji); <c>null</c>, gdy nic nie czeka.</summary>
    DateTimeOffset? NextDeadline { get; }

    /// <summary>Decyzja dla jednego zdarzenia hooka: czy połknąć klawisz i jakie Actions dostarczyć.</summary>
    InputDecision OnKey(KeyStroke key);

    /// <summary>Raport HID Consumer z Raw Input; nie decyduje o połknięciu.</summary>
    void OnHidConsumer(HidConsumerReport report);

    /// <summary><c>WM_INPUT</c> klawiatury; nie decyduje o połknięciu.</summary>
    void OnRawKeyboard(RawKeyboardInput input);

    /// <summary>Callback terminu; zwrócona decyzja nigdy nie połyka, bo timer nie jest zdarzeniem wejścia.</summary>
    InputDecision OnTimer(DateTimeOffset now);
}

/// <summary>
/// Dopasowanie ścieżki Raw Input do sygnatury urządzenia z <c>input.json</c> bez rozróżniania wielkości liter.
/// Wspólne dla <see cref="InputPolicy"/> i <see cref="InputCalibration"/>, żeby filtr urządzeń miał jedną definicję.
/// </summary>
internal static class DeviceSignature
{
    /// <summary>Czy <paramref name="devicePath"/> zawiera <paramref name="signature"/>?</summary>
    internal static bool Matches(string? devicePath, string? signature) =>
        !string.IsNullOrEmpty(devicePath)
        && !string.IsNullOrEmpty(signature)
        && devicePath.Contains(signature, StringComparison.OrdinalIgnoreCase);
}
