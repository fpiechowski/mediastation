using System;
using System.Collections.Generic;
using MediaStation.Core.Config;
using MediaStation.Core.Input;
using MediaStation.Core.Logging;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Diagnostyka odbioru (włączana zmienną <c>MS_INPUT_TRACE=1</c>): dekorator <see cref="IInputTarget"/>, który loguje
/// każde zdarzenie klawiatury z hooka LL z decyzją polityki. Pokazuje, które klawisze w ogóle docierają do hooka
/// i w jakiej kolejności; nie zmienia decyzji.
/// </summary>
internal sealed class TracingInputTarget : IInputTarget
{
    private readonly IInputTarget _inner;
    private readonly ILog _log;

    internal TracingInputTarget(IInputTarget inner, ILog log)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(log);

        _inner = inner;
        _log = log;
    }

    public DateTimeOffset? NextDeadline => _inner.NextDeadline;

    public InputDecision OnKey(KeyStroke key)
    {
        _log.Info("input", "keyEnter", new Dictionary<string, object?> { ["vk"] = $"0x{key.VirtualKey:X2}", ["transition"] = key.Transition });
        var started = global::System.Diagnostics.Stopwatch.GetTimestamp();
        InputDecision decision;
        try
        {
            decision = _inner.OnKey(key);
        }
        catch (Exception ex)
        {
            _log.Error("input", "keyTraceFailed", ex);
            throw;
        }

        _log.Info("input", "keyTrace", new Dictionary<string, object?>
        {
            ["us"] = (long)global::System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMicroseconds,
            ["vk"] = $"0x{key.VirtualKey:X2}",
            ["transition"] = key.Transition,
            ["injected"] = key.Injected,
            ["swallow"] = decision.Swallow,
            ["actions"] = decision.Actions.Count,
        });
        return decision;
    }

    public InputDecision OnGamepad(GamepadSnapshot snapshot) => _inner.OnGamepad(snapshot);

    public InputDecision OnHidConsumer(HidConsumerReport report)
    {
        _log.Info("input", "hidTrace", new Dictionary<string, object?>
        {
            ["usagePage"] = $"0x{report.UsagePage:X4}",
            ["usage"] = $"0x{report.Usage:X4}",
        });
        return _inner.OnHidConsumer(report);
    }

    public InputDecision OnPointer(PointerStroke pointer, ScreenRectangle? clip) => _inner.OnPointer(pointer, clip);

    public void OnRawKeyboard(RawKeyboardInput input) => _inner.OnRawKeyboard(input);

    public InputDecision OnTimer(DateTimeOffset now) => _inner.OnTimer(now);
}
