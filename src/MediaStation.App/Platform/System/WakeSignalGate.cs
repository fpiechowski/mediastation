using System;

namespace MediaStation.App.Platform.System;

/// <summary>
/// Reguła zwycięzcy sygnału wybudzenia (decyzja epiki): wygrywa pierwszy z <c>PBT_APMRESUMEAUTOMATIC</c>
/// albo <c>GUID_CONSOLE_DISPLAY_STATE=on</c>, a kolejne w oknie 5 s są pomijane. Reguła mieszka w adapterze
/// w App, nie w Core: Orchestrator widzi wyłącznie gotowy <see cref="MediaStation.Core.Contracts.SystemResumed"/>.
/// Okno liczy się na zegarze monotonicznym (<see cref="TimeProvider.GetTimestamp"/>), więc zmiana zegara
/// ściennego — także cofnięcie — nie przyjmuje ani nie blokuje wybudzeń.
/// </summary>
internal sealed class WakeSignalGate
{
    /// <summary>Źródło <c>WM_POWERBROADCAST</c> z <c>PBT_APMRESUMEAUTOMATIC</c>.</summary>
    public const string ResumeAutomaticSource = "PBT_APMRESUMEAUTOMATIC";

    /// <summary>Źródło <c>PBT_POWERSETTINGCHANGE</c> z <c>GUID_CONSOLE_DISPLAY_STATE=on</c>.</summary>
    public const string DisplayStateSource = "GUID_CONSOLE_DISPLAY_STATE";

    /// <summary>Okno deduplikacji liczone od ostatniego przyjętego sygnału.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private long? _lastAcceptedTimestamp;

    public WakeSignalGate(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>
    /// Czy kandydat jest zwycięzcą. Odrzuca źródła spoza reguły epiki oraz kandydatów przed upływem
    /// <see cref="Window"/> od ostatniego przyjętego sygnału; przyjęty kandydat zostaje nową kotwicą.
    /// </summary>
    public bool TryAccept(string source)
    {
        if (source is not (ResumeAutomaticSource or DisplayStateSource))
        {
            return false;
        }

        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (_lastAcceptedTimestamp is { } last && _time.GetElapsedTime(last, now) < Window)
            {
                return false;
            }

            _lastAcceptedTimestamp = now;
            return true;
        }
    }
}
