using System;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Input;

/// <summary>Okno pierwszego planu rozpoznane przez platformÄ™: korzeĹ„ okna (<c>GA_ROOT</c>) i PID jego procesu.</summary>
public readonly record struct ForegroundWindowInfo(long Hwnd, int Pid);

/// <summary>Odczyt okna pierwszego planu; <c>null</c> znaczy nieznane (brak okna, secure desktop, przeĹ‚Ä…cznik Alt+Tab).</summary>
public interface IForegroundWindow
{
    ForegroundWindowInfo? Current();
}

/// <summary>Bramka foreground (AD-8): czy na pierwszym planie jest okno MediaStation.</summary>
public interface IForegroundProbe
{
    /// <summary><c>true</c> tylko dla znanego okna z zarzÄ…dzanego zbioru; nieznane okno jest obce.</summary>
    bool IsManaged();
}

/// <summary>
/// Lokalna kopia zbioru okien MediaStation (AD-8): shell, klawiatura ekranowa i okna aplikacji. Shell wpisuje
/// <see cref="SetShell"/> (App), resztÄ™ publikuje Orchestrator przez <see cref="Apply"/>, ktĂłry przyjmuje wyĹ‚Ä…cznie
/// <c>epoch</c> Ĺ›ciĹ›le wiÄ™kszy od bieĹĽÄ…cego. WÄ…tek Input czyta bez czekania na Orchestrator: <c>lock</c> obejmuje
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
    /// Zapisuje zbiĂłr z komendy, gdy <c>epoch</c> jest Ĺ›ciĹ›le wiÄ™kszy od bieĹĽÄ…cego, i zwraca <c>true</c>.
    /// Dla <c>epoch</c> rĂłwnego albo starszego kopia zostaje bez zmian.
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
    /// Czy okno naleĹĽy do MediaStation: najpierw uchwyt (shell, OSK, okno aplikacji), potem PID korzenia aplikacji,
    /// bo okna potomne i okna dialogowe aplikacji majÄ… inne uchwyty tego samego procesu.
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
    private readonly Action<ForegroundWindowInfo?, bool>? _onChange;
    private (ForegroundWindowInfo? Window, bool Managed)? _last;

    /// <param name="onChange">WoĹ‚any tylko przy zmianie okna albo werdyktu (diagnostyka bramki, bez wpisu na kaĹĽdy klawisz).</param>
    public ManagedForegroundProbe(
        IForegroundWindow window,
        ManagedWindows managed,
        Action<ForegroundWindowInfo?, bool>? onChange = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(managed);

        _window = window;
        _managed = managed;
        _onChange = onChange;
    }

    public bool IsManaged()
    {
        var window = _window.Current();
        var managed = window is { } foreground && _managed.Contains(foreground.Hwnd, foreground.Pid);
        if (_onChange is not null && _last != (window, managed))
        {
            _last = (window, managed);
            _onChange(window, managed);
        }

        return managed;
    }
}
