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
/// odpowiada <c>LLKHF_INJECTED</c>, <paramref name="TimeMs"/> — polu <c>time</c> zdarzenia hooka, a
/// <paramref name="Scan"/> — kod scan (surowy, czytany tylko przez kalibrację).
/// </summary>
public sealed record KeyStroke(int VirtualKey, KeyTransition Transition, bool Injected, long TimeMs, int Scan = 0);

/// <summary>
/// Raport HID Consumer (np. pilot na donglu) z Raw Input; <paramref name="DevicePath"/> to ścieżka urządzenia,
/// a <paramref name="UsagePage"/>/<paramref name="Usage"/> — surowy usage raportu (czytany tylko przez kalibrację).
/// </summary>
public sealed record HidConsumerReport(string DevicePath, long TimeMs, int UsagePage, int Usage);

/// <summary>
/// Zdarzenie klawiatury z Raw Input (<c>WM_INPUT</c>); polityka używa tylko <paramref name="DevicePath"/> i czasu,
/// a surowe <paramref name="VirtualKey"/>/<paramref name="Scan"/>/<paramref name="Down"/> czyta wyłącznie kalibracja.
/// </summary>
public sealed record RawKeyboardInput(string DevicePath, long TimeMs, int VirtualKey, int Scan, bool Down);

/// <summary>Decyzja polityki dla jednego zdarzenia: czy połknąć klawisz i jakie Actions dostarczyć.</summary>
public sealed record InputDecision(bool Swallow, IReadOnlyList<ActionMessage> Actions);

/// <summary>
/// Czysta polityka wejścia (AD-8): zamienia zdarzenia platformy na decyzję o połknięciu i Actions.
/// Połknięcie i <c>kind</c> zapadają po samym VK ze scalonego mapowania wszystkich urządzeń z <c>input.json</c>,
/// bo hook LL działa przed <c>WM_INPUT</c>, a połknięty klawisz może nie dojść do Raw Input. <c>source</c> pochodzi
/// z korelacji w oknie <see cref="CorrelationWindowMs"/>. Jedno naciśnięcie daje jedną Action, a strzałka trzymana
/// dostaje <c>repeat</c> co <c>timing.repeatIntervalMs</c> po <c>timing.repeatDelayMs</c> (tylko Up|Down|Left|Right)
/// aż do key-up albo do <see cref="MaxRepeatHoldMs"/>. OK w trybie <see cref="InputMode.Shell"/> dostaje
/// <c>press</c> na key-up przed progiem albo samo <c>long</c> po progu. Czas płynie wyłącznie przez wstrzyknięty
/// <see cref="TimeProvider"/> (AD-14). Typ nie jest wątkowo bezpieczny: App woła go z jednego wątku Input
/// (callback hooka i timer progu na tym samym wątku).
/// </summary>
public sealed class InputPolicy : IInputTarget
{
    /// <summary>Okno korelacji raportu urządzenia z klawiszem; okno jest jednostronne (raport przed klawiszem).</summary>
    public const int CorrelationWindowMs = 50;

    /// <summary>
    /// Górna granica trzymania klawisza z repeat: po niej siatka kończy się bez emisji, bo key-up mógł zginąć
    /// (secure desktop, UAC, odpięta klawiatura, usunięty hook). Klawisz zostaje połknięty do key-up.
    /// </summary>
    public const int MaxRepeatHoldMs = 60_000;

    private static readonly IReadOnlyList<ActionMessage> NoActions = Array.Empty<ActionMessage>();
    private static readonly InputDecision PassDecision = new(false, NoActions);

    private readonly TimeProvider _time;
    private readonly InputMode _mode;
    private readonly int _longPressMs;
    private readonly int _repeatDelayMs;
    private readonly int _repeatIntervalMs;
    private readonly Dictionary<int, ActionKind> _kinds = [];
    private readonly List<(string Signature, ActionSource Source)> _devices = [];
    private readonly Dictionary<int, HeldKey> _held = [];
    private PendingLong? _pendingLong;
    private (long TimeMs, ActionSource Source)? _lastConsumer;
    private (long TimeMs, ActionSource Source)? _lastKeyboard;

    /// <param name="config">Config wejścia; mapowanie jest scalane ze wszystkich urządzeń, pierwsze wygrywa.</param>
    /// <param name="time">Zegar terminów long-press i repeat (AD-14).</param>
    /// <param name="mode">Tryb routingu; w tej story stały, routing dokłada story 5.</param>
    public InputPolicy(InputConfig config, TimeProvider time, InputMode mode = InputMode.Shell)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(time);

        _time = time;
        _mode = mode;
        _longPressMs = config.Timing.LongPressMs;
        _repeatDelayMs = config.Timing.RepeatDelayMs;
        _repeatIntervalMs = config.Timing.RepeatIntervalMs;

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

    /// <summary>Najbliższy termin: próg long-press albo najbliższy <c>repeat</c>; <c>null</c>, gdy nic nie czeka.</summary>
    public DateTimeOffset? NextDeadline
    {
        get
        {
            var next = _pendingLong?.Deadline;
            foreach (var held in _held.Values)
            {
                if (held.NextRepeatAt is { } at && (next is null || at < next))
                {
                    next = at;
                }
            }

            return next;
        }
    }

    /// <summary>
    /// Jedyna decyzja o połknięciu i Actions dla zdarzenia hooka. Autorepeat systemu (drugi key-down bez key-up)
    /// jest połykany bez Actions i bez przesunięcia terminu repeat, a key-up nieznanego VK nie jest połykany.
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
            if (_held.ContainsKey(key.VirtualKey))
            {
                return new InputDecision(true, NoActions);
            }

            var source = ResolveSource(key);
            _held.Add(key.VirtualKey, NewHeldKey(kind, source, key.TimeMs));

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

        var wasHeld = _held.Remove(key.VirtualKey, out _);
        if (wasHeld && _pendingLong is { } pending && pending.VirtualKey == key.VirtualKey)
        {
            _pendingLong = null;
            return new InputDecision(true, [new ActionMessage(kind, ActionPhase.Press, pending.Source, key.TimeMs)]);
        }

        return new InputDecision(true, NoActions);
    }

    /// <summary>
    /// Buforuje ostatni raport HID Consumer dopasowany do konkretnej sygnatury z configu; wildcard nie uczestniczy
    /// w korelacji <c>source</c>, żeby raport zwykłej klawiatury nie oznaczył pilota jako <c>keyboard</c>.
    /// Nie decyduje o połknięciu.
    /// </summary>
    public void OnHidConsumer(HidConsumerReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (MatchDevice(report.DevicePath, includeWildcard: false) is { } device)
        {
            _lastConsumer = (report.TimeMs, device.Source);
        }
    }

    /// <summary>
    /// Buforuje ostatni <c>WM_INPUT</c> klawiatury dopasowany do sygnatury z configu (wildcard jest fallbackiem);
    /// nie decyduje o połknięciu.
    /// </summary>
    public void OnRawKeyboard(RawKeyboardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (MatchDevice(input.DevicePath, includeWildcard: true) is { } device)
        {
            _lastKeyboard = (input.TimeMs, device.Source);
        }
    }

    /// <summary>
    /// Callback terminu: long-press jak dotąd oraz jeden <c>repeat</c> na każdy klucz, którego termin minął.
    /// Spóźniony timer przeskakuje siatkę do najnowszego minionego terminu, a po <see cref="MaxRepeatHoldMs"/>
    /// kończy siatkę bez emisji. Timer nie jest zdarzeniem wejścia, więc nie połyka.
    /// </summary>
    public InputDecision OnTimer(DateTimeOffset now)
    {
        var actions = new List<ActionMessage>();

        if (_pendingLong is { } pending && now >= pending.Deadline)
        {
            _pendingLong = null;
            actions.Add(new ActionMessage(
                pending.Kind,
                ActionPhase.Long,
                pending.Source,
                pending.DownTimeMs + _longPressMs));
        }

        CollectRepeats(now, actions);

        return actions.Count == 0 ? PassDecision : new InputDecision(false, actions);
    }

    /// <summary>Long-press dotyczy wyłącznie OK w Shell; OSK dokłada story 5.</summary>
    private bool RequiresLongPress(ActionKind kind) => kind == ActionKind.Ok && _mode == InputMode.Shell;

    /// <summary>Wpis trzymanego klawisza: repeat uzbraja się tylko dla strzałek i dodatnich progów z configu.</summary>
    private HeldKey NewHeldKey(ActionKind kind, ActionSource source, long downTimeMs)
    {
        if (!IsRepeatable(kind) || _repeatDelayMs <= 0 || _repeatIntervalMs <= 0)
        {
            return new HeldKey(kind, source, downTimeMs, null, 0, null);
        }

        var now = _time.GetUtcNow();
        return new HeldKey(
            kind,
            source,
            downTimeMs,
            now.AddMilliseconds(_repeatDelayMs),
            0,
            now.AddMilliseconds(MaxRepeatHoldMs));
    }

    /// <summary><c>repeat</c> mają wyłącznie strzałki; OK, Back, Home i Keyboard zostają przy jednej Action.</summary>
    private static bool IsRepeatable(ActionKind kind) =>
        kind is ActionKind.Up or ActionKind.Down or ActionKind.Left or ActionKind.Right;

    /// <summary>
    /// Jeden <c>repeat</c> na każdy klucz, którego termin minął. Spóźniony timer przeskakuje siatkę do najnowszego
    /// minionego terminu (bez serii po zacięciu wątku), a przekroczenie <see cref="MaxRepeatHoldMs"/> kończy siatkę
    /// bez emisji i zostawia wpis w <c>_held</c> do key-up.
    /// </summary>
    private void CollectRepeats(DateTimeOffset now, List<ActionMessage> actions)
    {
        List<int>? due = null;
        foreach (var (virtualKey, held) in _held)
        {
            if (held.NextRepeatAt is { } nextRepeatAt && now >= nextRepeatAt)
            {
                (due ??= []).Add(virtualKey);
            }
        }

        if (due is null)
        {
            return;
        }

        // Kolejność deterministyczna: najwcześniejszy termin, a przy remisie VK.
        due.Sort((left, right) =>
        {
            var order = _held[left].NextRepeatAt!.Value.CompareTo(_held[right].NextRepeatAt!.Value);
            return order != 0 ? order : left.CompareTo(right);
        });

        foreach (var virtualKey in due)
        {
            var held = _held[virtualKey];
            if (held.NextRepeatAt is not { } nextRepeatAt)
            {
                continue;
            }

            if (held.RepeatEndsAt is { } endsAt && now >= endsAt)
            {
                _held[virtualKey] = held with { NextRepeatAt = null, RepeatEndsAt = null };
                continue;
            }

            // Ile terminów siatki minęło poza bieżącym; jeden repeat z najnowszego z nich.
            var missed = (long)(now - nextRepeatAt).TotalMilliseconds / _repeatIntervalMs;
            var index = held.RepeatIndex + (int)missed;
            actions.Add(new ActionMessage(
                held.Kind,
                ActionPhase.Repeat,
                held.Source,
                held.DownTimeMs + _repeatDelayMs + (index * (long)_repeatIntervalMs)));

            var following = nextRepeatAt.AddMilliseconds((missed + 1) * _repeatIntervalMs);
            var finished = held.RepeatEndsAt is { } limit && following >= limit;
            _held[virtualKey] = held with
            {
                NextRepeatAt = finished ? null : following,
                RepeatEndsAt = finished ? null : held.RepeatEndsAt,
                RepeatIndex = index + 1,
            };
        }
    }

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

    /// <summary>
    /// Dopasowanie ścieżki Raw Input do sygnatury z configu, bez rozróżniania wielkości liter. Wpisy konkretne mają
    /// pierwszeństwo przed <see cref="InputDevice.WildcardSignature"/> niezależnie od kolejności w configu; wildcard
    /// jest fallbackiem i w korelacji <c>source</c> z raportu HID Consumer nie bierze udziału.
    /// </summary>
    private (string Signature, ActionSource Source)? MatchDevice(string devicePath, bool includeWildcard)
    {
        if (string.IsNullOrEmpty(devicePath))
        {
            return null;
        }

        foreach (var device in _devices)
        {
            if (device.Signature != InputDevice.WildcardSignature
                && DeviceSignature.Matches(devicePath, device.Signature))
            {
                return device;
            }
        }

        if (includeWildcard)
        {
            foreach (var device in _devices)
            {
                if (device.Signature == InputDevice.WildcardSignature)
                {
                    return device;
                }
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

    /// <summary>
    /// Jeden trzymany klawisz: kind i source z key-down (repeat dziedziczy je bez nowej korelacji) oraz siatka repeat
    /// — termin następnego <c>repeat</c>, jego indeks 0-based i termin końca siatki (<see cref="MaxRepeatHoldMs"/>).
    /// Wpisy bez repeat mają <c>null</c> w terminach i służą tylko do połykania autorepeatu systemu.
    /// </summary>
    private sealed record HeldKey(
        ActionKind Kind,
        ActionSource Source,
        long DownTimeMs,
        DateTimeOffset? NextRepeatAt,
        int RepeatIndex,
        DateTimeOffset? RepeatEndsAt);
}
