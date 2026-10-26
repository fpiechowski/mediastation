using System;
using System.Collections.Generic;
using System.Threading;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.App.Platform.Window;

/// <summary>
/// Wykonawca komend okienkowych (AD-15) nad <see cref="IWindowApi"/>: planuje wywołania, odblokowuje foreground
/// (bezpośrednio, potem Alt, potem <c>AttachThreadInput</c>), odpowiada <see cref="CommandCompleted"/> albo
/// <see cref="CommandFailed"/> i zostawia wpis <c>windowCommand {type, cmdId, hwnd, result}</c>. Nie rzuca (AD-4).
/// Nie zna wątków ani Win32: wołany z wątku WindowManagera.
/// </summary>
internal sealed class WindowCommandExecutor
{
    /// <summary>Źródło wpisów logu WindowManagera.</summary>
    public const string Src = "window";

    /// <summary>Wartość <c>result</c> dla sukcesu.</summary>
    public const string CompletedResult = "completed";

    private const int RestorePolls = 10;
    private static readonly TimeSpan RestorePollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(30);

    private readonly IWindowApi _api;
    private readonly ILog _log;
    private readonly Action<IOrchestratorMessage> _post;
    private readonly Func<long?> _shellHwnd;
    private readonly Action<TimeSpan> _delay;

    public WindowCommandExecutor(
        IWindowApi api,
        ILog log,
        Action<IOrchestratorMessage> post,
        Func<long?> shellHwnd,
        Action<TimeSpan>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(shellHwnd);

        _api = api;
        _log = log;
        _post = post;
        _shellHwnd = shellHwnd;
        _delay = delay ?? Thread.Sleep;
    }

    /// <summary>Wykonuje komendę, loguje wynik i wkłada <see cref="CommandCompleted"/>/<see cref="CommandFailed"/> do kolejki; nie rzuca.</summary>
    public void Execute(WindowCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Uchwyt z samej komendy, żeby wpis logu niósł go także po wyjątku; ShowShell dopisuje własny w `Run`.
        long? hwnd = command switch
        {
            MinimizeWindow minimize => minimize.Hwnd,
            ActivateWindow activate => activate.Hwnd,
            _ => ShellHwndOrNull(),
        };
        ErrorCode? error;
        try
        {
            (hwnd, error) = Run(command, hwnd);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = ErrorCode.Unexpected;
            _log.Error(Src, "windowCommandFailed", ex, new Dictionary<string, object?> { ["cmdId"] = command.CmdId });
        }

        try
        {
            Report(command, hwnd, error);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Raport nie może przepuścić wyjątku przez granicę portu (AD-4).
            _log.Error(Src, "windowCommandReportFailed", ex);
        }
    }

    private long? ShellHwndOrNull()
    {
        try
        {
            return _shellHwnd();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private (long? Hwnd, ErrorCode? Error) Run(WindowCommand command, long? hwnd) => command switch
    {
        MinimizeWindow minimize => (minimize.Hwnd, Minimize(minimize.Hwnd)),
        ActivateWindow activate => (activate.Hwnd, Activate(activate.Hwnd)),
        ShowShell => ShowShellWindow(),
        _ => (hwnd, ErrorCode.Unexpected),
    };

    private ErrorCode? Minimize(long hwnd)
    {
        if (!_api.IsWindow(hwnd))
        {
            return ErrorCode.WindowNotFound;
        }

        return _api.ShowAsync(hwnd, WindowShow.MinimizeNoActivate) ? null : ErrorCode.Unexpected;
    }

    private ErrorCode? Activate(long hwnd)
    {
        if (!_api.IsWindow(hwnd))
        {
            return ErrorCode.WindowNotFound;
        }

        if (_api.IsIconic(hwnd))
        {
            if (!_api.ShowAsync(hwnd, WindowShow.Restore))
            {
                return ErrorCode.Unexpected;
            }

            // Restore jest asynchroniczny: krótkie czekanie, żeby foreground nie trafił w jeszcze zminimalizowane okno.
            for (var i = 0; i < RestorePolls && _api.IsIconic(hwnd); i++)
            {
                _delay(RestorePollInterval);
            }

            if (_api.IsIconic(hwnd))
            {
                // Okno nie wróciło z minimalizacji: foreground nie byłby widoczną aktywacją.
                return ErrorCode.Unexpected;
            }
        }

        return TakeForeground(hwnd);
    }

    private (long? Hwnd, ErrorCode? Error) ShowShellWindow()
    {
        var shell = _shellHwnd();
        if (shell is not { } handle || !_api.IsWindow(handle))
        {
            return (shell, ErrorCode.WindowNotFound);
        }

        var show = _api.IsIconic(handle) ? WindowShow.Restore : WindowShow.Show;
        if (!_api.ShowAsync(handle, show) || !_api.RaiseAsync(handle))
        {
            return (handle, ErrorCode.Unexpected);
        }

        return (handle, TakeForeground(handle));
    }

    /// <summary>Kolejność odblokowania: bezpośrednie, Alt i ponowienie, <c>AttachThreadInput</c> i ponowienie.</summary>
    private ErrorCode? TakeForeground(long hwnd)
    {
        if (_api.SetForeground(hwnd))
        {
            return null;
        }

        if (_api.SimulateAlt())
        {
            _delay(RetryDelay);
            if (_api.SetForeground(hwnd))
            {
                return null;
            }
        }

        return _api.SetForegroundAttached(hwnd) ? null : ErrorCode.ForegroundDenied;
    }

    private void Report(WindowCommand command, long? hwnd, ErrorCode? error)
    {
        var data = new Dictionary<string, object?>
        {
            ["type"] = command.GetType().Name,
            ["cmdId"] = command.CmdId,
        };

        if (hwnd is { } value)
        {
            data["hwnd"] = value;
        }

        data["result"] = error?.ToString() ?? CompletedResult;

        if (error is { } code)
        {
            _log.Warn(Src, "windowCommand", data);
            _post(new CommandFailed(command.CmdId, code));
        }
        else
        {
            _log.Info(Src, "windowCommand", data);
            _post(new CommandCompleted(command.CmdId));
        }
    }
}
