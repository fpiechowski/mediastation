using System;
using System.Collections.Generic;
using MediaStation.App.Platform.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MediaStation.App.Platform.Window;

/// <summary>
/// Adapter <see cref="IWindowApi"/> na CsWin32 (AD-15): jedyne miejsce z P/Invoke okien WindowManagera.
/// Operacje na cudzych oknach idą przez <c>ShowWindowAsync</c> i <c>SWP_ASYNCWINDOWPOS</c>, więc zawieszone okno
/// (także shell z zawieszonym UI) nie blokuje wątku WindowManagera.
/// </summary>
internal sealed class Win32WindowApi : IWindowApi
{
    private static HWND ToHandle(long hwnd) => new((nint)hwnd);

    public bool IsWindow(long hwnd) => PInvoke.IsWindow(ToHandle(hwnd));

    public bool IsIconic(long hwnd) => PInvoke.IsIconic(ToHandle(hwnd));

    public bool IsVisible(long hwnd) => PInvoke.IsWindowVisible(ToHandle(hwnd));

    public bool ShowAsync(long hwnd, WindowShow show)
    {
        // Minimalizacja to zawsze SW_SHOWMINNOACTIVE: SW_HIDE zdejmuje okno przeglądarki z paska i z widoczności (AD-5).
        var command = show switch
        {
            WindowShow.Restore => SHOW_WINDOW_CMD.SW_RESTORE,
            WindowShow.Show => SHOW_WINDOW_CMD.SW_SHOW,
            WindowShow.MinimizeNoActivate => SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE,
            _ => throw new ArgumentOutOfRangeException(nameof(show)),
        };

        return PInvoke.ShowWindowAsync(ToHandle(hwnd), command);
    }

    public bool RaiseAsync(long hwnd) => PInvoke.SetWindowPos(
        ToHandle(hwnd),
        default,
        0,
        0,
        0,
        0,
        SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
            | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    public bool SetForeground(long hwnd) => PInvoke.SetForegroundWindow(ToHandle(hwnd));

    public bool SimulateAlt() => KeyInjector.TrySendKey(0x12);

    public unsafe bool SetForegroundAttached(long hwnd)
    {
        var target = ToHandle(hwnd);
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return PInvoke.SetForegroundWindow(target);
        }

        uint foregroundPid;
        var foregroundThread = PInvoke.GetWindowThreadProcessId(foreground, &foregroundPid);
        var currentThread = PInvoke.GetCurrentThreadId();
        if (foregroundThread == 0 || foregroundThread == currentThread)
        {
            return PInvoke.SetForegroundWindow(target);
        }

        var attached = PInvoke.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            return PInvoke.SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    public unsafe IReadOnlyList<WindowInfo> EnumerateTopLevel()
    {
        var windows = new List<WindowInfo>();
        PInvoke.EnumWindows(
            (hwnd, _) =>
            {
                uint pid;
                if (PInvoke.GetWindowThreadProcessId(hwnd, &pid) == 0)
                {
                    return true;
                }

                Span<char> name = stackalloc char[256];
                var length = PInvoke.GetClassName(hwnd, name);
                var className = length > 0 ? new string(name[..length]) : string.Empty;
                var owner = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER);
                windows.Add(new WindowInfo((long)(nint)hwnd.Value, (int)pid, className, PInvoke.IsWindowVisible(hwnd), !owner.IsNull));
                return true;
            },
            default);

        return windows;
    }
}
