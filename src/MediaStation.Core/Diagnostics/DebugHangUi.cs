using System;
using System.Globalization;

namespace MediaStation.Core.Diagnostics;

/// <summary>
/// Parse zmiennej <c>MEDIASTATION_DEBUG_HANG_UI</c> (AD-3, AD-14): czysta decyzja bez I/O i bez wyjątków. Wartość to
/// liczba sekund zawieszenia wątku UI po pierwszym renderze; wszystko, czego nie da się bezpiecznie zamienić na
/// dodatni <see cref="TimeSpan"/>, jest ignorowane, więc literówka w środowisku nigdy nie psuje startu.
/// </summary>
public static class DebugHangUi
{
    /// <summary>Nazwa zmiennej środowiskowej czytanej raz przy starcie, wyłącznie w trybie głównym.</summary>
    public const string EnvVarName = "MEDIASTATION_DEBUG_HANG_UI";

    /// <summary>
    /// Zwraca czas zawieszenia albo <c>null</c> dla pustej, nieparsowalnej, niedodatniej i nieskończonej wartości oraz
    /// liczby większej niż <c>int.MaxValue</c> milisekund (ponad 24,86 dnia), bo takiego czasu nie przyjmie
    /// <c>Thread.Sleep</c>. Kultura invariant, więc kropka jest separatorem dziesiętnym niezależnie od ustawień systemu.
    /// </summary>
    public static TimeSpan? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > int.MaxValue / 1000.0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
