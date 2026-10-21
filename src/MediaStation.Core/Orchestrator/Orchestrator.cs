using System;
using System.Collections.Generic;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;

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
/// Bez UI, I/O i czasu ambientowego; woła go wyłącznie jedna pętla w App, więc stan nie potrzebuje blokad.
/// </summary>
public sealed class Orchestrator
{
    private readonly List<AppState> _apps;
    private readonly Dictionary<string, AppState> _appsById;
    private readonly Dictionary<long, string> _pendingStarts = [];
    private long _rev;
    private long _focusSeq;
    private long _cmdId;
    private long _epoch;
    private FocusRequest? _focus;

    public Orchestrator(LoadedConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

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
            LaunchApp launch => OnLaunchApp(launch),
            AppWindowAttached attached => OnAppWindowAttached(attached),
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
    /// Wybór kafelka uruchamia tylko aplikację w <c>NotRunning</c>; <c>cmdId</c> zostaje w korelacji startu.
    /// Story 10 dołoży przełączanie z <c>Background</c>, story 7 adopcję.
    /// </summary>
    private OrchestratorResult OnLaunchApp(LaunchApp launch)
    {
        if (!_appsById.TryGetValue(launch.AppId, out var app) || app.State != AppLifecycleState.NotRunning)
        {
            return OrchestratorResult.None;
        }

        var cmdId = NextCmdId();
        app.State = AppLifecycleState.Starting;
        _pendingStarts.Add(cmdId, app.AppId);
        return new OrchestratorResult([new StartApp(cmdId, app.AppId)], Snapshot());
    }

    /// <summary>
    /// Okno po starcie daje <c>Foreground</c>: aktywacja okna i routing wejścia według tabeli AD-8.
    /// Poza <c>Starting</c> fakt jest ignorowany; adopcję (story 7) dokłada osobna gałąź.
    /// </summary>
    private OrchestratorResult OnAppWindowAttached(AppWindowAttached attached)
    {
        if (!_appsById.TryGetValue(attached.AppId, out var app) || app.State != AppLifecycleState.Starting)
        {
            return OrchestratorResult.None;
        }

        app.State = AppLifecycleState.Foreground;
        var activate = new ActivateWindow(NextCmdId(), attached.Hwnd);
        var routing = new SetInputRouting(NextCmdId(), InputMode.App, app.Type, app.Pointer, NextEpoch());
        return new OrchestratorResult([activate, routing], Snapshot());
    }

    /// <summary>
    /// Sukces startu nie zmienia stanu w tej story: <c>Foreground</c> daje wyłącznie
    /// <see cref="AppWindowAttached"/>. Korelacja kończy się razem z wynikiem komendy.
    /// </summary>
    private OrchestratorResult OnCommandCompleted(CommandCompleted completed)
    {
        _pendingStarts.Remove(completed.CmdId);
        return OrchestratorResult.None;
    }

    /// <summary>
    /// Porażka startu nie zmienia stanu w tej story — logowanie i przejście w <c>Failed</c> dokłada story 4.
    /// </summary>
    private OrchestratorResult OnCommandFailed(CommandFailed failed)
    {
        _pendingStarts.Remove(failed.CmdId);
        return OrchestratorResult.None;
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
    }
}
