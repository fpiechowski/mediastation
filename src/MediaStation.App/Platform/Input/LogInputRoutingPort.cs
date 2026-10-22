using System;
using System.Collections.Generic;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Adapter tylko-log routingu wejścia do czasu epic-input: każdy tryb zostawia wpis
/// <c>inputRouting {cmdId, mode, appType, pointer, epoch}</c> i nie ma efektu ubocznego. Nie udaje wykonawcy,
/// więc nie wysyła <see cref="CommandCompleted"/>. <c>appType</c> nie występuje, gdy żadna aplikacja nie jest aktywna.
/// </summary>
public sealed class LogInputRoutingPort : IInputRoutingPort
{
    /// <summary>Źródło wpisów logu routingu wejścia.</summary>
    public const string Src = "input";

    private readonly ILog _log;

    public LogInputRoutingPort(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
    }

    /// <summary>Loguje nowy tryb routingu bez zmiany zachowania InputHub.</summary>
    public void Execute(SetInputRouting command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Kolejność kluczy jest kolejnością pól w logu: cmdId, mode, appType?, pointer, epoch.
        var data = new Dictionary<string, object?>
        {
            ["cmdId"] = command.CmdId,
            ["mode"] = command.Mode,
        };

        // Jawnie pomijamy klucz: JsonLinesLog serializuje wartość null w słowniku jako "appType": null.
        if (command.AppType is { } appType)
        {
            data["appType"] = appType;
        }

        data["pointer"] = command.Pointer;
        data["epoch"] = command.Epoch;

        _log.Info(Src, "inputRouting", data);
    }
}
