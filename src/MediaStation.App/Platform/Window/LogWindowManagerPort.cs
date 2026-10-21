using System;
using System.Collections.Generic;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Platform.Window;

/// <summary>
/// Adapter tylko-log WindowManagera do czasu epic-windows-audio: każda komenda zostawia wpis
/// <c>windowCommand {type, cmdId, hwnd}</c> i nie ma efektu ubocznego. Nie udaje wykonawcy, więc nie
/// wysyła <see cref="CommandCompleted"/>. <c>hwnd</c> nie występuje dla <see cref="ShowShell"/>.
/// </summary>
public sealed class LogWindowManagerPort : IWindowManagerPort
{
    /// <summary>Źródło wpisów logu WindowManagera.</summary>
    public const string Src = "window";

    private readonly ILog _log;

    public LogWindowManagerPort(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
    }

    /// <summary>Loguje komendę okienkową bez zmiany stanu okien.</summary>
    public void Execute(WindowCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var data = new Dictionary<string, object?>
        {
            ["type"] = command.GetType().Name,
            ["cmdId"] = command.CmdId,
        };

        var hwnd = command switch
        {
            ActivateWindow activate => activate.Hwnd,
            MinimizeWindow minimize => minimize.Hwnd,
            _ => (long?)null,
        };

        if (hwnd is { } value)
        {
            data["hwnd"] = value;
        }

        _log.Info(Src, "windowCommand", data);
    }
}
