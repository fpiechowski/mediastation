using System;
using MediaStation.Core.Input;
using MediaStation.Core.Orchestrator;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace MediaStation.App.Platform.Window;

/// <summary>
/// Fakty o oknie dla Orchestratora (<see cref="IForegroundLookup"/>): uchwyt shella z <see cref="ManagedWindows"/>
/// i PID procesu okna z <c>GetWindowThreadProcessId</c>. Tylko odczyt, bez zmiany okna.
/// </summary>
internal sealed class Win32ForegroundLookup : IForegroundLookup
{
    private readonly ManagedWindows _managed;

    public Win32ForegroundLookup(ManagedWindows managed)
    {
        ArgumentNullException.ThrowIfNull(managed);
        _managed = managed;
    }

    public long ShellHwnd => _managed.Shell;

    public unsafe int ProcessIdOf(long hwnd)
    {
        uint pid;
        return PInvoke.GetWindowThreadProcessId(new HWND((nint)hwnd), &pid) == 0 ? 0 : (int)pid;
    }
}
