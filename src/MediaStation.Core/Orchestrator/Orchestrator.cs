using System;
using System.Collections.Generic;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Orchestrator;

/// <summary>
/// Wynik jednego kroku Orchestratora: komendy do wykonawców i opcjonalnie nowy snapshot dla shella.
/// <see cref="Snapshot"/> jest <c>null</c>, gdy komunikat nie zmienia widoku.
/// </summary>
public sealed record OrchestratorResult(IReadOnlyList<Command> Commands, ShellSnapshot? Snapshot)
{
    /// <summary>Brak komend i brak snapshotu — komunikat zignorowany w tej story.</summary>
    public static OrchestratorResult None { get; } = new(Array.Empty<Command>(), null);
}

/// <summary>
/// Jedyny właściciel stanu sesji (AD-3): czysty handler komunikatu na komendy i snapshot (AD-14, AD-16).
/// Bez UI, I/O i czasu ambientowego — zegar wchodzi wyłącznie przez wstrzyknięty <see cref="TimeProvider"/>;
/// woła go wyłącznie jedna pętla w App, więc stan nie potrzebuje blokad.
/// </summary>
public sealed class Orchestrator
{
    private readonly List<AppState> _apps;
    private readonly Dictionary<string, AppState> _appsById;
    private readonly Dictionary<long, string> _pendingStarts = [];
    private readonly Dictionary<long, PendingStop> _pendingStops = [];
    private readonly Dictionary<long, string> _startTimers = [];
    private readonly TimeProvider _time;
    private readonly TimeSpan _startingTimeout;
    private long _rev;
    private long _focusSeq;
    private long _cmdId;
    private long _timerId;
    private long _epoch;
    private FocusRequest? _focus;

    public Orchestrator(LoadedConfig config, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(time);

        _time = time;
        _startingTimeout = TimeSpan.FromSeconds(config.Settings.StartingTimeout);
        _apps = new List<AppState>(config.Apps.Apps.Count);
        _appsById = new Dictionary<string, AppState>(config.Apps.Apps.Count, StringComparer.Ordinal);
        foreach (var app in config.Apps.Apps)
        {
            var (type, pointer) = Describe(app);
            var state = new AppState(app.Id, app.Category, type, pointer);
            _apps.Add(state);
            _appsById.Add(state.AppId, state);
        }
    }

    /// <summary>
    /// Przetwarza jeden komunikat z kolejki. <c>rev</c> rośnie przy każdej zmianie snapshotu, a <c>seq</c>
    /// tylko przy nowym żądaniu fokusu. Komendy fire-and-forget dostają kolejny <c>cmdId</c> Orchestratora,
    /// a routing wejścia nowy <c>epoch</c> (AD-14).
    /// </summary>
    public OrchestratorResult Handle(IOrchestratorMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message switch
        {
            OrchestratorStarted => OnOrchestratorStarted(),
            SystemResumed => OnDefaultFocus(),
            ActionMessage action => OnHome(action),
            LaunchApp launch => OnLaunchApp(launch),
            CloseApp close => OnStop(close.AppId, restart: false),
            RestartApp restart => OnStop(restart.AppId, restart: true),
            AppWindowAttached attached => OnAppWindowAttached(attached),
            ProcessExited exited => OnProcessExited(exited),
            CdpLost lost => OnCdpLost(lost),
            TimerElapsed elapsed => OnTimerElapsed(elapsed),
            CommandCompleted completed => OnCommandCompleted(completed),
            CommandFailed failed => OnCommandFailed(failed),
            _ => OrchestratorResult.None,
        };
    }

    /// <summary>
    /// Start pętli: snapshot jak przy wybudzeniu plus <see cref="SetInputRouting"/> z trybem <c>shell</c>.
    /// Drugi start też wysyła routing z nowym <c>epoch</c>, bo to jedyny przypadek powtórzenia trybu.
    /// </summary>
    private OrchestratorResult OnOrchestratorStarted()
    {
        var routing = new SetInputRouting(NextCmdId(), InputMode.Shell, null, PointerPolicy.Blocked, NextEpoch());
        return OnDefaultFocus([routing]);
    }

    /// <summary>
    /// Ekran główny z nowym <c>seq</c> fokusu. Story 7 (sondowanie istniejących hostów) dokłada osobną
    /// gałąź <c>switch</c>, nie warunek tutaj.
    /// </summary>
    private OrchestratorResult OnDefaultFocus(IReadOnlyList<Command>? commands = null)
    {
        _focusSeq++;
        _focus = new FocusRequest(FocusRequest.DefaultTarget, _focusSeq);
        return new OrchestratorResult(commands ?? Array.Empty<Command>(), Snapshot());
    }

    /// <summary>
    /// Wybór kafelka uruchamia aplikację w <c>NotRunning</c> (<c>cmdId</c> zostaje w korelacji startu) i planuje
    /// timeout <c>Starting</c> na <c>startingTimeout</c>, albo przywraca ją z <c>Background</c>. Przełączanie między
    /// dwiema żywymi aplikacjami i adopcję (story 6/7) dokładają osobne gałęzie.
    /// </summary>
    private OrchestratorResult OnLaunchApp(LaunchApp launch)
    {
        if (!_appsById.TryGetValue(launch.AppId, out var app))
        {
            return OrchestratorResult.None;
        }

        if (app.State == AppLifecycleState.Background)
        {
            return app.Hwnd is { } hwnd ? OnResume(app, hwnd) : OrchestratorResult.None;
        }

        if (app.State != AppLifecycleState.NotRunning)
        {
            return OrchestratorResult.None;
        }

        var cmdId = NextCmdId();
        app.State = AppLifecycleState.Starting;
        _pendingStarts.Add(cmdId, app.AppId);
        return new OrchestratorResult([new StartApp(cmdId, app.AppId), ScheduleStartTimeout(app.AppId)], Snapshot());
    }

    /// <summary>
    /// Powrót z tła: proces żyje, więc wystarczą <see cref="ActivateWindow"/> i routing wejścia aplikacji.
    /// Guard „inna aplikacja w <c>Foreground</c>/<c>Starting</c>” chroni niezmiennik jednej aplikacji
    /// na pierwszym planie; politykę przełączania ustala story 6.
    /// </summary>
    private OrchestratorResult OnResume(AppState app, long hwnd)
    {
        if (AnotherAppIsActive(app))
        {
            return OrchestratorResult.None;
        }

        app.State = AppLifecycleState.Foreground;
        var activate = new ActivateWindow(NextCmdId(), hwnd);
        var routing = new SetInputRouting(NextCmdId(), InputMode.App, app.Type, app.Pointer, NextEpoch());
        return new OrchestratorResult([activate, routing], Snapshot());
    }

    /// <summary>
    /// Home z pierwszego planu: aplikacja schodzi do <c>Background</c>, shell wraca na wierzch, a InputHub
    /// dostaje tryb <c>shell</c>. Kolejność komend jest istotna (AD-5/AD-15): najpierw minimalizacja,
    /// potem <see cref="ShowShell"/>, na końcu routing. Fokusu nie zmieniamy — UI sam wraca na ostatni kafelek.
    /// Home bez aplikacji w <c>Foreground</c> (także w <c>Starting</c>/<c>Stopping</c>) jest ignorowany.
    /// </summary>
    private OrchestratorResult OnHome(ActionMessage action)
    {
        if (action.Kind != ActionKind.Home)
        {
            return OrchestratorResult.None;
        }

        var app = _apps.Find(candidate => candidate.State == AppLifecycleState.Foreground);
        if (app?.Hwnd is not { } hwnd)
        {
            return OrchestratorResult.None;
        }

        app.State = AppLifecycleState.Background;
        var minimize = new MinimizeWindow(NextCmdId(), hwnd);
        var showShell = new ShowShell(NextCmdId());
        var routing = new SetInputRouting(NextCmdId(), InputMode.Shell, null, PointerPolicy.Blocked, NextEpoch());
        return new OrchestratorResult([minimize, showShell, routing], Snapshot());
    }

    /// <summary>
    /// Okno po starcie daje <c>Foreground</c>: aktywacja okna i routing wejścia według tabeli AD-8.
    /// <c>hwnd</c> zostaje w stanie aplikacji, bo Home minimalizuje po nim okno, a powrót z tła je aktywuje.
    /// Poza <c>Starting</c> fakt jest ignorowany; adopcję (story 7) dokłada osobna gałąź.
    /// </summary>
    private OrchestratorResult OnAppWindowAttached(AppWindowAttached attached)
    {
        if (!_appsById.TryGetValue(attached.AppId, out var app) || app.State != AppLifecycleState.Starting)
        {
            return OrchestratorResult.None;
        }

        app.State = AppLifecycleState.Foreground;
        app.Hwnd = attached.Hwnd;
        var activate = new ActivateWindow(NextCmdId(), attached.Hwnd);
        var routing = new SetInputRouting(NextCmdId(), InputMode.App, app.Type, app.Pointer, NextEpoch());
        var commands = new List<Command> { activate, routing };

        // Okno zdążyło przed deadlinem: timeout `Starting` traci znaczenie, a scheduler dostaje jawne anulowanie.
        if (CancelStartTimeout(app.AppId) is { } cancel)
        {
            commands.Add(cancel);
        }

        return new OrchestratorResult(commands, Snapshot());
    }

    /// <summary>
    /// Zamknięcie albo restart aplikacji z <c>Background</c> albo <c>Failed</c>: Orchestrator przechodzi w <c>Stopping</c>
    /// i wysyła <c>StopApp</c> z <c>cmdId</c> w korelacji stopu. <c>Failed</c> wychodzi więc wyłącznie przez stop:
    /// potwierdzony stop kończy się <c>NotRunning</c> albo nowym <c>Starting</c>. Poza tymi stanami intencja jest
    /// ignorowana, podobnie gdy innemu zadaniu należy się pierwszy plan: restart skończyłby się w <c>Starting</c>
    /// → <c>Foreground</c>, czyli w dwóch aplikacjach naraz.
    /// </summary>
    private OrchestratorResult OnStop(string appId, bool restart)
    {
        if (!_appsById.TryGetValue(appId, out var app)
            || app.State is not (AppLifecycleState.Background or AppLifecycleState.Failed)
            || AnotherAppIsActive(app))
        {
            return OrchestratorResult.None;
        }

        var cmdId = NextCmdId();
        app.State = AppLifecycleState.Stopping;
        _pendingStops.Add(cmdId, new PendingStop(app.AppId, restart));
        return new OrchestratorResult([new StopApp(cmdId, app.AppId)], Snapshot());
    }

    /// <summary>
    /// Niezmiennik „jedna aplikacja na pierwszym planie”: inna aplikacja w <c>Foreground</c>/<c>Starting</c>
    /// blokuje powrót z tła, zamknięcie i restart. Politykę przełączania (story 6) to nie jest.
    /// </summary>
    private bool AnotherAppIsActive(AppState app) =>
        _apps.Exists(other => !ReferenceEquals(other, app)
            && other.State is AppLifecycleState.Foreground or AppLifecycleState.Starting);

    /// <summary>
    /// Sukces komendy domyka najpierw stop (<c>Stopping</c> → <c>NotRunning</c> albo <c>Starting</c> przy restarcie),
    /// a dopiero potem korelację startu. Sukces startu nie zmienia stanu: <c>Foreground</c> daje
    /// <see cref="AppWindowAttached"/>.
    /// </summary>
    private OrchestratorResult OnCommandCompleted(CommandCompleted completed)
    {
        if (_pendingStops.Remove(completed.CmdId, out var stopped))
        {
            return OnStopped(stopped);
        }

        _pendingStarts.Remove(completed.CmdId);
        return OrchestratorResult.None;
    }

    /// <summary>
    /// Porażka stopu nie jest dowodem, że proces zniknął: stan zostaje <c>Stopping</c>, a korelacja czeka
    /// na <see cref="ProcessExited"/> (timeout należy do story 11). Nieudany <c>StartApp</c> przenosi aplikację
    /// w <c>Failed</c> także wtedy, gdy okno zdążyło już dać <c>Foreground</c>/<c>Background</c>; porażka komendy
    /// spoza korelacji startu nie zmienia stanu.
    /// </summary>
    private OrchestratorResult OnCommandFailed(CommandFailed failed)
    {
        if (_pendingStarts.Remove(failed.CmdId, out var appId)
            && _appsById.TryGetValue(appId, out var app)
            && app.State is AppLifecycleState.Starting or AppLifecycleState.Foreground or AppLifecycleState.Background)
        {
            return EnterFailed(app, wasForeground: app.State == AppLifecycleState.Foreground);
        }

        return OrchestratorResult.None;
    }

    /// <summary>
    /// Zakończony proces domyka <c>Stopping</c> tak samo jak potwierdzony <see cref="StopApp"/>.
    /// W żywym cyklu (<c>Starting</c>, <c>Foreground</c>, <c>Background</c>) fakt jest awarią i prowadzi w <c>Failed</c>;
    /// <c>NotRunning</c>, <c>Failed</c> i <c>Stopping</c> bez korelacji stopu ignorują go, bo spóźniony fakt nie może
    /// wskrzesić kafelka.
    /// </summary>
    private OrchestratorResult OnProcessExited(ProcessExited exited)
    {
        if (!_appsById.TryGetValue(exited.AppId, out var app))
        {
            return OrchestratorResult.None;
        }

        if (app.State == AppLifecycleState.Stopping)
        {
            if (FindPendingStop(app.AppId) is not { } cmdId)
            {
                return OrchestratorResult.None;
            }

            var pending = _pendingStops[cmdId];
            _pendingStops.Remove(cmdId);
            return OnStopped(pending);
        }

        return app.State is AppLifecycleState.Starting or AppLifecycleState.Foreground or AppLifecycleState.Background
            ? EnterFailed(app, wasForeground: app.State == AppLifecycleState.Foreground)
            : OrchestratorResult.None;
    }

    /// <summary>
    /// Zerwane CDP to awaria tylko w żywym cyklu: <c>Starting</c>, <c>Foreground</c> i <c>Background</c> prowadzą
    /// w <c>Failed</c> (AD-10). <c>Stopping</c> należy do korelacji stopu, a <c>NotRunning</c> i <c>Failed</c>
    /// nie mają żywego procesu, którego awarię mógłby raportować.
    /// </summary>
    private OrchestratorResult OnCdpLost(CdpLost lost)
    {
        if (!_appsById.TryGetValue(lost.AppId, out var app))
        {
            return OrchestratorResult.None;
        }

        return app.State is AppLifecycleState.Starting or AppLifecycleState.Foreground or AppLifecycleState.Background
            ? EnterFailed(app, wasForeground: app.State == AppLifecycleState.Foreground)
            : OrchestratorResult.None;
    }

    /// <summary>
    /// Timeout <c>Starting</c>: o żywotności terminu decyduje wyłącznie korelacja <c>timerId → appId</c>, więc
    /// spóźniony <see cref="TimerElapsed"/> po <see cref="AppWindowAttached"/> albo po wejściu w <c>Failed</c>
    /// nie daje komend ani snapshotu. Żywy termin kończy start w <c>Failed</c> bez shella i fokusu (nie ma okna
    /// na pierwszym planie). Korelacja znika przed <see cref="EnterFailed"/>, więc timeout nie anuluje sam siebie.
    /// </summary>
    private OrchestratorResult OnTimerElapsed(TimerElapsed elapsed)
    {
        if (!_startTimers.Remove(elapsed.TimerId, out var appId))
        {
            return OrchestratorResult.None;
        }

        return _appsById.TryGetValue(appId, out var app) && app.State == AppLifecycleState.Starting
            ? EnterFailed(app, wasForeground: false)
            : OrchestratorResult.None;
    }

    /// <summary>
    /// Wspólne wejście w <c>Failed</c>: okno po awarii nie jest już pod kontrolą Orchestratora, więc <c>Hwnd</c> znika,
    /// a korelacje startu przepadają — spóźniony wynik startu nie może już zmienić stanu. Timeout <c>Starting</c>
    /// jest anulowany ostatnią komendą batcha, bo zegar nie ma już czego pilnować. Awaria na pierwszym planie
    /// pokazuje shell i oddaje mu wejście (AD-5/AD-15), a fokus z nowym <c>seq</c> wskazuje kafelek, który padł (AD-16).
    /// Kolejność komend jest istotna: najpierw <see cref="ShowShell"/>, potem routing, na końcu timer.
    /// </summary>
    private OrchestratorResult EnterFailed(AppState app, bool wasForeground)
    {
        app.Hwnd = null;
        app.State = AppLifecycleState.Failed;
        DropPendingStarts(app.AppId);

        var commands = new List<Command>();
        if (wasForeground)
        {
            commands.Add(new ShowShell(NextCmdId()));
            commands.Add(new SetInputRouting(NextCmdId(), InputMode.Shell, null, PointerPolicy.Blocked, NextEpoch()));
            _focus = new FocusRequest(app.AppId, ++_focusSeq);
        }

        if (CancelStartTimeout(app.AppId) is { } cancel)
        {
            commands.Add(cancel);
        }

        return new OrchestratorResult(commands, Snapshot());
    }

    /// <summary>Korelacje startu aplikacji tracą znaczenie po wejściu w <c>Failed</c>.</summary>
    private void DropPendingStarts(string appId)
    {
        var stale = new List<long>();
        foreach (var (cmdId, pendingAppId) in _pendingStarts)
        {
            if (pendingAppId == appId)
            {
                stale.Add(cmdId);
            }
        }

        foreach (var cmdId in stale)
        {
            _pendingStarts.Remove(cmdId);
        }
    }

    /// <summary>
    /// Planuje timeout <c>Starting</c> na wstrzykniętym zegarze: <c>timerId</c> nadaje wyłącznie Orchestrator,
    /// a korelacja <c>timerId → appId</c> żyje tutaj, więc spóźniony <see cref="TimerElapsed"/> odpada bez
    /// zaglądania w stan schedulera. Komenda timera idzie na koniec batcha startu.
    /// </summary>
    private ScheduleTimer ScheduleStartTimeout(string appId)
    {
        var timerId = ++_timerId;
        _startTimers.Add(timerId, appId);
        return new ScheduleTimer(NextCmdId(), timerId, appId, _time.GetUtcNow() + _startingTimeout);
    }

    /// <summary>
    /// Zdejmuje korelację timeoutu aplikacji i zwraca komendę anulowania; <c>null</c>, gdy termin już nie żyje
    /// (np. po <see cref="AppWindowAttached"/> albo po samym timeoucie).
    /// </summary>
    private CancelTimer? CancelStartTimeout(string appId)
    {
        var timerId = FindStartTimeout(appId);
        if (timerId is not { } id)
        {
            return null;
        }

        _startTimers.Remove(id);
        return new CancelTimer(NextCmdId(), id);
    }

    /// <summary><c>timerId</c> timeoutu <c>Starting</c> dla aplikacji; <c>null</c>, gdy termin już nie żyje.</summary>
    private long? FindStartTimeout(string appId)
    {
        foreach (var (timerId, owner) in _startTimers)
        {
            if (owner == appId)
            {
                return timerId;
            }
        }

        return null;
    }

    /// <summary>Wspólny koniec stopu: restart startuje aplikację od nowa, zamknięcie kończy się w <c>NotRunning</c>.</summary>
    private OrchestratorResult OnStopped(PendingStop stop)
    {
        var app = _appsById[stop.AppId];
        app.Hwnd = null;

        if (!stop.Restart)
        {
            app.State = AppLifecycleState.NotRunning;
            return new OrchestratorResult([], Snapshot());
        }

        var cmdId = NextCmdId();
        app.State = AppLifecycleState.Starting;
        _pendingStarts.Add(cmdId, app.AppId);
        return new OrchestratorResult([new StartApp(cmdId, app.AppId), ScheduleStartTimeout(app.AppId)], Snapshot());
    }

    /// <summary><c>cmdId</c> korelacji stopu dla aplikacji; <c>null</c>, gdy stop nie jest w toku.</summary>
    private long? FindPendingStop(string appId)
    {
        foreach (var (cmdId, pending) in _pendingStops)
        {
            if (pending.AppId == appId)
            {
                return cmdId;
            }
        }

        return null;
    }

    /// <summary>Snapshot bieżącego stanu; powtarza ostatni <see cref="FocusRequest"/>, bo UI stosuje <c>seq</c> raz.</summary>
    private ShellSnapshot Snapshot()
    {
        _rev++;

        var apps = new AppSnapshot[_apps.Count];
        for (var i = 0; i < _apps.Count; i++)
        {
            var app = _apps[i];
            apps[i] = new AppSnapshot(app.AppId, app.Category, app.State);
        }

        return new ShellSnapshot(
            _rev,
            ShellScreen.Placeholder,
            Array.Empty<ConfigError>(),
            apps,
            _focus);
    }

    private long NextCmdId() => ++_cmdId;

    private long NextEpoch() => ++_epoch;

    /// <summary>Typ i polityka wskaźnika z rekordu <c>apps.json</c>, z których żyje routing i mapa routera (AD-11).</summary>
    private static (AppType Type, PointerPolicy Pointer) Describe(AppEntry app) => app switch
    {
        WebApp web => (AppType.Web, web.Pointer),
        MoonlightApp moonlight => (AppType.Moonlight, moonlight.Pointer),
        NativeApp native => (AppType.Native, native.Pointer),
        _ => throw new InvalidOperationException($"Nieznany typ aplikacji {app.GetType().FullName}."),
    };

    private sealed class AppState(string appId, AppCategory category, AppType type, PointerPolicy pointer)
    {
        public string AppId { get; } = appId;

        public AppCategory Category { get; } = category;

        public AppType Type { get; } = type;

        public PointerPolicy Pointer { get; } = pointer;

        public AppLifecycleState State { get; set; } = AppLifecycleState.NotRunning;

        /// <summary>Uchwyt okna z <see cref="AppWindowAttached"/>; <c>null</c> po zamknięciu, restarcie albo wejściu w <c>Failed</c>.</summary>
        public long? Hwnd { get; set; }
    }

    /// <summary>Korelacja <c>cmdId → aplikacja</c> dla <see cref="StopApp"/>; <c>Restart</c> wybiera stan po stopie.</summary>
    private sealed record PendingStop(string AppId, bool Restart);
}
