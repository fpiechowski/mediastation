using System;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Input;

/// <summary>Okno pierwszego planu rozpoznane przez platformę: korzeń okna (<c>GA_ROOT</c>) i PID jego procesu.</summary>
public readonly record struct ForegroundWindowInfo(long Hwnd, int Pid);

/// <summary>Odczyt okna pierwszego planu; <c>null</c> znaczy nieznane (brak okna, secure desktop, przełącznik Alt+Tab).</summary>
public interface IForegroundWindow
{
    ForegroundWindowInfo? Current();
}

/// <summary>Bramka foreground (AD-8): czy na pierwszym planie jest okno MediaStation.</summary>
public interface IForegroundProbe
{
    /// <summary><c>true</c> tylko dla znanego okna z zarządzanego zbioru; nieznane okno jest obce.</summary>
    bool IsManaged();
}

/// <summary>
/// Lokalna kopia zbioru okien MediaStation (AD-8): shell, klawiatura ekranowa i okna aplikacji. Shell wpisuje
/// <see cref="SetShell"/> (App), resztę publikuje Orchestrator przez <see cref="Apply"/>, który przyjmuje wyłącznie
/// <c>epoch</c> ściśle większy od bieżącego. Wątek Input czyta bez czekania na Orchestrator: <c>lock</c> obejmuje
/// jedno przypisanie niezmiennej migawki, tak jak w <see cref="InputRoutingState"/>.
/// </summary>
public sealed class ManagedWindows
{
    private readonly object _gate = new();
    private Snapshot _current = new(0, null, Array.Empty<ManagedWindow>(), 0);

    /// <summary>Zapisuje uchwyt okna shella; <c>0</c> go usuwa.</summary>
    public void SetShell(long hwnd)
    {
        lock (_gate)
        {
            _current = _current with { Shell = hwnd };
        }
    }

    /// <summary>
    /// Zapisuje zbiór z komendy, gdy <c>epoch</c> jest ściśle większy od bieżącego, i zwraca <c>true</c>.
    /// Dla <c>epoch</c> równego albo starszego kopia zostaje bez zmian.
    /// </summary>
    public bool Apply(SetManagedWindows command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            if (command.Epoch <= _current.Epoch)
            {
                return false;
            }

            _current = _current with
            {
                Epoch = command.Epoch,
                Osk = command.OskHwnd,
                Apps = [.. command.Apps],
            };
            return true;
        }
    }

    /// <summary>
    /// Czy okno należy do MediaStation: najpierw uchwyt (shell, OSK, okno aplikacji), potem PID korzenia aplikacji,
    /// bo okna potomne i okna dialogowe aplikacji mają inne uchwyty tego samego procesu.
    /// </summary>
    public bool Contains(long hwnd, int pid)
    {
        Snapshot current;
        lock (_gate)
        {
            current = _current;
        }

        if (hwnd != 0 && (hwnd == current.Shell || hwnd == current.Osk))
        {
            return true;
        }

        foreach (var app in current.Apps)
        {
            if ((hwnd != 0 && app.Hwnd == hwnd) || (pid != 0 && app.RootPid == pid))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record Snapshot(long Shell, long? Osk, ManagedWindow[] Apps, long Epoch);
}

/// <summary>Sonda bramki foreground: okno pierwszego planu z platformy dopasowane do <see cref="ManagedWindows"/>.</summary>
public sealed class ManagedForegroundProbe : IForegroundProbe
{
    private readonly IForegroundWindow _window;
    private readonly ManagedWindows _managed;

    public ManagedForegroundProbe(IForegroundWindow window, ManagedWindows managed)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(managed);

        _window = window;
        _managed = managed;
    }

    public bool IsManaged() => _window.Current() is { } foreground && _managed.Contains(foreground.Hwnd, foreground.Pid);
}
