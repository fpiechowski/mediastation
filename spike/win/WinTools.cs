// Window helpers for window.ps1 and moonlight.ps1 (S8 hide variant, S11). C# 5 for Add-Type in PS 5.1.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class MsWin
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    const uint GW_OWNER = 4;

    // Unowned top-level windows with a title.
    public static List<IntPtr> TopWindows(uint pid, bool includeHidden)
    {
        List<IntPtr> found = new List<IntPtr>();
        EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
        {
            if (ProcessIdOf(hwnd) == pid && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero && Title(hwnd).Length > 0
                && (includeHidden || IsWindowVisible(hwnd)))
                found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Title(IntPtr hwnd)
    {
        StringBuilder text = new StringBuilder(256);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    public static uint ProcessIdOf(IntPtr hwnd)
    {
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        return pid;
    }
}
