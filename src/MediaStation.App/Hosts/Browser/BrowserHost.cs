using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Hosts.Browser;

/// <summary>Uruchomiony proces przeglądarki widziany przez <see cref="BrowserHost"/> (seam dla testów bez procesów).</summary>
internal interface IBrowserProcess
{
    int Id { get; }

    bool HasExited { get; }

    /// <summary>Kod wyjścia albo <c>null</c>, gdy proces żyje lub kodu nie da się odczytać.</summary>
    int? ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Łagodne zamknięcie okna głównego; nie rzuca.</summary>
    void CloseMainWindow();

    /// <summary>Zabija całe drzewo procesów; nie rzuca, gdy proces już nie żyje.</summary>
    void KillTree();
}

/// <summary>Start procesu przeglądarki z plikiem wykonywalnym i argumentami (seam dla testów).</summary>
internal interface IBrowserProcessLauncher
{
    /// <summary>Zwraca proces albo <c>null</c>, gdy system go nie uruchomił.</summary>
    IBrowserProcess? Start(string executable, IReadOnlyList<string> arguments);
}

/// <summary>
/// BrowserHost (AD-1, AD-4, AD-10, AD-13): uruchamia Edge/Chrome z wpisu <c>apps.json</c> z profilem
/// <c>profiles\&lt;appId&gt;</c>, flagami AD-1 i <c>--kiosk</c>; raportuje <see cref="AppWindowAttached"/>, gdy tylko okno
/// istnieje (attach nie czeka na CDP), potem łączy surowy klient CDP na <c>127.0.0.1:&lt;cdpPort&gt;</c>, instaluje
/// binding i PageRuntime, nawiguje na start URL z manifestu drivera i loguje <c>hello</c> strony.
/// Story 6.3: binding wraca po każdej nawigacji głównej ramki, a zerwane CDP żywej sesji host łączy ponownie z tym
/// samym celem strony (backoff, termin <c>cdpReconnectTimeout</c>) i instaluje binding, <c>Page.enable</c> i skrypt na
/// nowej sesji; bez sukcesu w terminie wysyła <see cref="CdpLost"/>. W trakcie zerwania sesja nie ma kanału CDP i nic
/// nie jest buforowane ani powtarzane po powrocie.
/// Story 6.4 (AD-13): przed każdym startem sprawdza profil (<c>lockfile</c>) i port CDP i przy zajętym odpowiada
/// <see cref="ErrorCode.ProfileInUse"/> albo <see cref="ErrorCode.PortInUse"/> bez startu; loguje <c>FileVersion</c>
/// binarki i <c>Last Version</c> profilu (<c>browserVersion</c>, <c>browserVersionChanged</c> przed <c>appLaunch</c>),
/// a po podpięciu potwierdza wersję przez <c>Browser.getVersion</c>. Stop czeka do 5 s na wyjście korzenia po zabiciu
/// drzewa, więc kontrola ponownego startu nie widzi starej przeglądarki.
/// Story 6.5 (AD-10, AD-13): sonda <see cref="ProbeExisting"/> szuka w liniach poleceń procesów żywego korzenia
/// z profilem aplikacji i oknem, adoptuje go bez drugiego startu (<see cref="AppWindowAttached"/>, watcher wyjścia),
/// a potem łączy CDP na porcie aplikacji i instaluje tylko binding, <c>Page.enable</c> i skrypt. Adoptowana sesja
/// działa jak uruchomiona (reconnect, Stop, odświeżenie); bez CDP daje <c>cdpUnavailable</c> i <see cref="CdpLost"/>,
/// a zostaje zarejestrowana, żeby Close/Restart zabiły osierocone drzewo. Start w trakcie sondy czeka na nią i
/// używa gotowej adoptowanej sesji zamiast startu. <see cref="RenewSession"/> po wybudzeniu zdejmuje bieżący kanał
/// CDP i uruchamia pętlę reconnectu 6.3 do tego samego celu.
/// Story 6.6 (AD-6, AD-7): wstrzykuje bundle IIFE PageRuntime z <c>page\runtime.js</c> owinięty w funkcję z configiem
/// (<see cref="BrowserLaunch.BuildInitScript"/>). Kopertę strony z innym major <c>v</c> loguje jako
/// <c>pageMessageRejected</c> z <c>reason: version</c> i raportuje <see cref="PageVersionRejected"/> najwyżej raz na
/// sesję (tylko bieżącą i niezamykaną), a Orchestrator oznacza kafelek stanem <c>Failed</c> jak po <see cref="CdpLost"/>.
/// Sam nigdy nie pokazuje, nie wynosi i nie minimalizuje okien (AD-14, AD-15). Metody portu tylko zlecają pracę
/// w tle i nie rzucają przez granicę portu (AD-4).
/// </summary>
internal sealed class BrowserHost : IAppHostPort
{
    /// <summary>Źródło wpisów logu hosta.</summary>
    public const string Src = "browser";

    /// <summary>Plik PageRuntime w katalogu <c>page</c> obok exe.</summary>
    public const string RuntimeFileName = "runtime.js";

    /// <summary>Plik w katalogu głównym <c>user-data-dir</c> z wersją przeglądarki z jej poprzedniego startu (AD-13).</summary>
    public const string LastVersionFileName = "Last Version";

    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KillExitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Wyzwalacz pętli reconnectu w logach: zamknięty socket bieżącej sesji CDP.</summary>
    private const string ClosedTrigger = "cdpClosed";

    /// <summary>Wyzwalacz pętli reconnectu w logach: <see cref="RenewSession"/> po wybudzeniu.</summary>
    private const string RenewTrigger = "renewSession";

    /// <summary>Przerwy przed kolejnymi próbami reconnectu; po wyczerpaniu listy obowiązuje ostatnia (2 s).</summary>
    private static readonly TimeSpan[] ReconnectBackoff =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    private readonly ILog _log;
    private readonly Action<IOrchestratorMessage> _post;
    private readonly IReadOnlyDictionary<string, WebApp> _apps;
    private readonly string _profilesDir;
    private readonly string _pageDir;
    private readonly TimeSpan _cdpConnectTimeout;
    private readonly TimeSpan _cdpReconnectTimeout;
    private readonly Func<BrowserKind, string?> _resolveBrowser;
    private readonly IBrowserProcessLauncher _launcher;
    private readonly Func<int, WindowInfo?> _findWindow;
    private readonly Func<int, CancellationToken, Task<ICdpTransport?>> _connect;
    private readonly Func<Uri, CancellationToken, Task<ICdpTransport?>> _reconnect;
    private readonly Func<string, string?> _readFile;
    private readonly Func<string, string?> _readFileVersion;
    private readonly Func<string, bool> _isProfileInUse;
    private readonly Func<int, bool> _isPortInUse;
    private readonly Func<IReadOnlyList<KeyValuePair<int, string>>> _commandLines;
    private readonly Func<int, IBrowserProcess?> _openProcess;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    /// <summary>Ostatnia sonda każdej aplikacji (nigdy nie kończy się wyjątkiem); Start czeka na nią przed swoją ścieżką.</summary>
    private readonly Dictionary<string, Task> _probes = new(StringComparer.Ordinal);

    internal BrowserHost(
        ILog log,
        Action<IOrchestratorMessage> post,
        IEnumerable<WebApp> apps,
        string profilesDir,
        string pageDir,
        TimeSpan cdpConnectTimeout,
        TimeSpan cdpReconnectTimeout,
        Func<BrowserKind, string?> resolveBrowser,
        IBrowserProcessLauncher launcher,
        Func<int, WindowInfo?> findWindow,
        Func<int, CancellationToken, Task<ICdpTransport?>> connect,
        Func<Uri, CancellationToken, Task<ICdpTransport?>> reconnect,
        Func<string, string?> readFile,
        Func<string, string?> readFileVersion,
        Func<string, bool> isProfileInUse,
        Func<int, bool> isPortInUse,
        Func<IReadOnlyList<KeyValuePair<int, string>>> commandLines,
        Func<int, IBrowserProcess?> openProcess,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageDir);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cdpConnectTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cdpReconnectTimeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(resolveBrowser);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(findWindow);
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(reconnect);
        ArgumentNullException.ThrowIfNull(readFile);
        ArgumentNullException.ThrowIfNull(readFileVersion);
        ArgumentNullException.ThrowIfNull(isProfileInUse);
        ArgumentNullException.ThrowIfNull(isPortInUse);
        ArgumentNullException.ThrowIfNull(commandLines);
        ArgumentNullException.ThrowIfNull(openProcess);
        ArgumentNullException.ThrowIfNull(time);

        var byId = new Dictionary<string, WebApp>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            byId.Add(app.Id, app);
        }

        _log = log;
        _post = post;
        _apps = byId;
        _profilesDir = profilesDir;
        _pageDir = pageDir;
        _cdpConnectTimeout = cdpConnectTimeout;
        _cdpReconnectTimeout = cdpReconnectTimeout;
        _resolveBrowser = resolveBrowser;
        _launcher = launcher;
        _findWindow = findWindow;
        _connect = connect;
        _reconnect = reconnect;
        _readFile = readFile;
        _readFileVersion = readFileVersion;
        _isProfileInUse = isProfileInUse;
        _isPortInUse = isPortInUse;
        _commandLines = commandLines;
        _openProcess = openProcess;
        _time = time;
    }

    /// <summary>Zleca start w tle; wynik wraca jako Events z tym samym <c>cmdId</c>.</summary>
    public void Start(StartApp command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            _ = Task.Run(() => StartCoreAsync(command));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(command, ex);
        }
    }

    /// <summary>
    /// Zamyka sesję w tle (CDP, okno do 3 s, drzewo procesów) i potwierdza <see cref="ProcessExited"/> oraz
    /// <see cref="CommandCompleted"/>; brak sesji to też sukces (idempotentny stop).
    /// </summary>
    public void Stop(StopApp command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            _ = Task.Run(() => StopCoreAsync(command));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(command, ex);
        }
    }

    /// <summary>
    /// Sonda w tle (story 6.5, AD-10): żywy korzeń z profilem aplikacji i oknem jest adoptowany bez startu, potem host
    /// łączy CDP. Brak korzenia albo okna to cisza (tylko wpis <c>probeExisting</c>). Zadanie sondy nigdy nie kończy się
    /// wyjątkiem, a Start tej samej aplikacji czeka na nie; sonda nie jest ponawiana.
    /// </summary>
    public void Probe(ProbeExisting command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            // Rejestracja przed powrotem z metody portu: StartApp wysłany po sondzie zawsze ją zobaczy.
            lock (_gate)
            {
                _probes[command.AppId] = Task.Run(() => ProbeCoreAsync(command));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "browserCommandFailed", ex, new Dictionary<string, object?> { ["cmdId"] = command.CmdId });
        }
    }

    /// <summary>
    /// Odświeżenie sesji po wybudzeniu (story 6.5, AD-10) w tle: gotowa sesja z bieżącym kanałem CDP traci go, a pętla
    /// reconnectu 6.3 łączy się z tym samym celem; porażka kończy się <see cref="CdpLost"/>. W pozostałych przypadkach
    /// tylko wpis <c>renewSession</c> z powodem.
    /// </summary>
    public void Renew(RenewSession command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            _ = Task.Run(() => RenewCore(command));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "browserCommandFailed", ex, new Dictionary<string, object?> { ["cmdId"] = command.CmdId });
        }
    }

    private async Task StartCoreAsync(StartApp command)
    {
        try
        {
            if (!_apps.TryGetValue(command.AppId, out var app))
            {
                FailStart(command, "unknownApp");
                return;
            }

            // Start w trakcie sondy czeka na nią: bez tego nowa sesja trafiłaby w ProfileInUse, a sierota nie zginęłaby.
            await AwaitProbeAsync(app.Id).ConfigureAwait(false);
            if (TryReuseAdopted(command, app))
            {
                return;
            }

            // Zasoby strony czytane z dysku przy każdym starcie (hotfix drivera, AD-13); bez nich nic nie startuje.
            var assets = LoadPageAssets(app);
            if (assets.InitScript is not { } initScript || assets.Manifest is not { } manifest)
            {
                FailStart(command, assets.Reason ?? "pageAssetInvalid", assets.File, assets.Detail);
                return;
            }

            var exe = _resolveBrowser(app.Browser);
            if (exe is null)
            {
                FailStart(command, "browserNotFound");
                return;
            }

            var profileDir = Path.Combine(_profilesDir, app.Id);
            var args = BrowserLaunch.BuildArguments(profileDir, app.CdpPort);

            // Sesja jest zarejestrowana przed startem procesu, więc Stop z luki nie ginie; ponowny start najpierw
            // zamyka poprzednią sesję, zamiast ją osierocić.
            var session = new Session(app.Id);
            Session? previous;
            lock (_gate)
            {
                _sessions.Remove(app.Id, out previous);
                _sessions[app.Id] = session;
            }

            if (previous is not null)
            {
                await StopSessionAsync(previous).ConfigureAwait(false);
            }

            // Kontrole po zamknięciu poprzedniej sesji (Stop czeka na wyjście jej korzenia), więc ponowny start nie
            // widzi własnej starej przeglądarki. Zajęty profil wygrywa z zajętym portem; nic nie jest uruchamiane.
            if (_isProfileInUse(profileDir))
            {
                Unregister(session);
                FailStart(command, "profileInUse", errorCode: ErrorCode.ProfileInUse, extra: ("profileDir", profileDir));
                return;
            }

            if (_isPortInUse(app.CdpPort))
            {
                Unregister(session);
                FailStart(command, "portInUse", errorCode: ErrorCode.PortInUse, extra: ("cdpPort", app.CdpPort));
                return;
            }

            session.BrowserVersion = LogBrowserVersion(app, exe, profileDir);

            // Termin CDP liczy się od startu procesu (AD-13), także przez czas szukania okna.
            using var cdpDeadline = new CancellationTokenSource(_cdpConnectTimeout, _time);
            using var cdpToken = CancellationTokenSource.CreateLinkedTokenSource(cdpDeadline.Token, session.StopToken);

            var process = _launcher.Start(exe, args);
            if (process is null)
            {
                Unregister(session);
                FailStart(command, "processStartFailed");
                return;
            }

            session.Process = process;
            _log.Info(Src, "appLaunch", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["commandLine"] = $"\"{exe}\" {string.Join(' ', args)}",
                ["args"] = args,
                ["cdpPort"] = app.CdpPort,
                ["pid"] = process.Id,
            });

            var window = await WaitForWindowAsync(session, process).ConfigureAwait(false);
            if (window is null)
            {
                Unregister(session);
                var stopped = session.StopRequested;
                var exited = process.HasExited;

                // Zawsze: Stop mógł przyjść przed przypisaniem Process i nie zabić tego drzewa (idempotentne).
                session.MarkStopping();
                process.KillTree();

                FailStart(
                    command,
                    stopped ? "stoppedDuringStart" : exited ? "launcherExited" : "windowTimeout",
                    exitCode: exited ? process.ExitCode : null);
                return;
            }

            _log.Info(Src, "appWindowAttached", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["hwnd"] = window.Hwnd,
                ["class"] = window.ClassName,
                ["rootPid"] = process.Id,
            });
            _post(new AppWindowAttached(app.Id, window.Hwnd, process.Id));
            WatchExit(session, process);

            await ConnectCdpAsync(command, app, manifest, initScript, session, cdpToken.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(command, ex);
        }
    }

    /// <summary>Zasoby strony aplikacji: skrypt PageRuntime z configiem i manifest drivera albo powód porażki z plikiem.</summary>
    private readonly record struct PageAssets(string? InitScript, DriverManifest? Manifest, string? Reason, string? File, string? Detail);

    /// <summary>
    /// <c>runtime.js</c> i manifest drivera czytane z dysku przy każdym użyciu (hotfix drivera, AD-13), wspólne dla
    /// startu i adopcji: brak pliku to <c>pageAssetMissing</c>, pusty runtime albo zły manifest <c>pageAssetInvalid</c>.
    /// </summary>
    private PageAssets LoadPageAssets(WebApp app)
    {
        var runtimePath = Path.Combine(_pageDir, RuntimeFileName);
        var runtime = _readFile(runtimePath);
        if (string.IsNullOrWhiteSpace(runtime))
        {
            return new PageAssets(null, null, runtime is null ? "pageAssetMissing" : "pageAssetInvalid", runtimePath, null);
        }

        var manifestPath = Path.Combine(_pageDir, "drivers", app.Service, "manifest.json");
        var manifestText = _readFile(manifestPath);
        if (manifestText is null)
        {
            return new PageAssets(null, null, "pageAssetMissing", manifestPath, null);
        }

        var parsed = DriverManifest.Parse(manifestText, app.Service);
        if (parsed.Manifest is not { } manifest)
        {
            return new PageAssets(null, null, "pageAssetInvalid", manifestPath, parsed.Reason);
        }

        var initScript = BrowserLaunch.BuildInitScript(runtime, PageProtocol.BindingName, app.Service, manifest.DriverVersion);
        return new PageAssets(initScript, manifest, null, null, null);
    }

    /// <summary>Czeka na ostatnią sondę aplikacji; zadanie sondy nigdy nie kończy się wyjątkiem.</summary>
    private async Task AwaitProbeAsync(string appId)
    {
        Task? probe;
        lock (_gate)
        {
            _probes.TryGetValue(appId, out probe);
        }

        if (probe is not null)
        {
            await probe.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Start po sondzie, która adoptowała gotową sesję (bieżący kanał CDP, żywy korzeń): to samo okno i korzeń jeszcze
    /// raz plus <see cref="CommandCompleted"/>, bez kontroli, stopu i startu. Orchestrator stosuje pierwszy z dwóch
    /// <see cref="AppWindowAttached"/> i ignoruje drugi, więc kolejność nie zmienia jego stanu.
    /// </summary>
    private bool TryReuseAdopted(StartApp command, WebApp app)
    {
        Session? session;
        lock (_gate)
        {
            _sessions.TryGetValue(app.Id, out session);
        }

        if (session is not { Adopted: true, Ready: true, Stopping: false }
            || !session.HasChannel
            || session.Process is not { HasExited: false } process)
        {
            return false;
        }

        _log.Info(Src, "startReusedAdopted", new Dictionary<string, object?>
        {
            ["appId"] = app.Id,
            ["rootPid"] = process.Id,
        });
        _post(new AppWindowAttached(app.Id, session.Hwnd, process.Id));
        _post(new CommandCompleted(command.CmdId));
        return true;
    }

    /// <summary>
    /// Sonda: pierwszy korzeń z profilem aplikacji (bez <c> --type=</c>) z oknem przeglądarki zostaje adoptowany, o ile
    /// aplikacja nie ma sesji. Host niczego nie uruchamia ani nie zabija; okno i dźwięk to adopcja Orchestratora
    /// (wyciszenie, minimalizacja, shell; AD-5). Każda sonda kończy się jednym wpisem <c>probeExisting</c>.
    /// </summary>
    private async Task ProbeCoreAsync(ProbeExisting command)
    {
        var outcome = "notRunning";
        int? rootPid = null;
        try
        {
            if (!_apps.TryGetValue(command.AppId, out var app))
            {
                outcome = "unknownApp";
                return;
            }

            if (HasSession(app.Id))
            {
                outcome = "sessionExists";
                return;
            }

            var profileDir = Path.Combine(_profilesDir, app.Id);
            IBrowserProcess? process = null;
            WindowInfo? window = null;
            string? rootLine = null;
            foreach (var (pid, line) in _commandLines())
            {
                if (!BrowserLaunch.CommandLineMatchesProfile(line, profileDir) || !BrowserLaunch.IsBrowserRoot(line))
                {
                    continue;
                }

                if (_findWindow(pid) is not { } found)
                {
                    outcome = "noWindow";
                    continue;
                }

                // Korzeń zniknął między odczytem linii poleceń a otwarciem: nie ma czego adoptować.
                if (_openProcess(pid) is { } opened)
                {
                    process = opened;
                    window = found;
                    rootLine = line;
                    break;
                }
            }

            if (process is null || window is null || rootLine is null)
            {
                return;
            }

            var session = new Session(app.Id, adopted: true) { Process = process, Hwnd = window.Hwnd };
            lock (_gate)
            {
                if (_sessions.ContainsKey(app.Id))
                {
                    outcome = "sessionExists";
                    return;
                }

                _sessions[app.Id] = session;
            }

            outcome = "adopted";
            rootPid = process.Id;
            _log.Info(Src, "appAdopted", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["rootPid"] = process.Id,
                ["hwnd"] = window.Hwnd,
                ["class"] = window.ClassName,
            });
            _post(new AppWindowAttached(app.Id, window.Hwnd, process.Id));
            WatchExit(session, process);

            await AdoptCdpAsync(app, session, rootLine).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nieudana sonda (np. WMI) to nie „nic do adopcji”: wpis końcowy mówi failed.
            outcome = "failed";
            _log.Error(Src, "browserProbeFailed", ex, new Dictionary<string, object?> { ["appId"] = command.AppId });
        }
        finally
        {
            var data = new Dictionary<string, object?>
            {
                ["appId"] = command.AppId,
                ["outcome"] = outcome,
            };
            if (rootPid is not null)
            {
                data["rootPid"] = rootPid;
            }

            _log.Info(Src, "probeExisting", data);
        }
    }

    private bool HasSession(string appId)
    {
        lock (_gate)
        {
            return _sessions.ContainsKey(appId);
        }
    }

    /// <summary>
    /// CDP po adopcji: tylko gdy korzeń ma port aplikacji, w terminie <c>cdpConnectTimeout</c> połączonym z tokenem
    /// stopu sesji, z samym <see cref="InstallAsync"/> (bez <c>Page.navigate</c> i odczytu wersji, AD-13). Sukces czyni
    /// sesję gotową i obserwowaną jak po starcie; porażka przy żywym korzeniu daje <c>cdpUnavailable</c> i
    /// <see cref="CdpLost"/>, a sesja zostaje dla Close/Restart.
    /// </summary>
    private async Task AdoptCdpAsync(WebApp app, Session session, string rootLine)
    {
        var assets = LoadPageAssets(app);
        if (assets.InitScript is not { } initScript)
        {
            AdoptedCdpFailed(app, session, assets.Reason ?? "pageAssetInvalid", assets.File, assets.Detail);
            return;
        }

        if (!BrowserLaunch.HasDebuggingPort(rootLine, app.CdpPort))
        {
            AdoptedCdpFailed(app, session, "portMismatch", null, null);
            return;
        }

        using var deadline = new CancellationTokenSource(_cdpConnectTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, session.StopToken);
        var token = linked.Token;

        ICdpTransport? transport;
        try
        {
            transport = await _connect(app.CdpPort, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            transport = null;
        }

        if (transport is null)
        {
            AdoptedCdpFailed(app, session, "notConnected", null, null);
            return;
        }

        // Pierwszy cel strony z /json/list konektora (jak przy starcie) zostaje celem reconnectu i odświeżenia.
        session.Endpoint = CdpTargets.LoopbackWebSocket(transport.Endpoint.OriginalString, app.CdpPort);

        var cdp = CreateCdp(session, transport);
        if (!session.TryAttach(cdp))
        {
            cdp.Dispose();
            AdoptedCdpFailed(app, session, "notConnected", null, null);
            return;
        }

        cdp.Start();
        try
        {
            // Strona już działa: bez Page.navigate i bez Browser.getVersion („nie przy adopcji”, AD-13).
            await InstallAsync(cdp, initScript, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CdpException or OperationCanceledException)
        {
            session.Detach(cdp);
            cdp.Dispose();
            AdoptedCdpFailed(app, session, "setupFailed", null, ex.Message);
            return;
        }

        if (!session.IsCurrent(cdp))
        {
            // Stop albo wyjście procesu w trakcie setupu: kanał zabrał Stop, nic więcej nie idzie.
            session.Detach(cdp);
            cdp.Dispose();
            AdoptedCdpFailed(app, session, "setupFailed", null, "stopping");
            return;
        }

        _log.Info(Src, "cdpConnected", new Dictionary<string, object?>
        {
            ["appId"] = app.Id,
            ["cdpPort"] = app.CdpPort,
            ["adopted"] = true,
        });
        session.MarkReady(initScript);
        WatchCdp(session, app, initScript, cdp);
    }

    /// <summary>
    /// Adopcja bez CDP: przy żywym korzeniu i bez Stopu <c>cdpUnavailable</c> i jeden <see cref="CdpLost"/> (AD-10);
    /// po Stopie albo wyjściu procesu tylko wpis informacyjny, bo te ścieżki raportują same.
    /// </summary>
    private void AdoptedCdpFailed(WebApp app, Session session, string reason, string? file, string? detail)
    {
        var exited = session.Process is not { HasExited: false };
        if (session.Stopping || session.StopToken.IsCancellationRequested || exited)
        {
            _log.Info(Src, "cdpAbandoned", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["reason"] = exited ? "processExited" : "stopped",
                ["adopted"] = true,
            });
            return;
        }

        var data = new Dictionary<string, object?>
        {
            ["appId"] = app.Id,
            ["cdpPort"] = app.CdpPort,
            ["reason"] = reason,
            ["adopted"] = true,
        };
        if (file is not null)
        {
            data["file"] = file;
        }

        if (detail is not null)
        {
            data["detail"] = detail;
        }

        _log.Warn(Src, "cdpUnavailable", data);
        _post(new CdpLost(app.Id));
    }

    /// <summary>
    /// Odświeżenie: zarejestrowana, niezamykana, gotowa sesja bez trwającego reconnectu oddaje atomowo bieżący kanał,
    /// który jest zamykany, a pętla reconnectu (backoff i termin 6.3) łączy się z zapamiętanym celem. Zamknięty stary
    /// kanał nie uruchamia drugiej pętli, bo <see cref="Session.Detach"/> zwraca dla niego <c>false</c>.
    /// </summary>
    private void RenewCore(RenewSession command)
    {
        try
        {
            Session? session;
            lock (_gate)
            {
                _sessions.TryGetValue(command.AppId, out session);
            }

            CdpSession? old = null;
            WebApp? app = null;
            string outcome;
            if (session is null || !_apps.TryGetValue(command.AppId, out app))
            {
                outcome = "noSession";
            }
            else if (session.Stopping)
            {
                outcome = "stopping";
            }
            else if (!session.Ready)
            {
                outcome = "notReady";
            }
            else if (session.Reconnecting)
            {
                outcome = "reconnecting";
            }
            else if ((old = session.DetachCurrent()) is null)
            {
                outcome = "noCdp";
            }
            else
            {
                session.Reconnecting = true;
                outcome = "renewing";
            }

            if (old is null || session is null || app is null || session.InitScript is not { } initScript)
            {
                LogRenew(command.AppId, outcome);
                return;
            }

            // Chwila odświeżenia i termin przed wpisem: backoff i termin liczą się od zdjęcia kanału.
            old.Dispose();
            var renewedAt = _time.GetTimestamp();
            var deadline = new CancellationTokenSource(_cdpReconnectTimeout, _time);
            LogRenew(command.AppId, outcome);
            _ = Task.Run(() => ReconnectAsync(session, app, initScript, renewedAt, deadline, RenewTrigger));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "renewSessionFailed", ex, new Dictionary<string, object?> { ["appId"] = command.AppId });
        }
    }

    private void LogRenew(string appId, string outcome) =>
        _log.Info(Src, "renewSession", new Dictionary<string, object?>
        {
            ["appId"] = appId,
            ["outcome"] = outcome,
        });

    private async Task ConnectCdpAsync(
        StartApp command,
        WebApp app,
        DriverManifest manifest,
        string initScript,
        Session session,
        CancellationToken token)
    {
        ICdpTransport? transport = null;
        try
        {
            transport = await _connect(app.CdpPort, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            transport = null;
        }

        if (transport is null)
        {
            CdpFailed(command, app, session, "notConnected", null);
            return;
        }

        // Cel strony zapamiętany dla reconnectu (story 6.3): tylko adres loopback portu aplikacji.
        session.Endpoint = CdpTargets.LoopbackWebSocket(transport.Endpoint.OriginalString, app.CdpPort);

        var cdp = CreateCdp(session, transport);
        if (!session.TryAttach(cdp))
        {
            cdp.Dispose();
            CdpFailed(command, app, session, "notConnected", null);
            return;
        }

        cdp.Start();
        try
        {
            await InstallAsync(cdp, initScript, token).ConfigureAwait(false);

            // Tylko przy starcie (reconnect używa samego InstallAsync); upływ terminu CDP idzie ścieżką setupFailed.
            var confirmed = await ConfirmVersionAsync(cdp, app, session, token).ConfigureAwait(false);

            // Odpowiedź na Page.navigate przychodzi dopiero po commicie nawigacji (sieć), więc nie liczy się do terminu
            // CDP: wolna sieć nie zamienia działającego CDP w CdpUnavailable. Czekanie przerywa tylko Stop albo wyjście.
            var navigated = await cdp.SendAsync("Page.navigate", new Dictionary<string, object?> { ["url"] = manifest.StartUrl }, session.StopToken).ConfigureAwait(false);

            var url = BrowserLaunch.SafeDiagnosticUrl(manifest.StartUrl);
            if (navigated.ValueKind == JsonValueKind.Object
                && navigated.TryGetProperty("errorText", out var errorText)
                && errorText.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(errorText.GetString()))
            {
                _log.Warn(Src, "pageNavigateFailed", new Dictionary<string, object?>
                {
                    ["appId"] = app.Id,
                    ["url"] = url,
                    ["errorText"] = errorText.GetString(),
                });
            }

            _log.Info(Src, "cdpConnected", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["cdpPort"] = app.CdpPort,
                ["url"] = url,
                ["driverVersion"] = manifest.DriverVersion,
                ["browserVersion"] = BrowserVersion.Format(confirmed),
            });
        }
        catch (Exception ex) when (ex is CdpException or OperationCanceledException)
        {
            session.Detach(cdp);
            cdp.Dispose();
            CdpFailed(command, app, session, "setupFailed", ex.Message);
            return;
        }

        // Reconnect dotyczy tylko sesji po zakończonym starcie: zamknięcie w trakcie setupu to ścieżka CdpUnavailable.
        // Od teraz sesja jest gotowa, więc RenewSession może ją odświeżyć.
        session.MarkReady(initScript);
        _post(new CommandCompleted(command.CmdId));
        WatchCdp(session, app, initScript, cdp);
    }

    /// <summary>
    /// Binding, <c>Page.enable</c> i skrypt PageRuntime na świeżej sesji CDP (start i reconnect). Bez
    /// <c>Runtime.enable</c> (AD-1): binding działa bez niego, ale nie przechodzi na nowy dokument, więc ponawiamy go
    /// po każdej nawigacji głównej ramki (<see cref="OnCdpEvent"/>). Skrypt rejestrujemy raz na sesję CDP: żyje tyle co
    /// ona, a ponowna rejestracja przy nawigacji uruchomiłaby PageRuntime dwa razy w jednym dokumencie.
    /// </summary>
    private static async Task InstallAsync(CdpSession cdp, string initScript, CancellationToken token)
    {
        await cdp.SendAsync("Runtime.addBinding", new Dictionary<string, object?> { ["name"] = PageProtocol.BindingName }, token).ConfigureAwait(false);
        await cdp.SendAsync("Page.enable", null, token).ConfigureAwait(false);
        await cdp.SendAsync("Page.addScriptToEvaluateOnNewDocument", new Dictionary<string, object?> { ["source"] = initScript }, token).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>Browser.getVersion</c> potwierdza wersję odczytaną przed startem (AD-13) i zwraca wersję z <c>product</c> albo
    /// <c>null</c>. Różnica daje <c>browserVersionMismatch</c>, a odpowiedź z błędem albo nieczytelny <c>product</c> daje
    /// <c>browserVersionUnconfirmed</c>; start i tak się kończy. Anulowanie (termin CDP, Stop) przechodzi dalej.
    /// </summary>
    private async Task<Version?> ConfirmVersionAsync(CdpSession cdp, WebApp app, Session session, CancellationToken token)
    {
        JsonElement reply;
        try
        {
            reply = await cdp.SendAsync("Browser.getVersion", null, token).ConfigureAwait(false);
        }
        catch (CdpException ex)
        {
            VersionUnconfirmed(app, ex.Message);
            return null;
        }

        var product = reply.ValueKind == JsonValueKind.Object
            && reply.TryGetProperty("product", out var p)
            && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
        var actual = BrowserVersion.FromProduct(product);
        if (actual is null)
        {
            VersionUnconfirmed(app, "unparsableProduct");
            return null;
        }

        if (BrowserVersion.Changed(session.BrowserVersion, actual))
        {
            _log.Warn(Src, "browserVersionMismatch", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["expected"] = BrowserVersion.Format(session.BrowserVersion),
                ["actual"] = BrowserVersion.Format(actual),
            });
        }

        return actual;
    }

    private void VersionUnconfirmed(WebApp app, string reason) =>
        _log.Warn(Src, "browserVersionUnconfirmed", new Dictionary<string, object?>
        {
            ["appId"] = app.Id,
            ["reason"] = reason,
        });

    /// <summary>
    /// Wersja przed startem (AD-13): bieżąca to <c>FileVersion</c> binarki użytej do startu, poprzednia to
    /// <c>Last Version</c> profilu. Oba wpisy idą przed <c>appLaunch</c>, zanim przeglądarka nadpisze plik, więc awaria
    /// po starcie nie gubi zmiany. Nieznana wersja nie daje wpisu o zmianie. Zwraca bieżącą wersję albo <c>null</c>.
    /// </summary>
    private Version? LogBrowserVersion(WebApp app, string exe, string profileDir)
    {
        var current = BrowserVersion.Parse(_readFileVersion(exe));
        var previous = BrowserVersion.Parse(_readFile(Path.Combine(profileDir, LastVersionFileName)));

        _log.Info(Src, "browserVersion", new Dictionary<string, object?>
        {
            ["appId"] = app.Id,
            ["version"] = BrowserVersion.Format(current),
            ["previous"] = BrowserVersion.Format(previous),
        });

        if (BrowserVersion.Changed(previous, current))
        {
            _log.Info(Src, "browserVersionChanged", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["from"] = BrowserVersion.Format(previous),
                ["to"] = BrowserVersion.Format(current),
            });
        }

        return current;
    }

    /// <summary>Sesja CDP, której zdarzenia mają skutek tylko wtedy, gdy jest bieżącym kanałem sesji aplikacji.</summary>
    private CdpSession CreateCdp(Session session, ICdpTransport transport)
    {
        CdpSession? cdp = null;
        cdp = new CdpSession(transport, (method, parameters) => OnCdpEvent(session, cdp!, method, parameters));
        return cdp;
    }

    /// <summary>Zamknięcie bieżącej sesji CDP żywej aplikacji uruchamia reconnect (<see cref="OnCdpClosed"/>).</summary>
    private void WatchCdp(Session session, WebApp app, string initScript, CdpSession cdp) =>
        _ = cdp.Closed.ContinueWith(
            _ => OnCdpClosed(session, app, initScript, cdp),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    /// Socket CDP zamknął się bez Stopu i wyjścia procesu: sesja traci kanał (<c>Cdp == null</c>), a termin
    /// <c>cdpReconnectTimeout</c> liczy się od tej chwili na zegarze hosta.
    /// </summary>
    private void OnCdpClosed(Session session, WebApp app, string initScript, CdpSession cdp)
    {
        try
        {
            if (!session.Detach(cdp))
            {
                return;
            }

            // Zwolnienie zaraz po zdjęciu: Stop, który zdążył pomiędzy, dostał już null i tej sesji nie zamknie.
            cdp.Dispose();
            if (session.Stopping)
            {
                return;
            }

            var closedAt = _time.GetTimestamp();
            var deadline = new CancellationTokenSource(_cdpReconnectTimeout, _time);
            session.Reconnecting = true;
            _log.Info(Src, "cdpClosed", new Dictionary<string, object?>
            {
                ["appId"] = session.AppId,
                ["timeoutSeconds"] = _cdpReconnectTimeout.TotalSeconds,
            });
            _ = Task.Run(() => ReconnectAsync(session, app, initScript, closedAt, deadline, ClosedTrigger));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "cdpReconnectFailed", ex, new Dictionary<string, object?> { ["appId"] = session.AppId });
        }
    }

    /// <summary>
    /// Pętla reconnectu do tego samego celu strony: przed kolejnymi próbami 250 ms, 500 ms, 1 s, potem co 2 s, liczone
    /// od zamknięcia, a dalej od startu poprzedniej próby. Każda próba i każde wywołanie setupu jest ograniczone
    /// terminem połączonym z tokenem stopu sesji. Wyjście procesu i Stop kończą pętlę po cichu; upływ terminu przy
    /// żywym procesie daje <c>cdpLost</c> i jeden <see cref="CdpLost"/>, a sesja zostaje zarejestrowana, żeby
    /// Close/Restart z AppMenu zatrzymały drzewo. <paramref name="trigger"/> (<c>cdpClosed</c> albo <c>renewSession</c>)
    /// trafia do <c>cdpReconnected</c> i <c>cdpLost</c>. Flaga <see cref="Session.Reconnecting"/> jest ustawiona przed
    /// startem pętli i zdejmowana na jej końcu (przy sukcesie przed obserwacją nowego kanału, którego zamknięcie zaczyna
    /// własną pętlę).
    /// </summary>
    private async Task ReconnectAsync(Session session, WebApp app, string initScript, long closedAt, CancellationTokenSource deadline, string trigger)
    {
        var reconnecting = true;
        try
        {
            using (deadline)
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, session.StopToken))
            {
                var token = linked.Token;
                var attempts = 0;
                var previous = closedAt;
                try
                {
                    while (true)
                    {
                        var wait = ReconnectBackoff[Math.Min(attempts, ReconnectBackoff.Length - 1)] - _time.GetElapsedTime(previous);
                        if (wait > TimeSpan.Zero)
                        {
                            await Task.Delay(wait, _time, token).ConfigureAwait(false);
                        }

                        token.ThrowIfCancellationRequested();
                        previous = _time.GetTimestamp();
                        attempts++;
                        if (await TryReconnectOnceAsync(session, initScript, token).ConfigureAwait(false) is { } cdp)
                        {
                            // Flaga zdjęta przed wpisem: kolejne odświeżenie po `cdpReconnected` nie trafi w `reconnecting`.
                            reconnecting = false;
                            session.Reconnecting = false;
                            _log.Info(Src, "cdpReconnected", new Dictionary<string, object?>
                            {
                                ["appId"] = session.AppId,
                                ["attempts"] = attempts,
                                ["elapsedMs"] = (long)_time.GetElapsedTime(closedAt).TotalMilliseconds,
                                ["trigger"] = trigger,
                            });
                            WatchCdp(session, app, initScript, cdp);
                            return;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Termin, Stop albo wyjście procesu: rozstrzyga sprawdzenie poniżej.
                }

                // Wyjście procesu (ProcessExited już poszło) i Stop kończą reconnect bez CdpLost.
                if (session.Stopping
                    || session.StopToken.IsCancellationRequested
                    || !deadline.IsCancellationRequested
                    || session.Process is not { HasExited: false })
                {
                    return;
                }

                _log.Warn(Src, "cdpLost", new Dictionary<string, object?>
                {
                    ["appId"] = session.AppId,
                    ["timeoutSeconds"] = _cdpReconnectTimeout.TotalSeconds,
                    ["attempts"] = attempts,
                    ["trigger"] = trigger,
                });
                _post(new CdpLost(session.AppId));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "cdpReconnectFailed", ex, new Dictionary<string, object?> { ["appId"] = session.AppId });
        }
        finally
        {
            if (reconnecting)
            {
                session.Reconnecting = false;
            }
        }
    }

    /// <summary>
    /// Jedna próba: transport do zapamiętanego celu i setup na nowej sesji; zwraca zainstalowaną bieżącą sesję albo
    /// <c>null</c>. Transport, który przyjdzie po terminie, Stopie albo wyjściu procesu, jest zwalniany bez skutku.
    /// </summary>
    private async Task<CdpSession?> TryReconnectOnceAsync(Session session, string initScript, CancellationToken token)
    {
        if (session.Endpoint is not { } endpoint)
        {
            return null;
        }

        var attempt = ConnectSafelyAsync(endpoint, token);
        ICdpTransport? transport;
        try
        {
            transport = await attempt.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Spóźniony transport porzuconej próby nie może ożyć jako kanał sesji.
            _ = attempt.ContinueWith(
                late => late.Result?.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
            throw;
        }

        if (transport is null)
        {
            return null;
        }

        var cdp = CreateCdp(session, transport);
        if (token.IsCancellationRequested || !session.TryAttach(cdp))
        {
            cdp.Dispose();
            token.ThrowIfCancellationRequested();
            return null;
        }

        cdp.Start();
        try
        {
            // Tylko trzy wywołania setupu: bez Page.navigate (strona już jest) i bez powtarzania czegokolwiek z przerwy.
            await InstallAsync(cdp, initScript, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CdpException or OperationCanceledException)
        {
            session.Detach(cdp);
            cdp.Dispose();
            token.ThrowIfCancellationRequested();
            return null;
        }

        if (token.IsCancellationRequested || !session.IsCurrent(cdp))
        {
            session.Detach(cdp);
            cdp.Dispose();
            token.ThrowIfCancellationRequested();
            return null;
        }

        return cdp;
    }

    /// <summary>Wywołanie seamu reconnectu, które nigdy nie rzuca: wyjątek albo anulowanie to nieudana próba.</summary>
    private async Task<ICdpTransport?> ConnectSafelyAsync(Uri endpoint, CancellationToken token)
    {
        try
        {
            return await _reconnect(endpoint, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Zdarzenia tylko z bieżącej sesji CDP: zdarzenie podmienionej albo zamkniętej sesji nie wysyła nic na nowej
    /// i nie loguje <c>hello</c>.
    /// </summary>
    private void OnCdpEvent(Session session, CdpSession cdp, string method, JsonElement parameters)
    {
        if (!session.IsCurrent(cdp))
        {
            return;
        }

        switch (method)
        {
            case "Page.frameNavigated":
                // Tylko binding: skrypt PageRuntime jest zarejestrowany raz na sesję CDP (InstallAsync).
                if (IsMainFrame(parameters))
                {
                    _ = RefreshBindingAsync(session, cdp);
                }

                break;

            case "Runtime.bindingCalled":
                OnBindingCalled(session, parameters);
                break;
        }
    }

    /// <summary>Główna ramka to ramka bez <c>parentId</c>; nawigacja iframe nie odświeża bindingu.</summary>
    private static bool IsMainFrame(JsonElement parameters) =>
        parameters.ValueKind == JsonValueKind.Object
        && parameters.TryGetProperty("frame", out var frame)
        && frame.ValueKind == JsonValueKind.Object
        && !frame.TryGetProperty("parentId", out _);

    private async Task RefreshBindingAsync(Session session, CdpSession cdp)
    {
        try
        {
            await cdp.SendAsync("Runtime.addBinding", new Dictionary<string, object?> { ["name"] = PageProtocol.BindingName }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Zamknięta albo podmieniona sesja to nie błąd odświeżenia: jej zerwaniem zajmuje się reconnect.
            if (session.IsCurrent(cdp) && !cdp.Closed.IsCompleted)
            {
                _log.Warn(Src, "bindingRefreshFailed", new Dictionary<string, object?>
                {
                    ["appId"] = session.AppId,
                    ["reason"] = ex.Message,
                });
            }
        }
    }

    private void OnBindingCalled(Session session, JsonElement parameters)
    {
        string? name = null;
        string? payload = null;
        if (parameters.ValueKind == JsonValueKind.Object)
        {
            if (parameters.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            {
                name = n.GetString();
            }

            if (parameters.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.String)
            {
                payload = p.GetString();
            }
        }

        if (!string.Equals(name, PageProtocol.BindingName, StringComparison.Ordinal))
        {
            RejectPageMessage(session, "binding");
            return;
        }

        var parsed = PageProtocol.Parse(payload);
        if (parsed is { Reason: "version", Version: { } version })
        {
            RejectPageVersion(session, version);
            return;
        }

        if (parsed.Hello is not { } hello)
        {
            RejectPageMessage(session, parsed.Reason ?? "unparsable");
            return;
        }

        _log.Info(Src, "pageHello", new Dictionary<string, object?>
        {
            ["appId"] = session.AppId,
            ["service"] = hello.Service,
            ["driverVersion"] = hello.DriverVersion,
            ["webdriver"] = hello.Webdriver,
        });
    }

    private void RejectPageMessage(Session session, string reason) =>
        _log.Warn(Src, "pageMessageRejected", new Dictionary<string, object?>
        {
            ["appId"] = session.AppId,
            ["reason"] = reason,
        });

    /// <summary>
    /// Koperta z innym major <c>v</c> (AD-7, DW-54): każda daje wpis w logu, a <see cref="PageVersionRejected"/> idzie
    /// najwyżej raz na sesję i tylko z sesji zarejestrowanej dla aplikacji, która nie jest zamykana. Stan kafelka
    /// ustala Orchestrator (AD-4).
    /// </summary>
    private void RejectPageVersion(Session session, int version)
    {
        _log.Warn(Src, "pageMessageRejected", new Dictionary<string, object?>
        {
            ["appId"] = session.AppId,
            ["reason"] = "version",
            ["v"] = version,
        });

        bool registered;
        lock (_gate)
        {
            registered = _sessions.TryGetValue(session.AppId, out var current) && ReferenceEquals(current, session);
        }

        if (registered && session.TryMarkVersionRejected())
        {
            _post(new PageVersionRejected(session.AppId, version));
        }
    }

    private void CdpFailed(StartApp command, WebApp app, Session session, string reason, string? detail)
    {
        if (session.StopRequested)
        {
            FailStart(command, "stoppedDuringStart");
            return;
        }

        if (session.Stopping)
        {
            // Proces zakończył się w trakcie łączenia: ProcessExited już poszło, więc bez drugiego ostrzeżenia.
            _log.Info(Src, "cdpAbandoned", new Dictionary<string, object?> { ["appId"] = app.Id, ["reason"] = "processExited" });
            _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
            return;
        }

        // Proces zostaje: o Failed i zamknięciu decyduje Orchestrator (AD-4). Wersja to FileVersion sprzed startu (AD-13).
        _log.Warn(Src, "cdpUnavailable", new Dictionary<string, object?>
        {
            ["cmdId"] = command.CmdId,
            ["appId"] = app.Id,
            ["cdpPort"] = app.CdpPort,
            ["timeoutSeconds"] = _cdpConnectTimeout.TotalSeconds,
            ["reason"] = reason,
            ["detail"] = detail,
            ["browserVersion"] = BrowserVersion.Format(session.BrowserVersion),
        });
        _post(new CommandFailed(command.CmdId, ErrorCode.CdpUnavailable));
    }

    /// <summary>Czeka do 20 s na główne okno w drzewie procesu; <c>null</c> po timeoucie, wyjściu procesu albo stopie.</summary>
    private async Task<WindowInfo?> WaitForWindowAsync(Session session, IBrowserProcess process)
    {
        var started = _time.GetTimestamp();
        while (!session.Stopping)
        {
            if (_findWindow(process.Id) is { } window)
            {
                return window;
            }

            if (process.HasExited || _time.GetElapsedTime(started) >= WindowTimeout)
            {
                return null;
            }

            try
            {
                await Task.Delay(PollInterval, _time, session.StopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    private void WatchExit(Session session, IBrowserProcess process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                if (session.Stopping)
                {
                    return;
                }

                var cdp = session.MarkStopping();
                Unregister(session);
                _log.Info(Src, "processExited", new Dictionary<string, object?>
                {
                    ["appId"] = session.AppId,
                    ["exitCode"] = process.ExitCode,
                });
                _post(new ProcessExited(session.AppId));

                // Po ProcessExited: zamknięte CDP i anulowane łączenie albo reconnect odpowiedzą dopiero teraz.
                cdp?.Dispose();
                session.CancelStart();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.Error(Src, "browserWatchFailed", ex, new Dictionary<string, object?> { ["appId"] = session.AppId });
            }
        });
    }

    private async Task StopCoreAsync(StopApp command)
    {
        try
        {
            Session? session;
            lock (_gate)
            {
                _sessions.Remove(command.AppId, out session);
            }

            if (session is not null)
            {
                await StopSessionAsync(session).ConfigureAwait(false);
            }

            _post(new ProcessExited(command.AppId));
            _post(new CommandCompleted(command.CmdId));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(command, ex);
        }
    }

    /// <summary>
    /// Zamknięcie CDP, łagodne zamknięcie okna głównego (do 3 s), potem zabicie całego drzewa procesów i czekanie do 5 s
    /// na wyjście korzenia, żeby kontrola profilu i portu ponownego startu nie widziała starej przeglądarki.
    /// </summary>
    private async Task StopSessionAsync(Session session)
    {
        session.StopRequested = true;
        var cdp = session.MarkStopping();
        session.CancelStart();
        cdp?.Dispose();
        if (session.Process is not { } process)
        {
            return;
        }

        process.CloseMainWindow();
        using (var grace = new CancellationTokenSource(GracefulCloseTimeout, _time))
        {
            try
            {
                await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Okno nie zamknęło się w 3 s: zostaje zabicie drzewa.
            }
        }

        process.KillTree();
        using var killed = new CancellationTokenSource(KillExitTimeout, _time);
        try
        {
            await process.WaitForExitAsync(killed.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Korzeń nie zakończył się w 5 s po zabiciu drzewa: stop i tak się kończy, a ślad zostaje w logu.
            _log.Warn(Src, "processKillTimedOut", new Dictionary<string, object?>
            {
                ["appId"] = session.AppId,
                ["pid"] = process.Id,
                ["timeoutSeconds"] = KillExitTimeout.TotalSeconds,
            });
        }
    }

    private void Unregister(Session session)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(session.AppId, out var current) && ReferenceEquals(current, session))
            {
                _sessions.Remove(session.AppId);
            }
        }
    }

    private void FailStart(
        StartApp command,
        string reason,
        string? file = null,
        string? detail = null,
        int? exitCode = null,
        ErrorCode errorCode = ErrorCode.Unexpected,
        (string Key, object? Value)? extra = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["cmdId"] = command.CmdId,
            ["appId"] = command.AppId,
            ["reason"] = reason,
        };
        if (file is not null)
        {
            data["file"] = file;
        }

        if (detail is not null)
        {
            data["detail"] = detail;
        }

        if (exitCode is not null)
        {
            data["exitCode"] = exitCode;
        }

        if (extra is { } field)
        {
            data[field.Key] = field.Value;
        }

        _log.Warn(Src, "browserStartFailed", data);
        _post(new CommandFailed(command.CmdId, errorCode));
    }

    private void Fail(Command command, Exception ex)
    {
        _log.Error(Src, "browserCommandFailed", ex, new Dictionary<string, object?> { ["cmdId"] = command.CmdId });
        _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
    }

    private sealed class Session(string appId, bool adopted = false)
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly object _gate = new();

        /// <summary>Bieżący kanał CDP; <c>null</c> przed połączeniem, w trakcie zerwania i po porażce setupu.</summary>
        private CdpSession? _cdp;
        private volatile bool _stopping;
        private volatile string? _initScript;

        /// <summary>Sesja wysłała już <see cref="PageVersionRejected"/>; pod <see cref="_gate"/>.</summary>
        private bool _versionRejected;

        public string AppId { get; } = appId;

        /// <summary>Sesja żywej przeglądarki przejętej przez sondę po restarcie (story 6.5), nie uruchomionej przez host.</summary>
        public bool Adopted { get; } = adopted;

        /// <summary>Okno adoptowanej przeglądarki (powtarzane przez Start, który ją ponownie używa); 0 dla startu.</summary>
        public long Hwnd { get; init; }

        /// <summary>Skrypt PageRuntime zainstalowanej sesji; <c>null</c> do końca startu albo udanego CDP po adopcji.</summary>
        public string? InitScript => _initScript;

        /// <summary>Start zakończony (albo CDP po adopcji gotowe): sesję można odświeżyć i ponownie użyć.</summary>
        public bool Ready => _initScript is not null;

        /// <summary>Trwa pętla reconnectu (po zamknięciu socketu albo odświeżeniu); ustawiana przed jej startem.</summary>
        public volatile bool Reconnecting;

        public void MarkReady(string initScript) => _initScript = initScript;

        /// <summary>Sesja ma bieżący kanał CDP i nie jest zamykana.</summary>
        public bool HasChannel
        {
            get
            {
                lock (_gate)
                {
                    return !_stopping && _cdp is not null;
                }
            }
        }

        /// <summary>Zdejmuje i zwraca bieżący kanał (atomowo); <c>null</c>, gdy go nie ma albo sesja jest zamykana.</summary>
        public CdpSession? DetachCurrent()
        {
            lock (_gate)
            {
                if (_stopping)
                {
                    return null;
                }

                var cdp = _cdp;
                _cdp = null;
                return cdp;
            }
        }

        public volatile IBrowserProcess? Process;

        /// <summary>Adres WebSocket celu strony z pierwszego połączenia; reconnect wraca tylko do niego.</summary>
        public volatile Uri? Endpoint;

        /// <summary><c>FileVersion</c> binarki sprzed startu (AD-13) albo <c>null</c>, gdy nieznana.</summary>
        public volatile Version? BrowserVersion;

        /// <summary>Sesja jest zamykana (Stop, ponowny start, timeout okna albo wyjście procesu): watcher milczy.</summary>
        public bool Stopping => _stopping;

        /// <summary>
        /// Zaznacza pierwsze odrzucenie wersji strony w sesji; <c>false</c>, gdy już było albo sesja jest zamykana
        /// (atomowo z <see cref="MarkStopping"/>), więc <see cref="PageVersionRejected"/> idzie najwyżej raz.
        /// </summary>
        public bool TryMarkVersionRejected()
        {
            lock (_gate)
            {
                if (_stopping || _versionRejected)
                {
                    return false;
                }

                _versionRejected = true;
                return true;
            }
        }

        /// <summary>Oznacza sesję jako zamykaną i zwraca bieżący kanał CDP do zamknięcia (atomowo z <see cref="TryAttach"/>).</summary>
        public CdpSession? MarkStopping()
        {
            lock (_gate)
            {
                _stopping = true;
                return _cdp;
            }
        }

        /// <summary>Ustawia bieżący kanał CDP; <c>false</c>, gdy sesja jest już zamykana (kanał trzeba zwolnić).</summary>
        public bool TryAttach(CdpSession cdp)
        {
            lock (_gate)
            {
                if (_stopping)
                {
                    return false;
                }

                _cdp = cdp;
                return true;
            }
        }

        /// <summary>Zdejmuje <paramref name="cdp"/>, jeśli to nadal bieżący kanał; <c>false</c>, gdy już go podmieniono.</summary>
        public bool Detach(CdpSession cdp)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_cdp, cdp))
                {
                    return false;
                }

                _cdp = null;
                return true;
            }
        }

        /// <summary>Kanał jest bieżący, a sesja nie jest zamykana: tylko jego zdarzenia mają skutek.</summary>
        public bool IsCurrent(CdpSession cdp)
        {
            lock (_gate)
            {
                return !_stopping && ReferenceEquals(_cdp, cdp);
            }
        }

        /// <summary>Zamknięcie zażądane z zewnątrz (Stop albo ponowny start).</summary>
        public volatile bool StopRequested;

        public CancellationToken StopToken => _stop.Token;

        public void CancelStart()
        {
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Token już zwolniony: start i tak się skończył.
            }
        }
    }
}
