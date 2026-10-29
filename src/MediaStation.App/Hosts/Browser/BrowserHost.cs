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
/// binding i minimalny PageRuntime, nawiguje na start URL z manifestu drivera i loguje <c>hello</c> strony.
/// Story 6.3: binding wraca po każdej nawigacji głównej ramki, a zerwane CDP żywej sesji host łączy ponownie z tym
/// samym celem strony (backoff, termin <c>cdpReconnectTimeout</c>) i instaluje binding, <c>Page.enable</c> i skrypt na
/// nowej sesji; bez sukcesu w terminie wysyła <see cref="CdpLost"/>. W trakcie zerwania sesja nie ma kanału CDP i nic
/// nie jest buforowane ani powtarzane po powrocie.
/// Sam nigdy nie pokazuje, nie wynosi i nie minimalizuje okien (AD-14, AD-15). Metody portu tylko zlecają pracę
/// w tle i nie rzucają przez granicę portu (AD-4). Sonda i odświeżenie sesji to na razie ciche no-opy (story 6.5).
/// </summary>
internal sealed class BrowserHost : IAppHostPort
{
    /// <summary>Źródło wpisów logu hosta.</summary>
    public const string Src = "browser";

    /// <summary>Plik PageRuntime w katalogu <c>page</c> obok exe.</summary>
    public const string RuntimeFileName = "runtime.js";

    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

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
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

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

    /// <summary>Adopcja należy do story 6.5: do tego czasu sonda milczy (cisza jest poprawną odpowiedzią).</summary>
    public void Probe(ProbeExisting command)
    {
        ArgumentNullException.ThrowIfNull(command);
    }

    /// <summary>Odświeżenie sesji po wybudzeniu należy do story 6.5: do tego czasu komenda milczy.</summary>
    public void Renew(RenewSession command)
    {
        ArgumentNullException.ThrowIfNull(command);
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

            // Zasoby strony czytane z dysku przy każdym starcie (hotfix drivera, AD-13); bez nich nic nie startuje.
            var runtimePath = Path.Combine(_pageDir, RuntimeFileName);
            var runtime = _readFile(runtimePath);
            if (string.IsNullOrWhiteSpace(runtime))
            {
                FailStart(command, runtime is null ? "pageAssetMissing" : "pageAssetInvalid", runtimePath);
                return;
            }

            var manifestPath = Path.Combine(_pageDir, "drivers", app.Service, "manifest.json");
            var manifestText = _readFile(manifestPath);
            if (manifestText is null)
            {
                FailStart(command, "pageAssetMissing", manifestPath);
                return;
            }

            var parsed = DriverManifest.Parse(manifestText, app.Service);
            if (parsed.Manifest is not { } manifest)
            {
                FailStart(command, "pageAssetInvalid", manifestPath, parsed.Reason);
                return;
            }

            var exe = _resolveBrowser(app.Browser);
            if (exe is null)
            {
                FailStart(command, "browserNotFound");
                return;
            }

            var initScript = BrowserLaunch.BuildInitScript(runtime, PageProtocol.BindingName, app.Service, manifest.DriverVersion);
            var args = BrowserLaunch.BuildArguments(Path.Combine(_profilesDir, app.Id), app.CdpPort);

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
            _log.Info(Src, "cdpClosed", new Dictionary<string, object?>
            {
                ["appId"] = session.AppId,
                ["timeoutSeconds"] = _cdpReconnectTimeout.TotalSeconds,
            });
            _ = Task.Run(() => ReconnectAsync(session, app, initScript, closedAt, deadline));
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
    /// Close/Restart z AppMenu zatrzymały drzewo.
    /// </summary>
    private async Task ReconnectAsync(Session session, WebApp app, string initScript, long closedAt, CancellationTokenSource deadline)
    {
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
                            _log.Info(Src, "cdpReconnected", new Dictionary<string, object?>
                            {
                                ["appId"] = session.AppId,
                                ["attempts"] = attempts,
                                ["elapsedMs"] = (long)_time.GetElapsedTime(closedAt).TotalMilliseconds,
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
                });
                _post(new CdpLost(session.AppId));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "cdpReconnectFailed", ex, new Dictionary<string, object?> { ["appId"] = session.AppId });
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

        // Proces zostaje: o Failed i zamknięciu decyduje Orchestrator (AD-4). 6.4 doda wersję przeglądarki do wpisu.
        _log.Warn(Src, "cdpUnavailable", new Dictionary<string, object?>
        {
            ["cmdId"] = command.CmdId,
            ["appId"] = app.Id,
            ["cdpPort"] = app.CdpPort,
            ["timeoutSeconds"] = _cdpConnectTimeout.TotalSeconds,
            ["reason"] = reason,
            ["detail"] = detail,
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

    /// <summary>Zamknięcie CDP, łagodne zamknięcie okna głównego (do 3 s), potem zabicie całego drzewa procesów.</summary>
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

    private void FailStart(StartApp command, string reason, string? file = null, string? detail = null, int? exitCode = null)
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

        _log.Warn(Src, "browserStartFailed", data);
        _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
    }

    private void Fail(Command command, Exception ex)
    {
        _log.Error(Src, "browserCommandFailed", ex, new Dictionary<string, object?> { ["cmdId"] = command.CmdId });
        _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
    }

    private sealed class Session(string appId)
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly object _gate = new();

        /// <summary>Bieżący kanał CDP; <c>null</c> przed połączeniem, w trakcie zerwania i po porażce setupu.</summary>
        private CdpSession? _cdp;
        private volatile bool _stopping;

        public string AppId { get; } = appId;

        public volatile IBrowserProcess? Process;

        /// <summary>Adres WebSocket celu strony z pierwszego połączenia; reconnect wraca tylko do niego.</summary>
        public volatile Uri? Endpoint;

        /// <summary>Sesja jest zamykana (Stop, ponowny start, timeout okna albo wyjście procesu): watcher milczy.</summary>
        public bool Stopping => _stopping;

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
