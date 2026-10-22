using System;

namespace MediaStation.App.Platform.System;

/// <summary>
/// Decyzja adaptera power bez Win32/WPF: okno echa rejestracji <c>GUID_CONSOLE_DISPLAY_STATE</c> i mapowanie
/// trzech źródeł sygnału wybudzenia na <see cref="PowerSignalReporter"/>. Shim <see cref="PowerNotifications"/>
/// dostarcza tylko zdarzenia systemowe (hook <c>WM_POWERBROADCAST</c>, notyfikację display-state, <c>SystemEvents</c>)
/// i deleguje tutaj, więc decyzja jest testowalna bez HWND.
/// </summary>
internal sealed class PowerSignalRouter
{
    /// <summary>Źródło <c>SystemEvents.PowerModeChanged</c> z <c>PowerModes.Resume</c>; tylko kandydat do logu.</summary>
    public const string PowerModeChangedSource = "PowerModeChanged";

    private readonly PowerSignalReporter _reporter;
    private bool _registering;

    public PowerSignalRouter(PowerSignalReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(reporter);

        _reporter = reporter;
    }

    /// <summary>
    /// Otwiera okno rejestracji: system wysyła bieżący stan wyświetlacza zaraz po
    /// <c>RegisterPowerSettingNotification</c>, a to echo nie jest wybudzeniem.
    /// </summary>
    public void BeginRegistration() => _registering = true;

    /// <summary>
    /// Zamyka okno echa i raportuje linię <c>powerSignalsRegistered</c> w jednym kroku: rozdzielenie tych
    /// dwóch rzeczy pozwoliłoby zostawić okno otwarte po starcie.
    /// </summary>
    public void CompleteRegistration(bool displayState, bool windowHook, bool powerModeChanged)
    {
        _registering = false;
        _reporter.ReportRegistration(displayState, windowHook, powerModeChanged);
    }

    /// <summary>
    /// <c>PBT_APMRESUMEAUTOMATIC</c>: poza oknem echa, bo to nie echo rejestracji — wybudzenie w trakcie
    /// startu ma wygrać jak dotąd.
    /// </summary>
    public void OnResumeAutomatic() => _reporter.Report(WakeSignalGate.ResumeAutomaticSource);

    /// <summary>
    /// <c>PBT_POWERSETTINGCHANGE</c>: kandydat tylko gdy <see cref="DisplayStateNotification.IsWake"/> uzna
    /// payload za wybudzenie; w oknie rejestracji echo jest odrzucane.
    /// </summary>
    public void OnDisplayStateChanged(ReadOnlySpan<byte> payload, Guid setting)
    {
        if (DisplayStateNotification.IsWake(payload, setting, _registering))
        {
            _reporter.Report(WakeSignalGate.DisplayStateSource);
        }
    }

    /// <summary>
    /// <c>PowerModes.Resume</c> z <c>SystemEvents</c>: reguła epiki nie dopuszcza tego źródła jako zwycięzcy,
    /// więc <see cref="WakeSignalGate"/> odrzuca je i zostaje sam wpis <c>powerSignal</c>.
    /// </summary>
    public void OnPowerModeResumed() => _reporter.Report(PowerModeChangedSource);
}
