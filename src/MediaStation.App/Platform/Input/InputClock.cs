using System;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Zdejmuje 32-bitowe zawinięcie czasu wejścia (AD-8, notatka story 1): pole <c>time</c> hooka LL i
/// <c>GetMessageTime</c> liczą milisekundy od startu systemu i wracają do zera co ~49,7 dnia.
/// <see cref="Normalize"/> akumuluje wielokrotności <c>2^32</c>, więc wynik rośnie monotonicznie,
/// a małe cofnięcie zegara (np. o 1 ms) nie dolicza pełnego obiegu. Typ nie jest wątkowo bezpieczny:
/// woła go wyłącznie wątek Input.
/// </summary>
internal sealed class InputClock
{
    /// <summary>Cofnięcie większe niż pół obiegu (2^31 ms) oznacza zawinięcie 32-bitowego licznika.</summary>
    internal const uint WrapThreshold = 0x8000_0000;

    private long _high;
    private long _last;
    private bool _started;

    /// <summary>Zwraca monotoniczny czas w milisekundach dla 32-bitowego znacznika z Win32.</summary>
    internal long Normalize(uint timeMs)
    {
        var candidate = _high + timeMs;

        // Tylko cofnięcie o więcej niż pół obiegu to zawinięcie licznika; zwykły krok wstecz zostaje krokiem wstecz.
        if (_started && _last - candidate > WrapThreshold)
        {
            _high += 1L << 32;
            candidate = _high + timeMs;
        }

        _started = true;
        _last = candidate;
        return candidate;
    }
}
