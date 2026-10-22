using System;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Przelicza termin <see cref="MediaStation.Core.Input.InputPolicy.NextDeadline"/> na opóźnienie <c>SetTimer</c>
/// (AD-8, AD-14). <c>null</c> znaczy „zabij timer", inaczej wynik to co najmniej 1 ms, zaokrąglony w górę i
/// ograniczony do <see cref="int.MaxValue"/>. Typ bez Win32, żeby arytmetykę terminu pokrywały testy.
/// </summary>
internal static class InputTimerDelay
{
    /// <summary>Opóźnienie timera w milisekundach albo <c>null</c>, gdy żaden termin nie czeka.</summary>
    internal static uint? Compute(DateTimeOffset? deadline, DateTimeOffset now)
    {
        if (deadline is not { } due)
        {
            return null;
        }

        var delay = due - now;

        // Termin miniony (albo równy teraz) odpala się po najbliższym przebiegu pętli, a nie jest odrzucany.
        if (delay <= TimeSpan.Zero)
        {
            return 1;
        }

        var milliseconds = Math.Ceiling(delay.TotalMilliseconds);
        return milliseconds >= int.MaxValue ? int.MaxValue : (uint)milliseconds;
    }
}
