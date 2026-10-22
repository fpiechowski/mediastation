using System;
using System.Collections.Generic;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Adapter routingu wejścia (AD-8): każdy tryb zostawia wpis
/// <c>inputRouting {cmdId, mode, appType?, pointer, epoch}</c> i zapisuje lokalną kopię w
/// <see cref="InputRoutingState"/>, z której hook czyta bez czekania. <c>epoch</c> równy albo starszy nie zmienia
/// kopii i kończy się wpisem <c>warn inputRoutingStale {cmdId, epoch}</c>. Nie udaje wykonawcy, więc nie wysyła
/// <see cref="CommandCompleted"/>. <c>appType</c> nie występuje, gdy żadna aplikacja nie jest aktywna.
/// </summary>
public sealed class InputHubRoutingPort : IInputRoutingPort
{
    /// <summary>Źródło wpisów logu routingu wejścia.</summary>
    public const string Src = "input";

    private readonly InputRoutingState _routing;
    private readonly ILog _log;

    /// <param name="routing">Współdzielona kopia, z której czyta <see cref="InputPolicy"/> na wątku Input.</param>
    /// <param name="log">Log aplikacji.</param>
    public InputHubRoutingPort(InputRoutingState routing, ILog log)
    {
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(log);

        _routing = routing;
        _log = log;
    }

    /// <summary>Loguje nowy tryb routingu i zapisuje go w lokalnej kopii, jeśli <c>epoch</c> jest nowszy.</summary>
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

        if (!_routing.Apply(command))
        {
            _log.Warn(Src, "inputRoutingStale", new Dictionary<string, object?>
            {
                ["cmdId"] = command.CmdId,
                ["epoch"] = command.Epoch,
            });
        }
    }
}
