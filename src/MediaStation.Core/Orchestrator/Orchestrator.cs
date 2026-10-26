using System;
using System.Collections.Generic;
using System.Linq;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Orchestrator;

/// <summary>
/// Wynik jednego kroku Orchestratora: komendy do wykonawców i opcjonalnie nowy snapshot dla shella.
/// <see cref="Snapshot"/> jest <c>null</c>, gdy komunikat nie zmienia widoku. <see cref="Actions"/> to Actions
/// przeznaczone dla Shell (AD-8) — pętla publikuje je po snapshocie; <see cref="IgnoredIntent"/> niesie Intent,
/// który Orchestrator odrzucił, żeby pętla (Core nie ma loga) zostawiła ślad w logu.
/// </summary>
public sealed record OrchestratorResult(IReadOnlyList<Command> Commands, ShellSnapshot? Snapshot)
{
    /// <summary>Brak komend i brak snapshotu — komunikat zignorowany w tej story.</summary>
    public static OrchestratorResult None { get; } = new(Array.Empty<Command>(), null);

    /// <summary>Actions dla Shell, w kolejności dostarczenia; domyślnie puste.</summary>
    public IReadOnlyList<ActionMessage> Actions { get; init; } = Array.Empty<ActionMessage>();

    /// <summary>Odrzucony Intent (nazwa, <c>appId</c>, powód) albo <c>null</c>.</summary>
    public IgnoredIntent? IgnoredIntent { get; init; }
}

/// <summary>Intent odrzucony przez Orchestrator: nazwa Intentu, <c>appId</c> i powód do logu <c>intentIgnored</c>.</summary>
public sealed record IgnoredIntent(string Intent, string AppId, string Reason);

/// <summary>
/// Jedyny właściciel stanu sesji (AD-3): czysty handler komunikatu na komendy i snapshot (AD-14, AD-16).
/// Bez UI, I/O i czasu ambientowego — zegar wchodzi wyłącznie przez wstrzyknięty <see cref="TimeProvider"/>;
/// woła go wyłącznie jedna pętla w App, więc stan nie potrzebuje blokad.
/// </summary>
public sealed partial class Orchestrator
{
    private readonly List<AppState> _apps;
    private readonly Dictionary<string, AppState> _appsById;
    private readonly Dictionary<long, string> _pendingStarts = [];
    private readonly Dictionary<long, PendingStop> _pendingStops = [];
    private readonly Dictionary<long, string> _startTimers = [];
    private readonly Dictionary<long, string> _stopTimers = [];
    private readonly TimeProvider _time;
    private readonly TimeSpan _startingTimeout;
    private readonly TimeSpan _stoppingTimeout;
    private long _rev;
    private long _focusSeq;
    private long _cmdId;
    private long _transitionId;
    private long _timerId;
    private long _epoch;
    private FocusRequest? _focus;
    private InputMode _mode = InputMode.Shell;
    private List<ManagedWindow> _publishedWindows = [];
    private long _managedEpoch;

    public Orchestrator(LoadedConfig config, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(time);

        _time = time;
        _startingTimeout = TimeSpan.FromSeconds(config.Settings.StartingTimeout);
        _stoppingTimeout = TimeSpan.FromSeconds(config.Settings.StoppingTimeout);
        _apps = new List<AppState>(config.Apps.Apps.Count);
        _appsById = new Dictionary<string, AppState>(config.Apps.Apps.Count, StringComparer.Ordinal);
        foreach (var app in config.Apps.Apps)
        {
            var (type, pointer) = Describe(app);
            var state = new AppState(app.Id, app.Category, app.Title, type, pointer);
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

        return WithManagedWindows(message switch
        {
            OrchestratorStarted => OnOrchestratorStarted(),
            SystemResumed => OnSystemResumed(),
            ActionMessage action => OnAction(action),
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
        });
    }

    /// <summary>
    /// Niezmiennik „jedna aplikacja na pierwszym planie”: inna aplikacja w <c>Foreground</c>/<c>Starting</c>
    /// blokuje powrót z tła, zamknięcie i restart. Politykę przełączania (story 6) to nie jest.
    /// </summary>
    private bool AnotherAppIsActive(AppState app) =>
        _apps.Exists(other => !ReferenceEquals(other, app)
            && other.State is AppLifecycleState.Foreground or AppLifecycleState.Starting);

    /// <summary>Snapshot bieżącego stanu; powtarza ostatni <see cref="FocusRequest"/>, bo UI stosuje <c>seq</c> raz.</summary>
    private ShellSnapshot Snapshot()
    {
        _rev++;

        var apps = new AppSnapshot[_apps.Count];
        for (var i = 0; i < _apps.Count; i++)
        {
            var app = _apps[i];
            apps[i] = new AppSnapshot(app.AppId, app.Category, app.State, app.Title);
        }

        return new ShellSnapshot(
            _rev,
            ShellScreen.Placeholder,
            Array.Empty<ConfigError>(),
            apps,
            _focus);
    }

    private long NextCmdId() => ++_cmdId;

    /// <summary>
    /// Kolejny wewnętrzny <c>transitionId</c> decyzji o pierwszym planie. Id nie trafia do kontraktów: rozstrzyga
    /// tylko, która decyzja jest bieżąca (AD-14), a „nowsza” znaczy „większa”.
    /// </summary>
    private long NextTransitionId() => ++_transitionId;

    private long NextEpoch() => ++_epoch;

    /// <summary>
    /// Jedyne miejsce, w którym powstaje <see cref="SetInputRouting"/>: zapamiętuje tryb wejścia, żeby
    /// <see cref="OnAction"/> wiedział, czy Actions należą do Shell (AD-8).
    /// </summary>
    private SetInputRouting Route(InputMode mode, AppType? appType, PointerPolicy pointer)
    {
        _mode = mode;
        return new SetInputRouting(NextCmdId(), mode, appType, pointer, NextEpoch());
    }

    /// <summary>Typ i polityka wskaźnika z rekordu <c>apps.json</c>, z których żyje routing i mapa routera (AD-11).</summary>
    private static (AppType Type, PointerPolicy Pointer) Describe(AppEntry app) => app switch
    {
        WebApp web => (AppType.Web, web.Pointer),
        MoonlightApp moonlight => (AppType.Moonlight, moonlight.Pointer),
        NativeApp native => (AppType.Native, native.Pointer),
        _ => throw new InvalidOperationException($"Nieznany typ aplikacji {app.GetType().FullName}."),
    };

    private sealed class AppState(string appId, AppCategory category, string title, AppType type, PointerPolicy pointer)
    {
        public string AppId { get; } = appId;

        public AppCategory Category { get; } = category;

        public string Title { get; } = title;

        public AppType Type { get; } = type;

        public PointerPolicy Pointer { get; } = pointer;

        public AppLifecycleState State { get; set; } = AppLifecycleState.NotRunning;

        /// <summary>Uchwyt okna z <see cref="AppWindowAttached"/>; <c>null</c> po zamknięciu, restarcie albo wejściu w <c>Failed</c>.</summary>
        public long? Hwnd { get; set; }

        /// <summary>PID korzenia z <see cref="AppWindowAttached.RootPid"/>; ważny tylko, gdy <see cref="Hwnd"/> jest ustawiony.</summary>
        public int RootPid { get; set; }

        /// <summary>
        /// <c>transitionId</c> decyzji, która miała wprowadzić aplikację na pierwszy plan (start, powrót z tła,
        /// restart); <c>0</c> tylko dopóki żadna taka decyzja nie zapadła, bo pole nie jest zerowane przy wyjściu
        /// ze stanu. <see cref="OnAppWindowAttached"/> porównuje je z bieżącym licznikiem: każda różnica znaczy,
        /// że w międzyczasie zapadła nowsza decyzja (Home, wybudzenie) i okno dostaje kompensację, a nie aktywację.
        /// </summary>
        public long ForegroundTransition { get; set; }
    }

    /// <summary>
    /// Korelacja <c>cmdId → aplikacja</c> dla <see cref="StopApp"/>; <c>Restart</c> wybiera stan po stopie,
    /// a <see cref="TransitionId"/> niesie decyzję o pierwszym planie z chwili <see cref="OnStop"/> (<c>0</c> dla zamknięcia).
    /// </summary>
    private sealed record PendingStop(string AppId, bool Restart, long TransitionId);
}