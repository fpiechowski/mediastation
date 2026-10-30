using System;

namespace MediaStation.App.Platform.System;

/// <summary>
/// Decyzja adaptera power bez Win32/WPF: echo rejestracji <c>GUID_CONSOLE_DISPLAY_STATE</c> i mapowanie
/// trzech źródeł sygnału wybudzenia na <see cref="PowerSignalReporter"/>. Shim <see cref="PowerNotifications"/>
/// dostarcza tylko zdarzenia systemowe (hook <c>WM_POWERBROADCAST</c>, notyfikację display-state, <c>SystemEvents</c>)
/// i deleguje tutaj, więc decyzja jest testowalna bez HWND. Stan echa jest ruszany wyłącznie na wątku UI (hook);
/// <see cref="OnPowerModeResumed"/> przychodzi z wątku <c>SystemEvents</c> i niczego z niego nie czyta.
/// </summary>
internal sealed class PowerSignalRouter
{
    /// <summary>Źródło <c>SystemEvents.PowerModeChanged</c> z <c>PowerModes.Resume</c>; tylko kandydat do logu.</summary>
    public const string PowerModeChangedSource = "PowerModeChanged";

    /// <summary>
    /// Jak długo po udanej rejestracji pierwsza notyfikacja display-state jest brana za echo. Na UM760 prawdziwe
    /// wybudzenia przychodzą tylko jako <c>GUID_CONSOLE_DISPLAY_STATE</c>, więc oczekiwanie musi mieć limit: echo,
    /// które nigdy nie przyszło, zjadłoby pierwsze wybudzenie. Liczy się wyłącznie wtedy, gdy echa jeszcze nie było.
    /// </summary>
    public static readonly TimeSpan EchoWait = TimeSpan.FromSeconds(5);

    private readonly PowerSignalReporter _reporter;
    private readonly TimeProvider _time;
    private bool _registering;
    private bool _echoSeen;
    private long? _echoWaitStart;

    public PowerSignalRouter(PowerSignalReporter reporter, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        ArgumentNullException.ThrowIfNull(time);

        _reporter = reporter;
        _time = time;
    }

    /// <summary>
    /// Otwiera okno rejestracji: system wysyła bieżący stan wyświetlacza po
    /// <c>RegisterPowerSettingNotification</c>, a to echo nie jest wybudzeniem.
    /// </summary>
    public void BeginRegistration()
    {
        _registering = true;
        _echoSeen = false;
        _echoWaitStart = null;
    }

    /// <summary>
    /// Zamyka okno rejestracji i raportuje linię <c>powerSignalsRegistered</c> w jednym kroku: rozdzielenie tych
    /// dwóch rzeczy pozwoliłoby zostawić okno otwarte po starcie. Echo może przyjść dopiero po powrocie ze startu,
    /// więc po udanej rejestracji bez echa zaczyna się odliczanie <see cref="EchoWait"/>.
    /// </summary>
    public void CompleteRegistration(bool displayState, bool windowHook, bool powerModeChanged)
    {
        _registering = false;
        _echoWaitStart = displayState && !_echoSeen ? _time.GetTimestamp() : null;
        _reporter.ReportRegistration(displayState, windowHook, powerModeChanged);
    }

    /// <summary>
    /// <c>PBT_APMRESUMEAUTOMATIC</c>: poza oknem echa, bo to nie echo rejestracji — wybudzenie w trakcie
    /// startu ma wygrać jak dotąd.
    /// </summary>
    public void OnResumeAutomatic() => _reporter.Report(WakeSignalGate.ResumeAutomaticSource);

    /// <summary>
    /// <c>PBT_POWERSETTINGCHANGE</c>: kandydat tylko gdy <see cref="DisplayStateNotification.IsWake"/> uzna
    /// payload za wybudzenie; echo rejestracji jest odrzucane.
    /// </summary>
    public void OnDisplayStateChanged(ReadOnlySpan<byte> payload, Guid setting)
    {
        var echo = IsRegistrationEcho(payload, setting);
        if (DisplayStateNotification.IsWake(payload, setting, echo))
        {
            _reporter.Report(WakeSignalGate.DisplayStateSource);
        }
    }

    /// <summary>
    /// <c>PowerModes.Resume</c> z <c>SystemEvents</c>: reguła epiki nie dopuszcza tego źródła jako zwycięzcy,
    /// więc <see cref="WakeSignalGate"/> odrzuca je i zostaje sam wpis <c>powerSignal</c>.
    /// </summary>
    public void OnPowerModeResumed() => _reporter.Report(PowerModeChangedSource);

    /// <summary>
    /// W oknie rejestracji odrzuca wszystko, a za echo liczy tylko poprawną notyfikację. Po oknie pierwsza poprawna
    /// notyfikacja kończy oczekiwanie, w dowolnym stanie wyświetlacza, i jest echem, jeśli przyszła przed upływem
    /// <see cref="EchoWait"/>; obce i uszkodzone notyfikacje niczego nie zużywają.
    /// </summary>
    private bool IsRegistrationEcho(ReadOnlySpan<byte> payload, Guid setting)
    {
        var displayState = DisplayStateNotification.IsDisplayState(payload, setting);
        if (_registering)
        {
            _echoSeen |= displayState;
            return true;
        }

        if (!displayState || _echoWaitStart is not { } start)
        {
            return false;
        }

        _echoWaitStart = null;
        return _time.GetElapsedTime(start) < EchoWait;
    }
}
