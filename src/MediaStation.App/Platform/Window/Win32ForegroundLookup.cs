using System;
using MediaStation.Core.Input;
using MediaStation.Core.Orchestrator;

namespace MediaStation.App.Platform.Window;

/// <summary>
/// Fakty o oknie dla Orchestratora (<see cref="IForegroundLookup"/>): uchwyt shella z <see cref="ManagedWindows"/>. Tylko odczyt, bez zmiany okna.
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
}
