using System;
using System.Collections.Generic;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.Core.Input;

/// <summary>
/// Adapter tylko-log kalibracji wejścia (AD-13): jedno <see cref="Deliver"/> daje jeden wpis
/// <c>rawInput {signature, vk, scan, usagePage, usage, injected, hasVk, llBeforeRawMs}</c>, a <see cref="Report"/>
/// — <c>inputHubError {operation[, errorCode]}</c> jak w <see cref="LogActionSink"/>. Kształt wpisu jest stały:
/// brakujące źródło zostaje jako <c>null</c>, więc autor widzi te same osiem kolumn przy każdym naciśnięciu.
/// Surowych kodów nie loguje żaden inny adapter. Nie decyduje o niczym i nie rzuca wyjątku z powodu zapisu.
/// </summary>
public sealed class LogRawInputSink : IRawInputSink, IInputHubErrorSink
{
    /// <summary>Źródło wpisów logu wejścia (jak <c>LogActionSink.Src</c>).</summary>
    public const string Src = "input";

    private readonly ILog _log;

    public LogRawInputSink(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
    }

    /// <summary>
    /// Loguje jedno naciśnięcie; kolejność kluczy jest kolejnością pól w logu: <c>signature</c>, <c>vk</c>,
    /// <c>scan</c>, <c>usagePage</c>, <c>usage</c>, <c>injected</c>, <c>hasVk</c>, <c>llBeforeRawMs</c>.
    /// </summary>
    public void Deliver(RawInputEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _log.Info(Src, "rawInput", new Dictionary<string, object?>
        {
            ["signature"] = entry.Signature,
            ["vk"] = Hex(entry.VirtualKey, "X2"),
            ["scan"] = Hex(entry.Scan, "X2"),
            ["usagePage"] = Hex(entry.UsagePage, "X4"),
            ["usage"] = Hex(entry.Usage, "X4"),
            ["injected"] = entry.Injected,
            ["hasVk"] = entry.HasVk,
            ["llBeforeRawMs"] = entry.LlBeforeRawMs,
        });
    }

    /// <summary>Loguje urządzenie spoza configu jako <c>rawInputUnknownDevice {signature}</c> (DW-27).</summary>
    public void DeliverUnknownDevice(string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        _log.Info(Src, "rawInputUnknownDevice", new Dictionary<string, object?>
        {
            ["signature"] = signature,
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

    /// <summary>Kod w formacie <c>0xNN</c> (VK, scan) albo <c>0xNNNN</c> (usage); <c>null</c> zostaje <c>null</c>.</summary>
    private static string? Hex(int? value, string format) => value is { } code ? $"0x{code.ToString(format)}" : null;
}
