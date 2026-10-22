using System;
using System.Collections.Generic;
using System.Globalization;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Input;

/// <summary>Zdarzenie klawiatury z hooka LL przeniesione do Core bez Win32 (AD-3, AD-8).</summary>
public enum KeyTransition
{
    Down,
    Up,
}

/// <summary>
/// Jedno zdarzenie klawiatury z hooka LL. <paramref name="VirtualKey"/> to kod VK, <paramref name="Injected"/>
/// odpowiada <c>LLKHF_INJECTED</c>, a <paramref name="TimeMs"/> — polu <c>time</c> zdarzenia hooka.
/// </summary>
public sealed record KeyStroke(int VirtualKey, KeyTransition Transition, bool Injected, long TimeMs);

/// <summary>Raport HID Consumer (np. pilot na donglu) z Raw Input; <paramref name="DevicePath"/> to ścieżka urządzenia.</summary>
public sealed record HidConsumerReport(string DevicePath, long TimeMs);

/// <summary>Zdarzenie klawiatury z Raw Input (<c>WM_INPUT</c>); służy tylko do identyfikacji urządzenia.</summary>
public sealed record RawKeyboardInput(string DevicePath, long TimeMs);

/// <summary>Decyzja polityki dla jednego zdarzenia: czy połknąć klawisz i jakie Actions dostarczyć.</summary>
public sealed record InputDecision(bool Swallow, IReadOnlyList<ActionMessage> Actions);

/// <summary>
/// Czysta polityka wejścia (AD-8): zamienia zdarzenia platformy na decyzję o połknięciu i Actions.
/// Połknięcie i <c>kind</c> zapadają po samym VK ze scalonego mapowania wszystkich urządzeń z <c>input.json</c>,
/// bo hook LL działa przed <c>WM_INPUT</c>, a połknięty klawisz może nie dojść do Raw Input. <c>source</c> pochodzi
/// z korelacji w oknie <see cref="CorrelationWindowMs"/>. Jedno naciśnięcie daje jedną Action, a OK w trybie
/// <see cref="InputMode.Shell"/> dostaje <c>press</c> na key-up przed progiem albo samo <c>long</c> po progu.
/// Czas płynie wyłącznie przez wstrzyknięty <see cref="TimeProvider"/> (AD-14). Typ nie jest wątkowo bezpieczny:
/// App woła go z jednego wątku Input (callback hooka i timer progu na tym samym wątku).
/// </summary>
public sealed class InputPolicy
{
    /// <summary>Okno korelacji raportu urządzenia z klawiszem; okno jest jednostronne (raport przed klawiszem).</summary>
    public const int CorrelationWindowMs = 50;

    private static readonly IReadOnlyList<ActionMessage> NoActions = Array.Empty<ActionMessage>();
    private static readonly InputDecision PassDecision = new(false, NoActions);

    private readonly TimeProvider _time;
    private readonly InputMode _mode;
    private readonly int _longPressMs;
    private readonly Dictionary<int, ActionKind> _kinds = [];
    private readonly List<(string Signature, ActionSource Source)> _devices = [];
    private readonly HashSet<int> _held = [];
    private PendingLong? _pendingLong;
    private (long TimeMs, ActionSource Source)? _lastConsumer;
    private (long TimeMs, ActionSource Source)? _lastKeyboard;

    /// <param name="config">Config wejścia; mapowanie jest scalane ze wszystkich urządzeń, pierwsze wygrywa.</param>
    /// <param name="time">Zegar terminów long-press (AD-14).</param>
    /// <param name="mode">Tryb routingu; w tej story stały, routing dokłada story 5.</param>
    public InputPolicy(InputConfig config, TimeProvider time, InputMode mode = InputMode.Shell)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(time);

        _time = time;
        _mode = mode;
        _longPressMs = config.Timing.LongPressMs;

        foreach (var device in config.Devices)
        {
            if (device is null)
            {
                continue;
            }

            foreach (var (key, kind) in device.Keys)
            {
                if (TryParseVirtualKey(key, out var virtualKey))
                {
                    _kinds.TryAdd(virtualKey, kind);
                }
            }

            if (!string.IsNullOrWhiteSpace(device.Signature))
            {
                _devices.Add((device.Signature, device.Source));
            }
        }
    }

    /// <summary>Termin progu long-press; <c>null</c>, gdy żaden klawisz nie czeka na <c>long</c>.</summary>
    public DateTimeOffset? NextDeadline => _pendingLong?.Deadline;

    /// <summary>
    /// Jedyna decyzja o połknięciu i Actions dla zdarzenia hooka. Autorepeat systemu (drugi key-down bez key-up)
    /// jest połykany bez Actions, a key-up nieznanego VK nie jest połykany.
    /// </summary>
    public InputDecision OnKey(KeyStroke key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_kinds.TryGetValue(key.VirtualKey, out var kind))
        {
            return PassDecision;
        }

        if (key.Transition == KeyTransition.Down)
        {
            if (!_held.Add(key.VirtualKey))
            {
                return new InputDecision(true, NoActions);
            }

            var source = ResolveSource(key);
            if (RequiresLongPress(kind))
            {
                if (_pendingLong is not null)
                {
                    // Jeden termin long (AD-8): drugi klawisz OK trzymany równolegle dostaje press od razu,
                    // bo terminu pilnuje tylko jeden PendingLong.
                    return new InputDecision(true, [new ActionMessage(kind, ActionPhase.Press, source, key.TimeMs)]);
                }

                _pendingLong = new PendingLong(
                    key.VirtualKey,
                    kind,
                    source,
                    key.TimeMs,
                    _time.GetUtcNow().AddMilliseconds(_longPressMs));
                return new InputDecision(true, NoActions);
            }

            return new InputDecision(true, [new ActionMessage(kind, ActionPhase.Press, source, key.TimeMs)]);
        }

        var wasHeld = _held.Remove(key.VirtualKey);
        if (wasHeld && _pendingLong is { } pending && pending.VirtualKey == key.VirtualKey)
        {
            _pendingLong = null;
            return new InputDecision(true, [new ActionMessage(kind, ActionPhase.Press, pending.Source, key.TimeMs)]);
        }

        return new InputDecision(true, NoActions);
    }

    /// <summary>Buforuje ostatni raport HID Consumer dopasowany do sygnatury z configu; nie decyduje o połknięciu.</summary>
    public void OnHidConsumer(HidConsumerReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (MatchDevice(report.DevicePath) is { } device)
        {
            _lastConsumer = (report.TimeMs, device.Source);
        }
    }

    /// <summary>Buforuje ostatni <c>WM_INPUT</c> klawiatury dopasowany do sygnatury z configu; nie decyduje o połknięciu.</summary>
    public void OnRawKeyboard(RawKeyboardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (MatchDevice(input.DevicePath) is { } device)
        {
            _lastKeyboard = (input.TimeMs, device.Source);
        }
    }

    /// <summary>
    /// Callback terminu progu. Spóźniony timer (po key-up albo po <c>long</c>) nic nie robi, bo
    /// <see cref="NextDeadline"/> jest już <c>null</c>; timer nie jest zdarzeniem wejścia, więc nie połyka.
    /// </summary>
    public InputDecision OnTimer(DateTimeOffset now)
    {
        if (_pendingLong is not { } pending || now < pending.Deadline)
        {
            return PassDecision;
        }

        _pendingLong = null;
        var action = new ActionMessage(
            pending.Kind,
            ActionPhase.Long,
            pending.Source,
            pending.DownTimeMs + _longPressMs);
        return new InputDecision(false, [action]);
    }

    /// <summary>Long-press dotyczy wyłącznie OK w Shell; OSK dokłada story 5.</summary>
    private bool RequiresLongPress(ActionKind kind) => kind == ActionKind.Ok && _mode == InputMode.Shell;

    /// <summary>
    /// Korelacja <c>source</c>: wstrzyknięty klawisz po raporcie HID Consumer dopasowanego urządzenia daje jego
    /// <c>source</c>, inaczej ostatni <c>WM_INPUT</c> w oknie, w ostateczności <c>keyboard</c>.
    /// </summary>
    private ActionSource ResolveSource(KeyStroke key)
    {
        if (key.Injected && WithinWindow(_lastConsumer, key.TimeMs))
        {
            return _lastConsumer!.Value.Source;
        }

        if (WithinWindow(_lastKeyboard, key.TimeMs))
        {
            return _lastKeyboard!.Value.Source;
        }

        return ActionSource.Keyboard;
    }

    private static bool WithinWindow((long TimeMs, ActionSource Source)? report, long timeMs) =>
        report is { } value
        && value.TimeMs <= timeMs
        && timeMs - value.TimeMs <= CorrelationWindowMs;

    /// <summary>Dopasowanie ścieżki Raw Input do sygnatury z configu, bez rozróżniania wielkości liter.</summary>
    private (string Signature, ActionSource Source)? MatchDevice(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
        {
            return null;
        }

        foreach (var device in _devices)
        {
            if (devicePath.Contains(device.Signature, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        return null;
    }

    private static bool TryParseVirtualKey(string key, out int virtualKey)
    {
        virtualKey = 0;
        return key.Length == 4
            && key.StartsWith("0x", StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out virtualKey);
    }

    /// <summary>Jeden czekający long-press: kind, source, czas key-down z hooka i termin na <see cref="TimeProvider"/>.</summary>
    private sealed record PendingLong(
        int VirtualKey,
        ActionKind Kind,
        ActionSource Source,
        long DownTimeMs,
        DateTimeOffset Deadline);
}
