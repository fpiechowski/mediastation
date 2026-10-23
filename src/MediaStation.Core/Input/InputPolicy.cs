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
/// odpowiada <c>LLKHF_INJECTED</c>, <paramref name="TimeMs"/> — polu <c>time</c> zdarzenia hooka,
/// <paramref name="Scan"/> — kod scan (surowy, czytany tylko przez kalibrację), a <paramref name="ExtraInfo"/> —
/// polu <c>dwExtraInfo</c> (znacznik własnego wstrzyknięcia, patrz <see cref="InjectionMarker"/>).
/// </summary>
public sealed record KeyStroke(
    int VirtualKey,
    KeyTransition Transition,
    bool Injected,
    long TimeMs,
    int Scan = 0,
    long ExtraInfo = 0);

/// <summary>
/// Migawka stanu pada XInput (indeks 0) z jednego odczytu: czas monotoniczny i przyciski wciśnięte w tej chwili.
/// <paramref name="Available"/> odróżnia „pad bez wciśniętych przycisków” (<c>true</c>, pusta lista) od braku
/// odczytu — brak pada albo błąd XInput (<c>false</c>); niedostępna migawka nie jest zwolnieniem przycisków (AD-8).
/// </summary>
public sealed record GamepadSnapshot(long TimeMs, IReadOnlyList<GamepadButton> Buttons, bool Available = true);

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
/// aż do key-up albo do <see cref="MaxRepeatHoldMs"/>. OK w trybie <see cref="InputMode.Shell"/> i
/// <see cref="InputMode.Osk"/> dostaje <c>press</c> na key-up przed progiem albo samo <c>long</c> po progu,
/// a wiersz <c>app</c> + <c>moonlight</c>/<c>native</c> przepuszcza D-pad i OK bez Actions. Decyzja zapada na
/// key-down i rządzi key-upem oraz autorepeatem, także po zmianie trybu w trakcie trzymania.
/// Pad (XInput) używa tej samej siatki <c>repeat</c>/<c>long</c>, ale nie ma połknięcia: wiersz AD-8 decyduje
/// wyłącznie o tym, czy przycisk daje Actions, a kombinacja <see cref="InputConfig.GamepadHome"/> daje jedno
/// <c>home</c> na pełne złożenie. Czas płynie wyłącznie przez wstrzyknięty <see cref="TimeProvider"/> (AD-14).
/// Zdarzenie ze <see cref="InjectionMarker.MediaStation"/> w <c>dwExtraInfo</c> nie daje Actions i nie jest połknięte,
/// bo to własne wstrzyknięcie MediaStation wracające z hooka, a nie naciśnięcie użytkownika.
/// Wskaźnik jest wyłącznie do połknięcia: <c>pointer: blocked</c> połyka ruch, kliknięcia i kółko, a w <c>osk</c>
/// z prostokątem <c>ClipCursor</c> połykany jest także klik poza nim; mysz nigdy nie daje Actions.
/// Typ nie jest wątkowo bezpieczny: App woła go z jednego wątku Input (callback hooka i timer na tym samym
/// wątku).
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
    private readonly InputRoutingState _routing;
    private readonly int _longPressMs;
    private readonly int _repeatDelayMs;
    private readonly int _repeatIntervalMs;
    private readonly Dictionary<int, ActionKind> _kinds = [];
    private readonly List<(string Signature, ActionSource Source)> _devices = [];
    private readonly List<(GamepadButton Button, ActionKind Kind)> _gamepadMappings = [];
    private readonly HashSet<GamepadButton> _gamepadHome = [];
    private readonly HashSet<GamepadButton> _pressed = [];
    private readonly HashSet<GamepadButton> _gamepadBaseline = [];
    private readonly Dictionary<HeldId, HeldKey> _held = [];
    private PendingLong? _pendingLong;
    private bool _homeEmitted;
    private bool _gamepadBaselinePending;
    private (long TimeMs, ActionSource Source)? _lastConsumer;
    private (long TimeMs, ActionSource Source)? _lastKeyboard;

    /// <param name="config">Config wejścia; mapowanie jest scalane ze wszystkich urządzeń, pierwsze wygrywa.</param>
    /// <param name="time">Zegar terminów long-press i repeat (AD-14).</param>
    /// <param name="routing">Współdzielona kopia routingu (Orchestrator pisze, wątek Input czyta).</param>
    public InputPolicy(InputConfig config, TimeProvider time, InputRoutingState routing)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(routing);

        _time = time;
        _routing = routing;
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

        // Brak sekcji w configu daje domyślne mapowanie; null całej sekcji (niepoprawny JSON) nie może wywrócić huba.
        if (config.Gamepad is { } gamepad)
        {
            _gamepadMappings.AddRange(gamepad.Mappings());
        }

        foreach (var button in config.GamepadHome)
        {
            _gamepadHome.Add(button);
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
    /// idzie za decyzją key-down: połknięty jest połykany bez Actions i bez przesunięcia terminu repeat,
    /// a przepuszczony zostaje przepuszczony. Key-up nieznanego VK nie jest połykany.
    /// </summary>
    public InputDecision OnKey(KeyStroke key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Własne wstrzyknięcie nie jest naciśnięciem: nie może uzbroić terminu `long`, wpisu trzymania ani zamknąć
        // cudzego trzymania, a klawisz zostaje przepuszczony. `LLKHF_INJECTED` bez znacznika nadal mapuje się normalnie.
        if (InjectionMarker.IsOurs(key.ExtraInfo))
        {
            return PassDecision;
        }

        if (!_kinds.TryGetValue(key.VirtualKey, out var kind))
        {
            return PassDecision;
        }

        if (key.Transition == KeyTransition.Down)
        {
            var id = HeldId.ForKeyboard(key.VirtualKey);
            if (_held.TryGetValue(id, out var autorepeat))
            {
                // Zgubiony key-up nie może uwięzić VK na zawsze: wpis przepuszczonego klawisza albo połkniętego bez siatki
                // repeat (Home, Back, OK, Keyboard) starszy niż MaxRepeatHoldMs jest nieświeży, więc ten key-down zaczyna
                // nowe naciśnięcie (naprawdę trzymany klawisz daje jedną Action na MaxRepeatHoldMs). Strzałka zostaje połknięta do key-up.
                if ((!autorepeat.Swallow || !IsRepeatable(kind)) && key.TimeMs - autorepeat.DownTimeMs > MaxRepeatHoldMs)
                {
                    _held.Remove(id);
                }
                else
                {
                    return new InputDecision(autorepeat.Swallow, NoActions);
                }
            }

            // Jedno odczytanie kopii na zdarzenie: cała decyzja key-down pochodzi z tego samego wiersza (AD-8).
            var routing = _routing.Current;
            var swallow = ShouldSwallow(kind, routing);
            var source = ResolveSource(key);
            _held.Add(id, NewHeldKey(kind, source, key.TimeMs, swallow));

            if (!swallow)
            {
                return PassDecision;
            }

            if (RequiresLongPress(kind, routing))
            {
                if (_pendingLong is not null)
                {
                    // Jeden termin long (AD-8): drugi klawisz OK trzymany równolegle dostaje press od razu,
                    // bo terminu pilnuje tylko jeden PendingLong.
                    return new InputDecision(true, [new ActionMessage(kind, ActionPhase.Press, source, key.TimeMs)]);
                }

                _pendingLong = new PendingLong(
                    id,
                    kind,
                    source,
                    key.TimeMs,
                    _time.GetUtcNow().AddMilliseconds(_longPressMs));
                return new InputDecision(true, NoActions);
            }

            return new InputDecision(true, [new ActionMessage(kind, ActionPhase.Press, source, key.TimeMs)]);
        }

        if (_held.Remove(HeldId.ForKeyboard(key.VirtualKey), out var held))
        {
            if (_pendingLong is { } pending && pending.Id == HeldId.ForKeyboard(key.VirtualKey))
            {
                _pendingLong = null;
                return new InputDecision(
                    held.Swallow,
                    [new ActionMessage(kind, ActionPhase.Press, pending.Source, key.TimeMs)]);
            }

            return new InputDecision(held.Swallow, NoActions);
        }

        // Key-up bez key-down (np. hook startował w trakcie trzymania) rozstrzyga bieżący wiersz kopii.
        return new InputDecision(ShouldSwallow(kind, _routing.Current), NoActions);
    }

    /// <summary>
    /// Migawka pada: krawędź naciśnięcia każdego zmapowanego przycisku daje jedną Action, a trzymanie prowadzi
    /// siatkę <c>repeat</c> (D-pad) albo termin <c>long</c> (OK) tak jak klawiatura. Kombinacja
    /// <see cref="InputConfig.GamepadHome"/> daje dokładnie jedno <c>home</c> na pełne złożenie; przerwanie
    /// kombinacji uzbraja kolejne. Decyzja zawsze <c>Swallow=false</c>: XInput jest tylko odpytywany, więc
    /// aplikacja i tak widzi przyciski, a wiersz AD-8 decyduje wyłącznie o zestawie Actions.
    /// </summary>
    public InputDecision OnGamepad(GamepadSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _pressed.Clear();
        foreach (var button in snapshot.Buttons)
        {
            _pressed.Add(button);
        }

        // Brak odczytu to nie „wszystkie przyciski zwolnione”: trzymania pada znikają bez Actions, a pierwsza
        // dostępna migawka po przerwie jest nowym stanem początkowym (bez krawędzi press i bez Home).
        if (!snapshot.Available)
        {
            DropGamepadState();
            return PassDecision;
        }

        if (_gamepadBaselinePending)
        {
            AdoptGamepadBaseline();
            return PassDecision;
        }

        var actions = new List<ActionMessage>();
        EmitGamepadHome(snapshot.TimeMs, actions);
        ReleaseGamepadButtons(snapshot.TimeMs, actions);
        PressGamepadButtons(snapshot.TimeMs, actions);

        return actions.Count == 0 ? PassDecision : new InputDecision(false, actions);
    }

    /// <summary>
    /// Brak odczytu pada: zrzuca trzymania pada (także czekający <c>long</c>) bez żadnej Action i uzbraja nowy
    /// stan początkowy dla pierwszej dostępnej migawki. Trzymania klawiatury zostają nietknięte.
    /// </summary>
    private void DropGamepadState()
    {
        List<HeldId>? gamepadHeld = null;
        foreach (var id in _held.Keys)
        {
            if (id.IsGamepad)
            {
                (gamepadHeld ??= []).Add(id);
            }
        }

        if (gamepadHeld is not null)
        {
            foreach (var id in gamepadHeld)
            {
                _held.Remove(id);
            }
        }

        if (_pendingLong is { } pending && pending.Id.IsGamepad)
        {
            _pendingLong = null;
        }

        _homeEmitted = false;
        _gamepadBaseline.Clear();
        _gamepadBaselinePending = true;
    }

    /// <summary>
    /// Pierwsza migawka po przerwie: bieżące przyciski stają się stanem początkowym, więc ich zwolnienie jest ciche,
    /// a ponowne naciśnięcie dopiero po zwolnieniu daje krawędź. Pełna kombinacja Home jest już „trzymana”, więc
    /// nie daje drugiego <c>home</c> po reconnect.
    /// </summary>
    private void AdoptGamepadBaseline()
    {
        _gamepadBaselinePending = false;
        _gamepadBaseline.Clear();

        foreach (var button in _pressed)
        {
            _gamepadBaseline.Add(button);
        }

        _homeEmitted = _gamepadHome.Count >= 2 && _gamepadHome.IsSubsetOf(_pressed);
    }

    /// <summary>
    /// Pełna kombinacja daje jedno <c>home</c>; zwolnienie choćby jednego składnika uzbraja kolejne złożenie.
    /// Home jest dostarczany w każdym wierszu AD-8, niezależnie od routingu.
    /// </summary>
    private void EmitGamepadHome(long timeMs, List<ActionMessage> actions)
    {
        if (_gamepadHome.Count < 2 || !_gamepadHome.IsSubsetOf(_pressed))
        {
            _homeEmitted = false;
            return;
        }

        if (_homeEmitted)
        {
            return;
        }

        _homeEmitted = true;
        actions.Add(new ActionMessage(ActionKind.Home, ActionPhase.Press, ActionSource.Gamepad, timeMs));
    }

    /// <summary>
    /// Zwolnienie zmapowanego przycisku pada: kończy siatkę i domyka czekający <c>long</c> jako <c>press</c>,
    /// zgodnie z decyzją naciśnięcia. Kolejność jest deterministyczna, bo przy jednym terminie long może czekać
    /// tylko jeden przycisk.
    /// </summary>
    private void ReleaseGamepadButtons(long timeMs, List<ActionMessage> actions)
    {
        _gamepadBaseline.RemoveWhere(button => !_pressed.Contains(button));

        List<HeldId>? released = null;
        foreach (var (id, _) in _held)
        {
            if (id.IsGamepad && !_pressed.Contains(id.Button))
            {
                (released ??= []).Add(id);
            }
        }

        if (released is null)
        {
            return;
        }

        released.Sort();
        foreach (var id in released)
        {
            if (!_held.Remove(id, out var held))
            {
                continue;
            }

            if (_pendingLong is { } pending && pending.Id == id)
            {
                _pendingLong = null;
                actions.Add(new ActionMessage(held.Kind, ActionPhase.Press, pending.Source, timeMs));
            }
        }
    }

    /// <summary>
    /// Krawędź naciśnięcia: przyciski kombinacji Home nie mają własnego mapowania (walidacja), a przycisk
    /// niezmapowany nie dostaje wpisu w trzymanych. Wiersz AD-8 decyduje, czy naciśnięcie daje Actions; gdy nie,
    /// wpis i tak pamięta decyzję do zwolnienia, ale nie uzbraja siatki ani terminu long.
    /// </summary>
    private void PressGamepadButtons(long timeMs, List<ActionMessage> actions)
    {
        // Jedno odczytanie kopii na migawkę: cała decyzja naciśnięć pochodzi z tego samego wiersza (AD-8).
        var routing = _routing.Current;

        foreach (var (button, kind) in _gamepadMappings)
        {
            if (!_pressed.Contains(button) || _gamepadHome.Contains(button) || _gamepadBaseline.Contains(button))
            {
                continue;
            }

            var id = HeldId.ForGamepad(button);
            if (_held.ContainsKey(id))
            {
                continue;
            }

            var producesActions = GamepadProducesActions(kind, routing);
            _held.Add(id, NewHeldKey(kind, ActionSource.Gamepad, timeMs, producesActions));
            if (!producesActions)
            {
                continue;
            }

            if (RequiresLongPress(kind, routing))
            {
                if (_pendingLong is not null)
                {
                    actions.Add(new ActionMessage(kind, ActionPhase.Press, ActionSource.Gamepad, timeMs));
                    continue;
                }

                _pendingLong = new PendingLong(
                    id,
                    kind,
                    ActionSource.Gamepad,
                    timeMs,
                    _time.GetUtcNow().AddMilliseconds(_longPressMs));
                continue;
            }

            actions.Add(new ActionMessage(kind, ActionPhase.Press, ActionSource.Gamepad, timeMs));
        }
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
    /// Zdarzenie wskaźnika z hooka myszy: jedno odczytanie kopii routingu na zdarzenie. Wiersz
    /// <see cref="PointerPolicy.Blocked"/> (<c>shell</c>, <c>app</c> + <c>web</c>) połyka ruch, kliknięcia i kółko;
    /// <see cref="PointerPolicy.Allowed"/> przepuszcza wszystko poza kliknięciem poza prostokątem clipu w trybie
    /// <c>osk</c>, bo kursor uwięziony przez <c>ClipCursor</c> i tak nie powinien trafić pod klawiaturę.
    /// Wskaźnik nigdy nie daje Actions, a zdarzenie ze znacznikiem <see cref="InjectionMarker.MediaStation"/>
    /// jest własnym wstrzyknięciem i zawsze przechodzi.
    /// </summary>
    public InputDecision OnPointer(PointerStroke pointer, ScreenRectangle? clip)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        if (InjectionMarker.IsOurs(pointer.ExtraInfo))
        {
            return PassDecision;
        }

        var routing = _routing.Current;
        if (routing.Pointer == PointerPolicy.Blocked)
        {
            return new InputDecision(true, NoActions);
        }

        var outsideClip = routing.Mode == InputMode.Osk
            && clip is { } rectangle
            && pointer.Message is PointerMessage.ButtonDown or PointerMessage.ButtonUp
            && !rectangle.Contains(pointer.X, pointer.Y);

        return outsideClip ? new InputDecision(true, NoActions) : PassDecision;
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

    /// <summary>
    /// <c>long</c> dotyczy wyłącznie OK w <see cref="InputMode.Shell"/> i <see cref="InputMode.Osk"/>; wiersz
    /// <c>app</c> dostaje <c>press</c> od razu, bo aplikacja sama obsługuje przytrzymanie.
    /// </summary>
    private static bool RequiresLongPress(ActionKind kind, InputRouting routing) =>
        kind == ActionKind.Ok && routing.Mode is InputMode.Shell or InputMode.Osk;

    /// <summary>
    /// Tabela AD-8: Home jest połknięty w każdym wierszu, a D-pad i OK przechodzą do aplikacji wyłącznie
    /// w <c>app</c> + <c>moonlight</c>/<c>native</c>. <c>app</c> bez <c>appType</c> i <c>osk</c> połykają,
    /// jak <c>shell</c>, bo tabela nie ma dla nich wiersza przepuszczającego.
    /// </summary>
    private static bool ShouldSwallow(ActionKind kind, InputRouting routing)
    {
        if (kind == ActionKind.Home)
        {
            return true;
        }

        var passesThrough = routing.Mode == InputMode.App
            && routing.AppType is AppType.Moonlight or AppType.Native
            && kind is ActionKind.Up or ActionKind.Down or ActionKind.Left or ActionKind.Right or ActionKind.Ok;

        return !passesThrough;
    }

    /// <summary>
    /// Tabela AD-8 dla pada: pad nie ma połknięcia, więc wiersz decyduje tylko o tym, czy naciśnięcie daje Actions.
    /// <c>app</c> + <c>moonlight</c> daje wyłącznie Home, <c>app</c> + <c>native</c> przepuszcza D-pad i OK, a każdy
    /// inny wiersz (także <c>app</c> bez <c>appType</c>, jak dla klawiatury) daje Actions.
    /// </summary>
    private static bool GamepadProducesActions(ActionKind kind, InputRouting routing) =>
        routing.Mode != InputMode.App
        || (routing.AppType switch
        {
            AppType.Moonlight => kind == ActionKind.Home,
            AppType.Native => kind is not (ActionKind.Up or ActionKind.Down or ActionKind.Left or ActionKind.Right or ActionKind.Ok),
            _ => true,
        });

    /// <summary>
    /// Wpis trzymanego klawisza albo przycisku pada: repeat uzbraja się tylko dla strzałek należących do MediaStation
    /// (klawisz połknięty, pad w wierszu dającym Actions) i dodatnich progów z configu, a decyzja naciśnięcia zostaje
    /// w <c>Swallow</c> na zwolnienie i autorepeat.
    /// </summary>
    private HeldKey NewHeldKey(ActionKind kind, ActionSource source, long downTimeMs, bool swallow)
    {
        if (!swallow || !IsRepeatable(kind) || _repeatDelayMs <= 0 || _repeatIntervalMs <= 0)
        {
            return new HeldKey(kind, source, downTimeMs, null, 0, null, swallow);
        }

        var now = _time.GetUtcNow();
        return new HeldKey(
            kind,
            source,
            downTimeMs,
            now.AddMilliseconds(_repeatDelayMs),
            0,
            now.AddMilliseconds(MaxRepeatHoldMs),
            swallow);
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
        List<HeldId>? due = null;
        foreach (var (id, held) in _held)
        {
            if (held.NextRepeatAt is { } nextRepeatAt && now >= nextRepeatAt)
            {
                (due ??= []).Add(id);
            }
        }

        if (due is null)
        {
            return;
        }

        // Kolejność deterministyczna: najwcześniejszy termin, a przy remisie źródło i kod (VK albo przycisk pada).
        due.Sort((left, right) =>
        {
            var order = _held[left].NextRepeatAt!.Value.CompareTo(_held[right].NextRepeatAt!.Value);
            return order != 0 ? order : left.CompareTo(right);
        });

        foreach (var id in due)
        {
            var held = _held[id];
            if (held.NextRepeatAt is not { } nextRepeatAt)
            {
                continue;
            }

            if (held.RepeatEndsAt is { } endsAt && now >= endsAt)
            {
                _held[id] = held with { NextRepeatAt = null, RepeatEndsAt = null };
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
            _held[id] = held with
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

    /// <summary>
    /// Tożsamość trzymanego wejścia: klawiatura (VK z hooka) albo pad (przycisk XInput), żeby jedna siatka
    /// <c>repeat</c>/<c>long</c> obsługiwała oba źródła. <see cref="IComparable{T}"/> daje deterministyczną kolejność
    /// przy remisie terminów.
    /// </summary>
    private readonly record struct HeldId(int Family, int Code) : IComparable<HeldId>
    {
        private const int KeyboardFamily = 0;
        private const int GamepadFamily = 1;

        public static HeldId ForKeyboard(int virtualKey) => new(KeyboardFamily, virtualKey);

        public static HeldId ForGamepad(GamepadButton button) => new(GamepadFamily, (int)button);

        public bool IsGamepad => Family == GamepadFamily;

        public GamepadButton Button => (GamepadButton)Code;

        public int CompareTo(HeldId other) =>
            Family != other.Family ? Family.CompareTo(other.Family) : Code.CompareTo(other.Code);
    }

    /// <summary>Jeden czekający long-press: kind, source, czas naciśnięcia i termin na <see cref="TimeProvider"/>.</summary>
    private sealed record PendingLong(
        HeldId Id,
        ActionKind Kind,
        ActionSource Source,
        long DownTimeMs,
        DateTimeOffset Deadline);

    /// <summary>
    /// Jeden trzymany klawisz albo przycisk pada: kind i source z naciśnięcia (repeat dziedziczy je bez nowej
    /// korelacji), decyzja naciśnięcia oraz siatka repeat — termin następnego <c>repeat</c>, jego indeks 0-based
    /// i termin końca siatki (<see cref="MaxRepeatHoldMs"/>). Wpisy bez repeat mają <c>null</c> w terminach i służą
    /// tylko do prowadzenia zwolnienia za decyzją naciśnięcia (dla klawiatury także do połykania autorepeatu systemu).
    /// </summary>
    private sealed record HeldKey(
        ActionKind Kind,
        ActionSource Source,
        long DownTimeMs,
        DateTimeOffset? NextRepeatAt,
        int RepeatIndex,
        DateTimeOffset? RepeatEndsAt,
        bool Swallow);
}
