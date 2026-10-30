using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MediaStation.App.Platform.System;

/// <summary>
/// Cienki shim Win32 dla trzech źródeł sygnału wybudzenia: hook <c>WM_POWERBROADCAST</c> na top-level HWND
/// shella (broadcast nie dociera do okien message-only), <c>RegisterPowerSettingNotification</c> dla
/// <c>GUID_CONSOLE_DISPLAY_STATE</c> i <c>SystemEvents.PowerModeChanged</c>. Decyzję (echo rejestracji i mapowanie
/// źródeł) trzyma <see cref="PowerSignalRouter"/>, księgowość <see cref="PowerSignalReporter"/>, a klasyfikację
/// payloadu <see cref="DisplayStateNotification"/>.
/// </summary>
internal sealed class PowerNotifications : IDisposable
{
    private readonly PowerSignalRouter _router;
    private readonly PowerSourcesAttachment _power = new();
    private readonly HwndSourceHook _hook;
    private UnregisterPowerSettingNotificationSafeHandle? _displayStateRegistration;
    private bool _disposed;

    /// <summary>
    /// Podłącza trzy źródła do <paramref name="hwnd"/>. Echo rejestracji <c>GUID_CONSOLE_DISPLAY_STATE</c>
    /// tłumi <see cref="PowerSignalRouter"/> (także echo doręczone po powrocie stąd); shim tylko przekazuje
    /// zdarzenia systemowe.
    /// </summary>
    public static PowerNotifications Start(IntPtr hwnd, PowerSignalReporter reporter, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        ArgumentNullException.ThrowIfNull(time);

        return new PowerNotifications(hwnd, reporter, time);
    }

    private PowerNotifications(IntPtr hwnd, PowerSignalReporter reporter, TimeProvider time)
    {
        _router = new PowerSignalRouter(reporter, time);
        _hook = OnWindowMessage;
        _router.BeginRegistration();

        var windowHook = false;
        var displayState = false;

        try
        {
            var source = HwndSource.FromHwnd(hwnd);
            if (source is not null)
            {
                _power.Attach(() => source.AddHook(_hook), () => source.RemoveHook(_hook));
                windowHook = true;
            }

            _power.Attach(
                () => displayState = (_displayStateRegistration = RegisterDisplayState(hwnd)) is { IsInvalid: false },
                () =>
                {
                    _displayStateRegistration?.Dispose();
                    _displayStateRegistration = null;
                });

            _power.Attach(
                () => SystemEvents.PowerModeChanged += OnPowerModeChanged,
                () => SystemEvents.PowerModeChanged -= OnPowerModeChanged);
        }
        finally
        {
            // Raport opisuje stan podłączeń z chwili startu także wtedy, gdy konstrukcja padła: nieudane
            // podłączenie zdejmuje już podłączone źródła (DW-2), a linia zostaje ta sama co dawniej.
            _router.CompleteRegistration(
                displayState: displayState,
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
                _router.OnResumeAutomatic();
            }
            else if (powerEvent == PInvoke.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
            {
                if (TryCopyDisplayStateSetting(lParam, out var payload))
                {
                    _router.OnDisplayStateChanged(payload, PInvoke.GUID_CONSOLE_DISPLAY_STATE);
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
            _router.OnPowerModeResumed();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _power.Dispose();
    }
}
