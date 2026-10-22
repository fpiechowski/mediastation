using System;
using System.Collections.Generic;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Ports;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Input;

/// <summary>
/// Kalibracja wejścia (AD-13) jako <see cref="IInputTarget"/>: loguje surowe kody wyłącznie dla przycisków
/// urządzeń <c>source: remote</c> z <c>input.json</c>, nie mapuje i nie połyka klawiszy. Jednostką wpisu jest jedno
/// naciśnięcie, nie jedno zdarzenie: raport HID Consumer (usage), hook LL (VK, scan, <c>injected</c>) i
/// <c>WM_INPUT</c> klawiatury dongla tej samej sygnatury są scalane w oknie <see cref="MergeWindowMs"/> i domykane
/// timerem <c>flushAt</c>, więc kolejność źródeł nie ma znaczenia. Hook bez zdarzenia urządzenia <c>remote</c>
/// w oknie jest tylko buforowany w pamięci i nigdy nie trafia do logu (zwykła klawiatura, AD-13).
/// Tryb kończy się po <see cref="DurationSeconds"/> sekundach albo na fizyczny Esc, zawsze bez połknięcia.
/// Czas płynie wyłącznie przez wstrzyknięty <see cref="TimeProvider"/> (AD-14); typ nie jest wątkowo bezpieczny —
/// App woła go z jednego wątku Input.
/// </summary>
public sealed class InputCalibration : IInputTarget
{
    /// <summary>Długość trybu kalibracji; po niej tryb kończy się sam.</summary>
    public const int DurationSeconds = 60;

    /// <summary>Kod VK fizycznego Esc, który kończy tryb (Esc wstrzyknięty przez pilota go nie kończy).</summary>
    public const int EscapeVirtualKey = 0x1B;

    /// <summary>Okno scalania zdarzeń jednego naciśnięcia; ten sam próg co <see cref="InputPolicy.CorrelationWindowMs"/>.</summary>
    public const int MergeWindowMs = 50;

    /// <summary>
    /// Jak długo hook czeka w pamięci na zdarzenie urządzenia <c>remote</c>. Po tym czasie nie może już należeć do
    /// żadnego naciśnięcia, więc jest usuwany bez wpisu (AD-13).
    /// </summary>
    private const long HookRetentionMs = 8L * MergeWindowMs;

    /// <summary>Górna granica bufora hooków; chroni pamięć przy nietypowym zegarze zdarzeń.</summary>
    private const int HookBufferLimit = 64;

    private static readonly InputDecision PassDecision = new(false, Array.Empty<ActionMessage>());

    private readonly TimeProvider _time;
    private readonly IRawInputSink _sink;
    private readonly List<string> _remoteSignatures = [];
    private readonly List<KeyStroke> _hooks = [];
    private readonly HashSet<int> _heldUsages = [];
    private readonly HashSet<int> _heldVks = [];
    private readonly HashSet<int> _heldRemoteVks = [];
    private readonly HashSet<int> _heldRawVks = [];
    private readonly DateTimeOffset _endsAt;

    private PendingPress? _pending;
    private bool _completed;

    /// <param name="config">Config wejścia; filtr bierze z niego wyłącznie urządzenia <c>source: remote</c>.</param>
    /// <param name="time">Zegar końca trybu i terminu domknięcia wpisu (AD-14).</param>
    /// <param name="sink">Port wpisów kalibracji; wołany z wątku Input, poza callbackiem hooka.</param>
    public InputCalibration(InputConfig config, TimeProvider time, IRawInputSink sink)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(sink);

        _time = time;
        _sink = sink;
        _endsAt = time.GetUtcNow().AddSeconds(DurationSeconds);

        foreach (var device in config.Devices)
        {
            if (device is not null && device.Source == ActionSource.Remote
                && !string.IsNullOrWhiteSpace(device.Signature))
            {
                _remoteSignatures.Add(device.Signature);
            }
        }
    }

    /// <summary>
    /// Zgłaszane po Esc albo po upływie <see cref="DurationSeconds"/>; App czeka na nie z ograniczeniem, bo w tym
    /// trybie nie ma okna ani pętli UI. Wołane z wątku Input i tylko raz.
    /// </summary>
    public event System.Action? Completed;

    /// <summary>Najbliższy termin: domknięcie otwartego wpisu albo koniec trybu; <c>null</c> po zakończeniu.</summary>
    public DateTimeOffset? NextDeadline
    {
        get
        {
            if (_completed)
            {
                return null;
            }

            return _pending is { } pending && pending.FlushAt < _endsAt ? pending.FlushAt : _endsAt;
        }
    }

    /// <summary>
    /// Zdarzenie hooka: nigdy nie połyka i nie zwraca Actions. Fizyczny Esc kończy tryb, key-up zwalnia znaczniki
    /// trzymania, a key-down trafia do bufora i — jeśli należy do otwartego naciśnięcia — uzupełnia jego VK, scan
    /// i <c>injected</c>. Autorepeat (VK już trzymany) jest ignorowany.
    /// </summary>
    public InputDecision OnKey(KeyStroke key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_completed)
        {
            return PassDecision;
        }

        if (key.VirtualKey == EscapeVirtualKey && !key.Injected)
        {
            // Esc kończy tryb; otwarty wpis zostaje porzucony, bo logowanie w callbacku hooka łamałoby AD-2/AD-8.
            if (key.Transition == KeyTransition.Down)
            {
                Complete();
            }

            return PassDecision;
        }

        if (key.Transition == KeyTransition.Up)
        {
            Release(key.VirtualKey);
            return PassDecision;
        }

        PruneHooks(key.TimeMs);
        if (_pending is { } pending && Fits(pending, key))
        {
            AttachHook(pending, key);

            // VK dopięty do wpisu należy do pilota: bez tego jego key-up nie zwolniłby znacznika usage.
            _heldVks.Add(key.VirtualKey);
            _heldRemoteVks.Add(key.VirtualKey);
            return PassDecision;
        }

        if (_heldVks.Add(key.VirtualKey))
        {
            BufferHook(key);
        }

        return PassDecision;
    }

    /// <summary>Migawka pada: kalibracja mapuje wyłącznie urządzenia <c>remote</c>, więc zawsze przepuszcza bez Actions.</summary>
    public InputDecision OnGamepad(GamepadSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return PassDecision;
    }

    /// <summary>
    /// Raport HID Consumer urządzenia <c>remote</c>: nowy usage otwiera (albo domyka poprzednie i otwiera nowe)
    /// naciśnięcie, a <c>usage == 0</c> to zwolnienie przycisku według konwencji HID Consumer i nie tworzy wpisu.
    /// </summary>
    public void OnHidConsumer(HidConsumerReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (_completed)
        {
            return;
        }

        if (MatchRemote(report.DevicePath) is not { } signature)
        {
            return;
        }

        if (report.Usage == 0)
        {
            ReleaseAll();
            return;
        }

        if (!_heldUsages.Add(report.Usage))
        {
            return;
        }

        PruneHooks(report.TimeMs);
        var press = Merge(signature, report.TimeMs, usage: report.Usage);
        press.UsagePage ??= report.UsagePage;
        press.Usage ??= report.Usage;
        AttachBufferedHook(press);
    }

    /// <summary>
    /// <c>WM_INPUT</c> klawiatury urządzenia <c>remote</c>: key-down otwiera albo uzupełnia naciśnięcie (VK, scan,
    /// czas <c>WM_INPUT</c>), key-up zwalnia znaczniki trzymania i nie tworzy wpisu. Powtórki trzymanego VK są
    /// deduplikowane.
    /// </summary>
    public void OnRawKeyboard(RawKeyboardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_completed)
        {
            return;
        }

        if (MatchRemote(input.DevicePath) is not { } signature)
        {
            return;
        }

        // Zdarzenie bez VK (np. ciało RAWKEYBOARD nie do odczytania) nie jest przyciskiem i nie zwalnia znaczników.
        if (input.VirtualKey == 0)
        {
            return;
        }

        if (!input.Down)
        {
            Release(input.VirtualKey);
            return;
        }

        if (!_heldRawVks.Add(input.VirtualKey))
        {
            return;
        }

        PruneHooks(input.TimeMs);
        var press = Merge(signature, input.TimeMs, virtualKey: input.VirtualKey);
        press.VirtualKey ??= input.VirtualKey;
        press.Scan ??= input.Scan;
        press.RawTimeMs ??= input.TimeMs;
        AttachBufferedHook(press);
    }

    /// <summary>
    /// Callback terminu: domyka otwarty wpis po <c>flushAt</c>, a po <see cref="DurationSeconds"/> kończy tryb
    /// (domykając wpis przed końcem, bo timer nie jest callbackiem hooka). Nie połyka i nie zwraca Actions.
    /// </summary>
    public InputDecision OnTimer(DateTimeOffset now)
    {
        if (_completed)
        {
            return PassDecision;
        }

        if (_pending is { } pending && (now >= pending.FlushAt || now >= _endsAt))
        {
            // Koniec trybu domyka wpis bezwarunkowo: naciśnięcie z ostatniego okna scalania nie może zginąć.
            Flush(pending);
        }

        if (now >= _endsAt)
        {
            Complete();
        }

        return PassDecision;
    }

    /// <summary>
    /// Sygnatura urządzenia <c>remote</c> pasująca do ścieżki Raw Input; <c>null</c> dla zwykłej klawiatury
    /// i urządzeń spoza configu (AD-13).
    /// </summary>
    private string? MatchRemote(string devicePath)
    {
        foreach (var signature in _remoteSignatures)
        {
            if (DeviceSignature.Matches(devicePath, signature))
            {
                return signature;
            }
        }

        return null;
    }

    /// <summary>
    /// Wspólne wejście zdarzenia urządzenia: dołącza do otwartego naciśnięcia tej samej sygnatury w oknie
    /// <see cref="MergeWindowMs"/>, inaczej domyka poprzednie i otwiera nowe z terminem <c>flushAt</c>.
    /// Inny usage albo inny VK w oknie to już kolejne naciśnięcie: scalenie dwóch różnych przycisków zgubiłoby
    /// jeden z nich z logu, a wpis ma odpowiadać jednemu przyciskowi.
    /// </summary>
    private PendingPress Merge(string signature, long eventTimeMs, int? usage = null, int? virtualKey = null)
    {
        if (_pending is { } pending
            && pending.Signature == signature
            && WithinWindow(pending.FirstEventTimeMs, eventTimeMs)
            && !Conflicts(pending, usage, virtualKey))
        {
            return pending;
        }

        if (_pending is { } previous)
        {
            // Nowe naciśnięcie domyka poprzednie; wpis zamyka timer, więc nie czeka na kolejną rundę.
            Flush(previous);
        }

        var created = new PendingPress(signature, eventTimeMs, _time.GetUtcNow().AddMilliseconds(MergeWindowMs));
        _pending = created;
        return created;
    }

    /// <summary>Czy zdarzenie niesie usage albo VK inny niż ten, który już jest w otwartym naciśnięciu?</summary>
    private static bool Conflicts(PendingPress press, int? usage, int? virtualKey) =>
        (usage is { } newUsage && press.Usage is { } pressUsage && pressUsage != newUsage)
        || (virtualKey is { } newVirtualKey && press.VirtualKey is { } pressVirtualKey && pressVirtualKey != newVirtualKey);

    /// <summary>Domyka wpis, uzupełnia brakujący hook z bufora i oddaje go do sinka.</summary>
    private void Flush(PendingPress press)
    {
        _pending = null;
        AttachBufferedHook(press);

        _sink.Deliver(new RawInputEntry(
            press.Signature,
            press.VirtualKey,
            press.Scan,
            press.UsagePage,
            press.Usage,
            press.Injected,
            press.VirtualKey is not null,
            BeforeRaw(press)));
    }

    /// <summary>
    /// <c>rawTime − hookTime</c> z ograniczeniem do <see cref="int"/>; <c>null</c>, gdy jednego ze źródeł nie było.
    /// </summary>
    private static int? BeforeRaw(PendingPress press) =>
        press.HookTimeMs is { } hookTime && press.RawTimeMs is { } rawTime
            ? (int)Math.Clamp(rawTime - hookTime, int.MinValue, int.MaxValue)
            : null;

    /// <summary>Czy hook pasuje do otwartego naciśnięcia: tylko wstrzyknięty (AD-8), to samo okno i — gdy znamy VK — ten sam VK.</summary>
    private static bool Fits(PendingPress press, KeyStroke key) =>
        press.Injected is null
        && key.Injected
        && WithinWindow(press.FirstEventTimeMs, key.TimeMs)
        && (press.VirtualKey is not { } virtualKey || virtualKey == key.VirtualKey);

    /// <summary>
    /// Dokłada zdarzenie hooka do wpisu: VK i scan tylko gdy <c>WM_INPUT</c> ich nie dał, a <c>injected</c>
    /// i czas hooka zawsze, bo pochodzą wyłącznie z hooka. Wołane tylko z wstrzykniętym hookiem (AD-8).
    /// </summary>
    private static void AttachHook(PendingPress press, KeyStroke key)
    {
        press.VirtualKey ??= key.VirtualKey;
        press.Scan ??= key.Scan;
        press.Injected ??= key.Injected;
        press.HookTimeMs ??= key.TimeMs;
    }

    /// <summary>
    /// Bierze z bufora najbliższy hook w oknie naciśnięcia; gdy wpis zna już VK, tylko hook tego samego VK.
    /// </summary>
    private void AttachBufferedHook(PendingPress press)
    {
        if (press.Injected is not null)
        {
            return;
        }

        var reference = press.RawTimeMs ?? press.FirstEventTimeMs;
        var index = -1;
        long best = long.MaxValue;
        for (var i = 0; i < _hooks.Count; i++)
        {
            var hook = _hooks[i];
            if (!hook.Injected)
            {
                continue;
            }

            if (press.VirtualKey is { } virtualKey && hook.VirtualKey != virtualKey)
            {
                continue;
            }

            var distance = Math.Abs(hook.TimeMs - reference);
            if (distance > MergeWindowMs || distance >= best)
            {
                continue;
            }

            best = distance;
            index = i;
        }

        if (index < 0)
        {
            return;
        }

        var matched = _hooks[index];
        _hooks.RemoveAt(index);
        AttachHook(press, matched);
    }

    private void BufferHook(KeyStroke key)
    {
        _hooks.Add(key);
        while (_hooks.Count > HookBufferLimit)
        {
            _hooks.RemoveAt(0);
        }
    }

    /// <summary>Usuwa hooki, które są starsze niż okno skorelowania z jakimkolwiek przyszłym zdarzeniem.</summary>
    private void PruneHooks(long referenceTimeMs)
    {
        var oldest = referenceTimeMs - HookRetentionMs;
        _hooks.RemoveAll(hook => hook.TimeMs < oldest);
    }

    /// <summary>
    /// Zwolnienie klawisza z hooka albo <c>WM_INPUT</c>: zdejmuje VK, a znacznik usage czyści tylko wtedy, gdy
    /// zwolniony VK należał do naciśnięcia pilota. Key-up zwykłej klawiatury (albo VK tylko zbuforowany bez
    /// urządzenia) nie może wyczyścić trzymanego przycisku pilota (AD-13), bo powtórzony raport HID dałby wtedy
    /// drugi wpis tego samego naciśnięcia.
    /// </summary>
    private void Release(int virtualKey)
    {
        _heldVks.Remove(virtualKey);

        var remote = _heldRemoteVks.Remove(virtualKey);
        remote |= _heldRawVks.Remove(virtualKey);

        if (remote)
        {
            _heldUsages.Clear();
        }
    }

    /// <summary>Raport HID z <c>usage == 0</c> zwalnia przycisk: czyści znaczniki trzymania obu źródeł.</summary>
    private void ReleaseAll()
    {
        _heldUsages.Clear();
        _heldVks.Clear();
        _heldRemoteVks.Clear();
        _heldRawVks.Clear();
    }

    /// <summary>Kończy tryb raz; <c>Completed</c> budzi App czekający na koniec kalibracji.</summary>
    private void Complete()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        Completed?.Invoke();
    }

    private static bool WithinWindow(long firstEventTimeMs, long eventTimeMs) =>
        Math.Abs(eventTimeMs - firstEventTimeMs) <= MergeWindowMs;

    /// <summary>
    /// Jedno naciśnięcie w trakcie scalania: sygnatura urządzenia, czas pierwszego zdarzenia, termin domknięcia
    /// oraz surowe pola uzupełniane przez kolejne źródła.
    /// </summary>
    private sealed class PendingPress(string signature, long firstEventTimeMs, DateTimeOffset flushAt)
    {
        public string Signature { get; } = signature;

        public long FirstEventTimeMs { get; } = firstEventTimeMs;

        public DateTimeOffset FlushAt { get; } = flushAt;

        public int? UsagePage { get; set; }

        public int? Usage { get; set; }

        public int? VirtualKey { get; set; }

        public int? Scan { get; set; }

        public bool? Injected { get; set; }

        public long? HookTimeMs { get; set; }

        public long? RawTimeMs { get; set; }
    }
}
