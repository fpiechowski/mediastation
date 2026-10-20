using System.IO;
using System.Reflection;
using System.Windows;
using MediaStation.App.Config;
using MediaStation.App.Logging;
using MediaStation.App.Shell;
using MediaStation.Core.Cli;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.App;

/// <summary>
/// Składa aplikację: log (pierwszy), odczyt configu (I/O tylko w App), walidacja w Core (każdy błąd w logu),
/// snapshot dla shella, okno shella.
/// </summary>
public static class CompositionRoot
{
    private const string Src = "app";

    private const string ConfigSrc = "config";

    private static ILog? _log;

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
        var log = new JsonLinesLog(TimeProvider.System, new FileLogSink(LogsDir));
        _log = log;
        AttachLifecycleLogging(app, log);
        log.Info(Src, "start", new Dictionary<string, object?>
        {
            ["version"] = AppVersion(),
            ["pid"] = Environment.ProcessId,
            ["args"] = args,
        });

        var parsed = CliArgs.Parse(args);
        if (parsed is CliParseResult.Invalid invalid)
        {
            // Złe argumenty to nie tryb: mutex nie jest brany, więc działająca instancja nie blokuje odpowiedzi.
            log.Error(Src, "invalidArguments", data: new Dictionary<string, object?> { ["reason"] = invalid.Reason });
            app.Shutdown((int)ExitCode.InvalidArguments);
            return;
        }

        var mode = ((CliParseResult.Parsed)parsed).Mode;
        if (!SingleInstance.TryAcquire(log))
        {
            log.Warn(Src, "instanceRejected", new Dictionary<string, object?> { ["mode"] = mode.LogName });
            app.Shutdown((int)ExitCode.AlreadyRunning);
            return;
        }

        if (mode is CliMode.Run)
        {
            StartRun(app, log);
        }
        else
        {
            RejectUnsupportedMode(app, log, mode);
        }
    }

    /// <summary>Tryb główny: config (I/O tylko w App), walidacja w Core, snapshot i okno shella.</summary>
    private static void StartRun(Application app, ILog log)
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

        var snapshot = new ShellSnapshot(
            Rev: 1,
            Screen: result.Errors.Count == 0 ? ShellScreen.Placeholder : ShellScreen.ConfigError,
            ConfigErrors: result.Errors);

        var bridge = new ShellBridge(snapshot, log);
        var window = new ShellWindow(bridge, WebView2UserDataDir, log);
        app.MainWindow = window;
        window.Show();
    }

    /// <summary>Tryb rozpoznany, ale jeszcze niezaimplementowany: wpis z nazwą trybu i kod <see cref="ExitCode.Unsupported"/>.</summary>
    private static void RejectUnsupportedMode(Application app, ILog log, CliMode mode)
    {
        var data = new Dictionary<string, object?> { ["mode"] = mode.LogName };
        if (mode is CliMode.Smoke smoke)
        {
            data["appId"] = smoke.AppId;
        }

        log.Warn(Src, "modeUnsupported", data);
        app.Shutdown((int)ExitCode.Unsupported);
    }

    private static void AttachLifecycleLogging(Application app, ILog log)
    {
        app.Exit += (_, e) => log.Info(Src, "stop", new Dictionary<string, object?> { ["exitCode"] = e.ApplicationExitCode });

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var data = new Dictionary<string, object?> { ["isTerminating"] = e.IsTerminating };
            if (e.ExceptionObject is not Exception)
            {
                data["exceptionObject"] = e.ExceptionObject?.ToString();
            }

            log.Error(Src, "unhandledException", e.ExceptionObject as Exception, data);
        };

        // Bez `Handled = true`: logujemy i zostawiamy domyślne zachowanie WPF (crash, restart przez --supervise).
        app.DispatcherUnhandledException += (_, e) => log.Error(Src, "dispatcherUnhandledException", e.Exception);

        TaskScheduler.UnobservedTaskException += (_, e) => log.Error(Src, "unobservedTaskException", e.Exception);
    }

    private static string? AppVersion()
    {
        var assembly = typeof(CompositionRoot).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
    }
}
