using System;
using System.Collections.Generic;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Input;

/// <summary>
/// Adapter tylko-log konsumenta Actions i porażek InputHubu (AD-13): każde <see cref="Deliver"/> zostawia wpis
/// <c>action {kind, phase, source}</c>, a każde <see cref="Report"/> — <c>inputHubError {operation[, errorCode]}</c>.
/// Wpisy nie niosą <c>tsMono</c> ani surowych kodów (<c>vk</c>, <c>scan</c>, <c>usage</c>); te należą wyłącznie
/// do trybu <c>--calibrate-input</c>. Nie decyduje o niczym i nie rzuca wyjątku z powodu zapisu.
/// </summary>
public sealed class LogActionSink : IActionSink, IInputHubErrorSink
{
    /// <summary>Źródło wpisów logu wejścia (jak <c>InputHubRoutingPort.Src</c>).</summary>
    public const string Src = "input";

    private readonly ILog _log;

    public LogActionSink(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
    }

    /// <summary>Loguje jedną Action; kolejność kluczy jest kolejnością pól w logu: <c>kind</c>, <c>phase</c>, <c>source</c>.</summary>
    public void Deliver(ActionMessage action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _log.Info(Src, "action", new Dictionary<string, object?>
        {
            ["kind"] = action.Kind,
            ["phase"] = action.Phase,
            ["source"] = action.Source,
        });
    }

    /// <summary>Loguje porażkę huba; klucz <c>errorCode</c> jest pomijany, gdy operacja go nie zwróciła.</summary>
    public void Report(InputHubError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var data = new Dictionary<string, object?>
        {
            ["operation"] = error.Operation,
        };

        // Jawnie pomijamy klucz: JsonLinesLog serializuje wartość null w słowniku jako `"errorCode": null`.
        if (error.ErrorCode is { } errorCode)
        {
            data["errorCode"] = errorCode;
        }

        _log.Error(Src, "inputHubError", data: data);
    }
}
