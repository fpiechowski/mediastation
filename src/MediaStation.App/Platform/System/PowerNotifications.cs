using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MediaStation.Core.Logging;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MediaStation.App.Platform.System;

/// <summary>
/// Cienki shim Win32 dla trzech źródeł sygnału wybudzenia: hook <c>WM_POWERBROADCAST</c> na top-level HWND
/// shella (broadcast nie dociera do okien message-only), <c>RegisterPowerSettingNotification</c> dla
/// <c>GUID_CONSOLE_DISPLAY_STATE</c> i <c>SystemEvents.PowerModeChanged</c>. Decyzję i księgowość trzyma
/// <see cref="PowerSignalReporter"/>, a klasyfikację payloadu <see cref="DisplayStateNotification"/>.
/// </summary>
internal sealed class PowerNotifications : IDisposable
{
    private const string PowerModeChangedSource = "PowerModeChanged";

    private readonly PowerSignalReporter _reporter;
    private readonly HwndSource? _source;
    private readonly HwndSourceHook _hook;
    private UnregisterPowerSettingNotificationSafeHandle? _displayStateRegistration;
    private bool _registering;
    private bool _disposed;

    /// <summary>
    /// Podłącza trzy źródła do <paramref name="hwnd"/>. Echo rejestracji <c>GUID_CONSOLE_DISPLAY_STATE</c>
    /// (bieżący stan wysyłany od razu po rejestracji) nie jest kandydatem: cały start jest w oknie
    /// <see cref="_registering"/>.
    /// </summary>
    public static PowerNotifications Start(IntPtr hwnd, PowerSignalReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(reporter);

        return new PowerNotifications(hwnd, reporter);
    }

    private PowerNotifications(IntPtr hwnd, PowerSignalReporter reporter)
    {
        _reporter = reporter;
        _hook = OnWindowMessage;
        _registering = true;

        var windowHook = false;
        try
        {
            _source = HwndSource.FromHwnd(hwnd);
            if (_source is not null)
            {
                _source.AddHook(_hook);
                windowHook = true;
            }

            _displayStateRegistration = RegisterDisplayState(hwnd);

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        finally
        {
            _registering = false;
            _reporter.ReportRegistration(
                displayState: _displayStateRegistration is { IsInvalid: false },
                windowHook: windowHook,
                powerModeChanged: true);
        }
    }

    private static unsafe UnregisterPowerSettingNotificationSafeHandle? RegisterDisplayState(IntPtr hwnd)
    {
        try
        {
            var setting = PInvoke.GUID_CONSOLE_DISPLAY_STATE;
            var registration = PInvoke.RegisterPowerSettingNotification(
                (HANDLE)hwnd,
                &setting,
                REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);

            // NULL = rejestracja padła: zostają hook i PowerModeChanged.
            return registration.IsNull
                ? null
                : new UnregisterPowerSettingNotificationSafeHandle(registration, ownsHandle: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Brak notyfikacji display-state nie może zabić startu aplikacji.
            return null;
        }
    }

    /// <summary>
    /// Czyta <c>POWERBROADCAST_SETTING</c> bez nadczytu: <c>DataLength</c> (4 B na offsecie 16) musi być
    /// równe DWORD, a dopiero potem kopiujemy nagłówek i zadeklarowane dane.
    /// </summary>
    private static bool TryCopyDisplayStateSetting(IntPtr setting, out byte[] payload)
    {
        payload = new byte[DisplayStateNotification.MinimumPayloadLength];

        var dataLength = (uint)Marshal.ReadInt32(setting, DisplayStateNotification.DataLengthOffset);
        if (dataLength != DisplayStateNotification.DisplayStateDataLength)
        {
            return false;
        }

        Marshal.Copy(setting, payload, 0, payload.Length);
        return true;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Hook zawsze zwraca false: broadcast ma dotrzeć także do innych odbiorców.
        if (msg == (int)PInvoke.WM_POWERBROADCAST)
        {
            var powerEvent = (uint)wParam.ToInt64();
            if (powerEvent == PInvoke.PBT_APMRESUMEAUTOMATIC)
            {
                _reporter.Report(WakeSignalGate.ResumeAutomaticSource);
            }
            else if (powerEvent == PInvoke.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
            {
                if (TryCopyDisplayStateSetting(lParam, out var payload)
                    && DisplayStateNotification.IsWake(payload, PInvoke.GUID_CONSOLE_DISPLAY_STATE, _registering))
                {
                    _reporter.Report(WakeSignalGate.DisplayStateSource);
                }
            }
        }

        return IntPtr.Zero;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // Reguła epiki nie dopuszcza PowerModeChanged jako zwycięzcy; to kandydat tylko do logu.
        if (e.Mode == PowerModes.Resume)
        {
            _reporter.Report(PowerModeChangedSource);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        if (_source is not null)
        {
            _source.RemoveHook(_hook);
        }

        _displayStateRegistration?.Dispose();
    }
}
