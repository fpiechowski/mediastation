using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MediaStation.App.Config;
using MediaStation.App.Diagnostics;
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
using MediaStation.Core.Diagnostics;
using MediaStation.Core.Input;
using MediaStation.Core.Logging;
using MediaStation.Core.Orchestrator;
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
            AttachStopLogging(app, log);
            app.Shutdown((int)exit.Code);
            return;
        }

        if (decision is StartDecision.CalibrateInput)
        {
            // Tryb bez okna i bez UI: AttachStopLogging rejestruje stop przed startem huba, więc ostatni wpis
            // logu to stop także na ścieżce złego configu i porażki huba.
            var code = StartCalibration(app, log, time);
            app.Shutdown((int)code);
            return;
        }

        StartRun(app, log, time);
        AttachStopLogging(app, log);

        // Zawieszenie UI na żądanie (S10) dopiero po pierwszym renderze: ApplicationIdle jest niżej niż Render,
        // więc okno istnieje i jest widoczne, a blokada nie zatrzymuje InputHubu ani pompy Actions.
        if (DebugHangUi.Parse(Environment.GetEnvironmentVariable(DebugHangUi.EnvVarName)) is { } hang)
        {
            UiHang.Schedule(hang, log, action => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, action));
        }
    }

    /// <summary>
    /// Odczyt configu i walidacja (AD-11) wspólne dla trybu głównego i kalibracji: App czyta pliki, Core waliduje,
    /// a każdy błąd zostaje w logu jako <c>configError</c>. Zwraca też <see cref="Config"/> dla pozostałych epik.
    /// </summary>
    private static ConfigResult LoadConfig(ILog log)
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
        return result;
    }

    /// <summary>
    /// Tryb <c>--calibrate-input</c> (AD-13): sam InputHub z <see cref="InputCalibration"/> zamiast
    /// <see cref="InputPolicy"/>, bez okna i UI. Kalibracja nie mapuje i nie połyka klawiszy, a wpisy
    /// <c>rawInput</c> powstają wyłącznie dla urządzeń <c>source: remote</c>. App czeka na <c>Completed</c>
    /// z ograniczeniem <see cref="InputCalibration.DurationSeconds"/> + 10 s, bo tryb nie ma pętli UI, a martwy
    /// hub nie może zawiesić procesu; zwraca kod, którym <see cref="Start"/> kończy aplikację.
    /// </summary>
    private static ExitCode StartCalibration(Application app, ILog log, TimeProvider time)
    {
        AttachStopLogging(app, log);

        var result = LoadConfig(log);
        if (result.Config is not { } config)
        {
            // Bez ekranu błędu configu: tryb nie ma okna, więc samo configError i kod z decyzji Core.
            return StartDecision.CalibrationExit(result);
        }

        var rawSink = new LogRawInputSink(log);
        var calibration = new InputCalibration(config.Input, time, rawSink);
        using var completed = new ManualResetEventSlim(initialState: false);
        calibration.Completed += completed.Set;

        InputHub? hub = null;
        try
        {
            // Kalibracja nie emituje Actions, więc kolejka nie ma czytelnika i budzik jest pusty. Świeża kopia
            // routingu (bez zmian `epoch`) znaczy, że clip kursora nigdy nie obowiązuje w tym trybie.
            var traceInput = Environment.GetEnvironmentVariable("MS_INPUT_TRACE") == "1";
            hub = new InputHub(
                calibration,
                new ActionQueue(static () => { }),
                rawSink,
                time,
                new InputRoutingState(),
                traceInput ? log : null);
            hub.Start();

            // Termin kalibracji to 60 s; zapas 10 s pokrywa start wątku Input i rotację timera. Przekroczenie
            // znaczy, że hub nie domknął trybu, a całą pracą tego trybu jest wejście.
            if (!completed.Wait(TimeSpan.FromSeconds(InputCalibration.DurationSeconds + 10)))
            {
                return ExitCode.InputHubFailed;
            }

            return StartDecision.CalibrationExit(result);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Padnięty hub w trybie, którego całą pracą jest wejście, nie może udawać sukcesu.
            log.Error(LogRawInputSink.Src, "inputHubStartFailed", ex);
            return ExitCode.InputHubFailed;
        }
        finally
        {
            hub?.Dispose();
        }
    }

    /// <summary>Tryb główny: config (I/O tylko w App), walidacja w Core, snapshot i okno shella.</summary>
    private static void StartRun(Application app, ILog log, TimeProvider time)
    {
        var result = LoadConfig(log);

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

        // Scheduler timerów liczy termin na tym samym zegarze co log i power; publikuje do kolejki pętli leniwie,
        // bo pętla powstaje po routerze.
        var timers = new TimerScheduler(time, message => postsTo?.Post(message));

        // Jedna kopia routingu dla portu (wątek Orchestratora) i polityki (wątek Input): key-down czyta ją bez
        // czekania na Orchestrator (AD-8), a port odrzuca `epoch` równy i starszy.
        var routing = new InputRoutingState();

        // Zbiór okien MediaStation dla bramki foreground (AD-8): shell wpisuje się tu sam (Core nie zna jego uchwytu),
        // resztę publikuje Orchestrator komendą `SetManagedWindows`.
        var managedWindows = new ManagedWindows();
        var shellHwnd = new WindowInteropHelper(window).Handle.ToInt64();
        managedWindows.SetShell(shellHwnd);
        log.Info("input", "managedShell", new Dictionary<string, object?> { ["hwnd"] = shellHwnd });

        // Wstrzyknięcia klawiszy idą na własny wątek pompy (AD-14): router tylko kolejkuje, więc pętla Orchestratora
        // nie woła Win32 i Home nie czeka na SendInput. Pompa powstaje przed routerem, a startuje przed pętlą.
        var keyInjection = new KeyInjectionPump(KeyInjector.TrySendKey, log);

        var router = new CommandRouter(
            appTypes,
            hosts,
            new LogWindowManagerPort(log),
            new InputHubRoutingPort(routing, log, managedWindows),
            keyInjection,
            timers,
            log);

        keyInjection.Start();

        var loop = new OrchestratorLoop(new CoreOrchestrator(config, time), router.Dispatch, bridge.Publish, log);
        postsTo = loop;
        loop.Start();

        PowerSignalReporter? reporter = null;
        PowerNotifications? notifications = null;
        InputHub? inputHub = null;
        ActionPump? actionPump = null;

        // Handler przed startem adapterów: wyjątek w konstrukcji nie może zostawić pętli bez zwolnienia.
        // Sekwencję buduje się w chwili wyjścia, bo część slotów jest `null` przy rejestracji; kolejność zna
        // TeardownSequence: najpierw hub wejścia (żadnych nowych Actions), potem pompa Actions (domyka kolejkę),
        // pompa wstrzyknięć (porzuca niewstrzyknięte komendy), dalej adapter power, scheduler timerów i na końcu pętla.
        app.Exit += (_, _) => new TeardownSequence(
            inputHub,
            actionPump,
            keyInjection,
            notifications,
            reporter,
            timers,
            loop).Dispose();

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

        try
        {
            // InputHub po pętli i oknie: callback hooka wkłada Actions do kolejki Core, a pompa opróżnia ją
            // poza wątkiem Input (AD-2, AD-8). Sink loguje Action i wkłada ją do kolejki Orchestratora bez czekania.
            var actionSink = new OrchestratorActionSink(log, loop.Post);
            actionPump = new ActionPump(actionSink, actionSink);
            actionPump.Start();

            IForegroundProbe foregroundProbe = new ManagedForegroundProbe(new Win32ForegroundWindow(), managedWindows, (window, managed) =>
                log.Info("input", "foregroundGate", new Dictionary<string, object?>
                {
                    ["hwnd"] = window?.Hwnd,
                    ["pid"] = window?.Pid,
                    ["managed"] = managed,
                }));
            var inputPolicy = new InputPolicy(config.Input, time, routing, foregroundProbe);
            var traceInput = Environment.GetEnvironmentVariable("MS_INPUT_TRACE") == "1";
            IInputTarget inputTarget = traceInput ? new TracingInputTarget(inputPolicy, log) : inputPolicy;
            inputHub = new InputHub(inputTarget, actionPump.Queue, actionSink, time, routing, traceInput ? log : null);
            inputHub.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Padnięty InputHub nie może zabić startu: aplikacja żyje bez pilota i klawiatury.
            log.Error(LogActionSink.Src, "inputHubStartFailed", ex);
        }
    }

    private static void AttachLifecycleLogging(Application app, ILog log, DispatcherExceptionLog dispatcherExceptions)
    {
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

    /// <summary>
    /// Wpis <c>stop</c> rejestrujemy po <see cref="StartRun"/> (i po handlerze teardownu wejścia), bo .NET woła
    /// <c>Application.Exit</c> w kolejności subskrypcji: pompa domyka wtedy kolejkę Actions przed <c>stop</c>.
    /// Ścieżka wczesnego wyjścia nie ma łańcucha wejścia, więc rejestruje go tuż przed <c>Shutdown</c>.
    /// </summary>
    private static void AttachStopLogging(Application app, ILog log)
    {
        app.Exit += (_, e) => log.Info(Src, "stop", new Dictionary<string, object?> { ["exitCode"] = e.ApplicationExitCode });
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
