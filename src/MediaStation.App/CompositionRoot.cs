using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using MediaStation.App.Config;
using MediaStation.App.Hosts;
using MediaStation.App.Logging;
using MediaStation.App.Orchestrator;
using MediaStation.App.Platform.Input;
using MediaStation.App.Platform.System;
using MediaStation.App.Platform.Window;
using MediaStation.App.Shell;
using MediaStation.Core.Cli;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;

namespace MediaStation.App;

/// <summary>
/// Składa aplikację: log (pierwszy), odczyt configu (I/O tylko w App), walidacja w Core (każdy błąd w logu),
/// snapshot startowy dla shella, okno shella i pętla Orchestratora.
/// </summary>
public static class CompositionRoot
{
    private const string Src = "app";

    private const string ConfigSrc = "config";

    private const string SystemSrc = "system";

    private static ILog? _log;

    private static readonly DispatcherExceptionLog DispatcherExceptions = new();

    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaStation");

    public static string ConfigDir { get; } = Path.Combine(DataRoot, "config");

    public static string LogsDir { get; } = Path.Combine(DataRoot, "logs");

    public static string WebView2UserDataDir { get; } = Path.Combine(DataRoot, "webview2");

    /// <summary>
    /// Config po walidacji (AD-11) dla kolejnych epik; <c>null</c>, gdy start pokazał ekran błędu konfiguracji.
    /// </summary>
    public static LoadedConfig? Config { get; private set; }

    /// <summary>Log aplikacji (AD-13); dostępny od pierwszego kroku <see cref="Start"/>.</summary>
    public static ILog Log => _log ?? throw new InvalidOperationException("Log jest tworzony w CompositionRoot.Start.");

    public static void Start(Application app, string[] args)
    {
        var time = TimeProvider.System;
        var log = new JsonLinesLog(time, new FileLogSink(LogsDir));
        _log = log;
        AttachLifecycleLogging(app, log, DispatcherExceptions);
        log.Info(Src, "start", new Dictionary<string, object?>
        {
            ["version"] = AppVersion(),
            ["pid"] = Environment.ProcessId,
            ["args"] = args,
        });

        var decision = StartDecision.Decide(args, () => SingleInstance.TryAcquire(log));
        if (decision is StartDecision.Exit exit)
        {
            log.Write(exit.Level, Src, exit.Msg, exit.Data);
            app.Shutdown((int)exit.Code);
            return;
        }

        StartRun(app, log, time);
    }

    /// <summary>Tryb główny: config (I/O tylko w App), walidacja w Core, snapshot i okno shella.</summary>
    private static void StartRun(Application app, ILog log, TimeProvider time)
    {
        var files = ConfigFileReader.ReadAll(ConfigDir, ConfigPreflight.RequiredFiles);
        var result = ConfigValidation.Validate(files);
        foreach (var error in result.Errors)
        {
            log.Error(ConfigSrc, "configError", data: new Dictionary<string, object?>
            {
                ["file"] = error.File,
                ["path"] = error.Path,
                ["reason"] = error.Reason,
            });
        }

        Config = result.Config;

        // Bootstrap ma rev 0 i nie jest logowany: pierwszy snapshot z Orchestratora ma rev 1.
        var snapshot = ShellSnapshotFactory.FromConfig(result, rev: 0);
        var bridge = new ShellBridge(snapshot, log, app.Dispatcher);
        var window = new ShellWindow(bridge, WebView2UserDataDir, log);
        app.MainWindow = window;
        window.Show();

        // Przy błędzie configu Orchestrator nie startuje: shell dostaje ConfigError, a proces żyje.
        if (result.Config is not { } config)
        {
            return;
        }

        // Mapa appId → AppType bierze się z tych samych rekordów apps.json, z których Orchestrator bierze pointer.
        var appTypes = new Dictionary<string, AppType>(StringComparer.Ordinal);
        foreach (var entry in config.Apps.Apps)
        {
            appTypes.Add(entry.Id, AppTypeOf(entry));
        }

        // Adaptery „brak hosta” odpowiadają w kolejce pętli, a pętla powstaje po routerze, więc delegat czyta ją leniwie.
        OrchestratorLoop? postsTo = null;
        var hosts = new Dictionary<AppType, IAppHostPort>
        {
            [AppType.Web] = new MissingHostPort(log, message => postsTo?.Post(message)),
            [AppType.Moonlight] = new MissingHostPort(log, message => postsTo?.Post(message)),
            [AppType.Native] = new MissingHostPort(log, message => postsTo?.Post(message)),
        };

        var router = new CommandRouter(
            appTypes,
            hosts,
            new LogWindowManagerPort(log),
            new LogInputRoutingPort(log),
            log);

        var loop = new OrchestratorLoop(new CoreOrchestrator(config), router.Dispatch, bridge.Publish, log);
        postsTo = loop;
        loop.Start();

        PowerSignalReporter? reporter = null;
        PowerNotifications? notifications = null;

        // Handler przed startem adaptera: wyjątek w konstrukcji nie może zostawić pętli bez zwolnienia.
        // Kolejność zwolnienia: najpierw adapter (żadnych nowych Eventów), potem pętla.
        app.Exit += (_, _) =>
        {
            try
            {
                notifications?.Dispose();
            }
            finally
            {
                try
                {
                    reporter?.Dispose();
                }
                finally
                {
                    loop.Dispose();
                }
            }
        };

        try
        {
            // Adapter po starcie pętli: pierwszy komunikat kolejki to zawsze OrchestratorStarted, a każdy
            // sygnał wybudzenia trafia już do żywej kolejki. Hook idzie na top-level HWND shella, bo
            // broadcast nie dociera do okien message-only.
            reporter = new PowerSignalReporter(log, new WakeSignalGate(time), loop.Post);
            notifications = PowerNotifications.Start(new WindowInteropHelper(window).Handle, reporter);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Padnięty adapter power nie może zabić startu: aplikacja żyje bez sygnałów wybudzenia.
            log.Error(SystemSrc, "powerNotificationsStartFailed", ex);
        }
    }

    private static void AttachLifecycleLogging(Application app, ILog log, DispatcherExceptionLog dispatcherExceptions)
    {
        app.Exit += (_, e) => log.Info(Src, "stop", new Dictionary<string, object?> { ["exitCode"] = e.ApplicationExitCode });

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            // Wyjątek z Dispatchera jest już zalogowany: AppDomain nie dopisuje drugiego wpisu dla tego samego obiektu.
            if (e.ExceptionObject is Exception logged && dispatcherExceptions.TryConsume(logged))
            {
                return;
            }

            var data = new Dictionary<string, object?> { ["isTerminating"] = e.IsTerminating };
            if (e.ExceptionObject is not Exception)
            {
                data["exceptionObject"] = e.ExceptionObject?.ToString();
            }

            log.Error(Src, "unhandledException", e.ExceptionObject as Exception, data);
        };

        // Bez `Handled = true`: logujemy i zostawiamy domyślne zachowanie WPF (crash, restart przez --supervise).
        app.DispatcherUnhandledException += (_, e) =>
        {
            dispatcherExceptions.Mark(e.Exception);
            log.Error(Src, "dispatcherUnhandledException", e.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) => log.Error(Src, "unobservedTaskException", e.Exception);
    }

    private static string? AppVersion()
    {
        var assembly = typeof(CompositionRoot).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
    }

    /// <summary>Typ aplikacji z rekordu <c>apps.json</c>; nim router wybiera hosta dla <c>StartApp</c> (AD-11).</summary>
    private static AppType AppTypeOf(AppEntry app) => app switch
    {
        WebApp => AppType.Web,
        MoonlightApp => AppType.Moonlight,
        NativeApp => AppType.Native,
        _ => throw new InvalidOperationException($"Nieznany typ aplikacji {app.GetType().FullName}."),
    };
}
