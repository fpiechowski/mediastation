using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Hosts.DevHarness;

/// <summary>
/// Host aplikacji <c>web</c> i <c>moonlight</c> tylko dla dev harnessu (<c>MEDIASTATION_DEV_HARNESS=1</c>): uruchamia
/// Edge/Chrome z osobnym profilem albo <c>Moonlight.exe stream</c>, znajduje okno główne w drzewie procesów i raportuje <see cref="AppWindowAttached"/>, a przy
/// zamknięciu procesu <see cref="ProcessExited"/>. Harness sam nie wynosi okna na wierzch (AD-14/15): robi to
/// WindowManager po komendzie Orchestratora. Bez portu CDP. Praca idzie na zadaniach w tle, więc metody portu
/// nie blokują pętli Orchestratora; żaden wyjątek nie przechodzi przez granicę portu (AD-4).
/// </summary>
internal sealed class DevHarnessHostPort : IAppHostPort
{
    /// <summary>Źródło wpisów logu hosta.</summary>
    public const string Src = "host";

    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly ILog _log;
    private readonly Action<IOrchestratorMessage> _post;
    private readonly IReadOnlyDictionary<string, AppEntry> _apps;
    private readonly IWindowApi _windows;
    private readonly Func<IReadOnlyList<KeyValuePair<int, int>>> _processParents;
    private readonly Func<IReadOnlyList<KeyValuePair<int, string>>> _commandLines;
    private readonly string _profilesDir;
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    internal DevHarnessHostPort(
        ILog log,
        Action<IOrchestratorMessage> post,
        IReadOnlyDictionary<string, AppEntry> apps,
        IWindowApi windows,
        Func<IReadOnlyList<KeyValuePair<int, int>>> processParents,
        Func<IReadOnlyList<KeyValuePair<int, string>>> commandLines,
        string profilesDir)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(processParents);
        ArgumentNullException.ThrowIfNull(commandLines);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesDir);

        _log = log;
        _post = post;
        _apps = apps;
        _windows = windows;
        _processParents = processParents;
        _commandLines = commandLines;
        _profilesDir = profilesDir;
    }

    /// <summary>Uruchamia przeglądarkę i w tle czeka na jej okno; wynik wraca jako Event z tym samym <c>cmdId</c>.</summary>
    public void Start(StartApp command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            _ = Task.Run(() => StartCore(command));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(command, ex);
        }
    }

    /// <summary>
    /// Zamyka aplikację (łagodnie, potem drzewo procesów) w tle i potwierdza <see cref="ProcessExited"/> oraz
    /// <see cref="CommandCompleted"/>; brak sesji to też sukces (nie ma czego zamykać).
    /// </summary>
    public void Stop(StopApp command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            _ = Task.Run(() => StopCore(command));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(command, ex);
        }
    }

    /// <summary>
    /// Podpina żywe okno przeglądarki z profilem <c>profiles\&lt;appId&gt;</c> (własna sesja albo proces z linii poleceń,
    /// np. po restarcie MediaStation) i raportuje <see cref="AppWindowAttached"/>; bez takiego okna milczy.
    /// Wyszukiwanie jest wolne, więc idzie poza pętlą Orchestratora.
    /// </summary>
    public void Probe(ProbeExisting command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            _ = Task.Run(() => ProbeCore(command));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "devHarnessProbeFailed", ex, new Dictionary<string, object?> { ["appId"] = command.AppId });
        }
    }

    /// <summary>Bez CDP nie ma sesji do odświeżenia: cisza jest poprawną odpowiedzią.</summary>
    public void Renew(RenewSession command)
    {
        ArgumentNullException.ThrowIfNull(command);
    }

    private void StopCore(StopApp command)
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
                StopSession(session);
            }

            _post(new ProcessExited(command.AppId));
            _post(new CommandCompleted(command.CmdId));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(command, ex);
        }
    }

    private void ProbeCore(ProbeExisting command)
    {
        try
        {
            Session? session;
            lock (_gate)
            {
                _sessions.TryGetValue(command.AppId, out session);
            }

            if (session is not null)
            {
                // Własna sesja: albo startuje (Process jeszcze pusty i Start sam zaraportuje okno), albo żyje.
                if (session.Process is { } own && !own.HasExited
                    && FindWindow(own.Id, HarnessLaunch.WindowClassOf(_apps.GetValueOrDefault(command.AppId))) is { } ownWindow)
                {
                    _post(new AppWindowAttached(command.AppId, ownWindow.Hwnd, own.Id));
                }

                return;
            }

            if (!_apps.TryGetValue(command.AppId, out var entry))
            {
                return;
            }

            var roots = HarnessLaunch.MatchingRootsFor(entry, _commandLines(), _profilesDir);
            var windowClass = HarnessLaunch.WindowClassOf(entry);
            HarnessBrowser.WebLaunch? probeLaunch = null;
            if (entry is WebApp web)
            {
                var exe = HarnessBrowser.ResolveExecutable(HarnessBrowser.ExecutableCandidates(
                    web.Browser,
                    Environment.GetEnvironmentVariable("ProgramFiles"),
                    Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                    Environment.GetEnvironmentVariable("LocalAppData")), File.Exists);
                if (exe is not null)
                {
                    probeLaunch = HarnessBrowser.BuildLaunch(exe, Path.Combine(_profilesDir, entry.Id),
                        HarnessBrowser.ResolveUrl(Environment.GetEnvironmentVariable(HarnessBrowser.UrlEnvVarName)));
                }
            }
            foreach (var pid in roots)
            {
                if (FindWindow(pid, windowClass) is not { } window)
                {
                    continue;
                }

                if (probeLaunch is not null)
                {
                    LaunchWebHandoff(entry.Id, probeLaunch);
                }
                var adopted = new Session { Process = Process.GetProcessById(pid) };
                lock (_gate)
                {
                    if (_sessions.ContainsKey(command.AppId))
                    {
                        return;
                    }

                    _sessions[command.AppId] = adopted;
                }

                _log.Info(Src, "appWindowAttached", new Dictionary<string, object?>
                {
                    ["appId"] = command.AppId,
                    ["hwnd"] = window.Hwnd,
                    ["rootPid"] = pid,
                });
                _post(new AppWindowAttached(command.AppId, window.Hwnd, pid));
                WatchExit(command.AppId, adopted);
                return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "devHarnessProbeFailed", ex, new Dictionary<string, object?> { ["appId"] = command.AppId });
        }
    }

    private void StartCore(StartApp command)
    {
        try
        {
            if (!_apps.TryGetValue(command.AppId, out var app))
            {
                FailWith(command, "unknownApp");
                return;
            }

            string? exe;
            string[] launchArgs;
            HarnessBrowser.WebLaunch? webLaunch = null;
            HarnessMoonlight.MoonlightLaunch? moonLaunch = null;
            switch (app)
            {
                case WebApp web:
                    var candidates = HarnessBrowser.ExecutableCandidates(
                        web.Browser,
                        Environment.GetEnvironmentVariable("ProgramFiles"),
                        Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                        Environment.GetEnvironmentVariable("LocalAppData"));
                    exe = HarnessBrowser.ResolveExecutable(candidates, File.Exists);
                    if (exe is null)
                    {
                        FailWith(command, "browserNotFound");
                        return;
                    }

                    var url = HarnessBrowser.ResolveUrl(Environment.GetEnvironmentVariable(HarnessBrowser.UrlEnvVarName));
                    webLaunch = HarnessBrowser.BuildLaunch(exe, Path.Combine(_profilesDir, app.Id), url);
                    launchArgs = [.. webLaunch.Arguments];
                    break;
                case MoonlightApp moon:
                    exe = HarnessMoonlight.ResolveExecutable(
                        Environment.GetEnvironmentVariable(HarnessMoonlight.ExeEnvVarName),
                        File.Exists);
                    if (exe is null)
                    {
                        FailWith(command, "moonlightNotFound");
                        return;
                    }

                    var envNames = Environment.GetEnvironmentVariables().Keys.Cast<string>().ToList();
                    moonLaunch = HarnessMoonlight.BuildLaunch(exe, moon.Host, moon.App, envNames);
                    launchArgs = [.. moonLaunch.Args];
                    break;
                default:
                    FailWith(command, "unknownApp");
                    return;
            }

            var windowClass = HarnessLaunch.WindowClassOf(app);

            // Działająca instancja profilu przeglądarki (bez własnej sesji) jest adoptowana zamiast drugiego startu:
            // drugi msedge.exe na tym samym profilu tylko przekazuje żądanie i kończy się.
            if (webLaunch is not null && TryAdoptRunningProfile(command, app, windowClass, webLaunch))
            {
                return;
            }

            // Sesja jest zarejestrowana przed startem procesu, więc Stop z luki nie ginie; drugi start tej samej
            // aplikacji najpierw zamyka poprzednią sesję, zamiast ją osierocić.
            var session = new Session();
            Session? previous;
            lock (_gate)
            {
                _sessions.Remove(app.Id, out previous);
                _sessions[app.Id] = session;
            }

            if (previous is not null)
            {
                StopSession(previous);
            }

            var info = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var arg in launchArgs)
            {
                info.ArgumentList.Add(arg);
            }

            if (moonLaunch is not null)
            {
                HarnessMoonlight.Apply(info, moonLaunch);
            }

            Process? started = null;
            try
            {
                started = Process.Start(info);
            }
            finally
            {
                if (started is null)
                {
                    Unregister(app.Id, session);
                }
            }

            if (started is null)
            {
                FailWith(command, "processStartFailed");
                return;
            }

            session.Process = started;

            if (moonLaunch is not null)
            {
                _log.Info(Src, "appLaunch", new Dictionary<string, object?>
                {
                    ["appId"] = app.Id,
                    ["commandLine"] = $"\"{exe}\" {string.Join(' ', launchArgs)}",
                    ["args"] = launchArgs,
                    ["workingDirectory"] = info.WorkingDirectory,
                    ["removedEnvironment"] = moonLaunch.EnvironmentToRemove,
                    ["environment"] = HarnessMoonlight.DescribeEnvironment(
                        info.Environment.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value))),
                    ["pid"] = started.Id,
                });
                LogStage(app.Id, "started", started, windowClass);
            }
            else
            {
                _log.Info(Src, "appLaunch", new Dictionary<string, object?>
                {
                    ["appId"] = app.Id,
                    ["commandLine"] = $"\"{exe}\" {string.Join(' ', webLaunch?.DiagnosticArguments ?? launchArgs)}",
                    ["args"] = webLaunch?.DiagnosticArguments ?? launchArgs,
                    ["workingDirectory"] = info.WorkingDirectory,
                    ["url"] = webLaunch?.DiagnosticUrl,
                    ["pid"] = started.Id,
                });
            }

            var hit = session.Stopping ? null : WaitForWindow(session, app, windowClass);
            if (hit is null || session.Stopping)
            {
                // Sesję zdejmujemy tylko własną (reference): nowszy start mógł już wpisać następną.
                Unregister(app.Id, session);
                var stoppedFromOutside = session.StopRequested;
                if (moonLaunch is not null && !stoppedFromOutside)
                {
                    LogStage(app.Id, "windowTimeout", started, windowClass);
                }
                else if (app is WebApp && !stoppedFromOutside)
                {
                    var exitCode = ExitCodeOf(started);
                    LogStage(
                        app.Id,
                        "windowTimeout",
                        started,
                        windowClass,
                        HarnessLaunch.WebFailedStage(exitCode is not null, exitCode ?? 0),
                        exitCode,
                        webLaunch?.DiagnosticUrl,
                        webLaunch?.DiagnosticArguments,
                        webLaunch is null ? null : $"\"{webLaunch.Executable}\" {string.Join(' ', webLaunch.DiagnosticArguments)}",
                        info.WorkingDirectory);
                }

                session.Stopping = true;
                Kill(session.Process ?? started);
                FailWith(command, stoppedFromOutside ? "stoppedDuringStart" : "windowTimeout");
                return;
            }

            _log.Info(Src, "appWindowAttached", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["hwnd"] = hit.Window.Hwnd,
                ["class"] = hit.Window.ClassName,
                ["rootPid"] = hit.RootPid,
            });
            _post(new AppWindowAttached(app.Id, hit.Window.Hwnd, hit.RootPid));
            _post(new CommandCompleted(command.CmdId));

            WatchExit(app.Id, session);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(command, ex);
        }
    }

    private void WatchExit(string appId, Session session)
    {
        _ = Task.Run(() =>
        {
            try
            {
                session.Process!.WaitForExit();
                if (session.Stopping)
                {
                    return;
                }

                Unregister(appId, session);
                _log.Info(Src, "processExited", new Dictionary<string, object?> { ["appId"] = appId });
                _post(new ProcessExited(appId));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.Error(Src, "devHarnessWatchFailed", ex, new Dictionary<string, object?> { ["appId"] = appId });
            }
        });
    }

    /// <summary>
    /// Czeka na okno. Launcher przeglądarki zakończony kodem 0 to możliwe przekazanie żądania instancji profilu:
    /// wtedy szukamy też w pozostałych procesach profilu i przepinamy <see cref="Session.Process"/> na ten, który
    /// ma okno. Zwraca okno i PID korzenia, w którym je znaleziono.
    /// </summary>
    private WindowHit? WaitForWindow(Session session, AppEntry app, string? windowClass)
    {
        var launcher = session.Process!;
        var clock = Stopwatch.StartNew();
        var found = HarnessLaunch.FindWindow(
            app,
            () => FindWindow(launcher.Id, windowClass) is { } own ? new WindowHit(own, launcher.Id) : null,
            () => ExitCodeOf(launcher),
            () =>
            {
                foreach (var pid in HarnessLaunch.MatchingRootsFor(app, _commandLines(), _profilesDir))
                {
                    if (pid != launcher.Id && FindWindow(pid, windowClass) is { } other)
                    {
                        return new WindowHit(other, pid);
                    }
                }

                return null;
            },
            () => session.Stopping,
            () => Thread.Sleep(PollInterval),
            () => clock.Elapsed,
            WindowTimeout);

        if (found is not null && found.RootPid != launcher.Id)
        {
            try
            {
                session.Process = Process.GetProcessById(found.RootPid);
            }
            catch (ArgumentException)
            {
                // Korzeń z oknem zniknął między wykryciem a pobraniem uchwytu: start się nie udał.
                return null;
            }
        }

        return found;
    }

    private static int? ExitCodeOf(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Adoptuje działającą instancję profilu przeglądarki z oknem, jeśli nie ma własnej sesji. Zwraca <c>true</c>,
    /// gdy sesja została wpisana i okno zaraportowane (bez uruchamiania nowego procesu).
    /// </summary>
    private bool TryAdoptRunningProfile(StartApp command, AppEntry app, string? windowClass, HarnessBrowser.WebLaunch launch)
    {
        lock (_gate)
        {
            if (_sessions.ContainsKey(app.Id))
            {
                return false;
            }
        }

        var roots = HarnessLaunch.MatchingRootsFor(app, _commandLines(), _profilesDir);
        if (roots.Count == 0)
        {
            return false;
        }

        foreach (var pid in roots)
        {
            if (FindWindow(pid, windowClass) is not { } window)
            {
                continue;
            }

            LaunchWebHandoff(app.Id, launch);
            Process process;
            try
            {
                process = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var adopted = new Session { Process = process };
            lock (_gate)
            {
                if (_sessions.ContainsKey(app.Id))
                {
                    return false;
                }

                _sessions[app.Id] = adopted;
            }

            _log.Info(Src, "appWindowAttached", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["hwnd"] = window.Hwnd,
                ["class"] = window.ClassName,
                ["rootPid"] = pid,
                ["adopted"] = true,
            });
            _post(new AppWindowAttached(app.Id, window.Hwnd, pid));
            _post(new CommandCompleted(command.CmdId));
            WatchExit(app.Id, adopted);
            return true;
        }

        return false;
    }

    private void LaunchWebHandoff(string appId, HarnessBrowser.WebLaunch launch)
    {
        var commandLine = $"\"{launch.Executable}\" {string.Join(' ', launch.DiagnosticArguments)}";
        try
        {
            var info = new ProcessStartInfo(launch.Executable) { UseShellExecute = false };
            foreach (var arg in launch.Arguments) info.ArgumentList.Add(arg);
            var process = Process.Start(info);
            _log.Info(Src, "appLaunch", new Dictionary<string, object?>
            {
                ["appId"] = appId, ["commandLine"] = commandLine,
                ["args"] = launch.DiagnosticArguments, ["workingDirectory"] = info.WorkingDirectory,
                ["url"] = launch.DiagnosticUrl, ["pid"] = process?.Id, ["handoff"] = true,
            });
            if (process is not null)
            {
                LogStage(appId, "handoff", process, HarnessLaunch.WindowClassOf(_apps.GetValueOrDefault(appId)),
                    url: launch.DiagnosticUrl, args: launch.DiagnosticArguments, commandLine: commandLine,
                    workingDirectory: info.WorkingDirectory);
            }
            else
            {
                _log.Warn(Src, "appLaunchStage", new Dictionary<string, object?>
                {
                    ["appId"] = appId, ["stage"] = "navigation", ["reason"] = "processStartReturnedNull",
                    ["pid"] = null, ["url"] = launch.DiagnosticUrl, ["commandLine"] = commandLine,
                    ["args"] = launch.DiagnosticArguments, ["workingDirectory"] = info.WorkingDirectory,
                });
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn(Src, "appLaunchStage", new Dictionary<string, object?>
            {
                ["appId"] = appId, ["stage"] = "navigation", ["reason"] = ex.GetType().Name, ["pid"] = null,
                ["url"] = launch.DiagnosticUrl, ["commandLine"] = commandLine,
                ["args"] = launch.DiagnosticArguments, ["workingDirectory"] = null,
            });
        }
    }

    private static IReadOnlyList<string> ReadMoonlightLogLines()
    {
        try
        {
            var files = new DirectoryInfo(Path.GetTempPath()).GetFiles("Moonlight-*.log")
                .Select(f => new KeyValuePair<string, DateTime>(f.FullName, f.LastWriteTimeUtc));
            if (HarnessMoonlight.LatestLogFile(files) is not { } latest)
            {
                return [];
            }

            using var stream = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    private static string TitleOf(long hwnd)
    {
        try
        {
            var text = new System.Text.StringBuilder(256);
            return GetWindowTextW((IntPtr)hwnd, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private void LogStage(string appId, string stage, Process process, string? windowClass, string? failedStage = null, int? exitCode = null, string? url = null, IReadOnlyList<string>? args = null, string? commandLine = null, string? workingDirectory = null)
    {
        try
        {
            var exited = false;
            try
            {
                exited = process.HasExited;
            }
            catch (InvalidOperationException)
            {
                exited = true;
            }

            var data = new Dictionary<string, object?>
            {
                ["appId"] = appId,
                ["stage"] = stage,
                ["pid"] = process.Id,
                ["exited"] = exited,
                ["expectedWindowClass"] = windowClass,
            };
            if (failedStage is not null)
            {
                data["failedStage"] = failedStage;
                data["exitCode"] = exitCode;
            }
            if (url is not null)
            {
                data["url"] = url;
            }
            if (args is not null)
            {
                data["args"] = args;
                data["commandLine"] = commandLine;
                data["workingDirectory"] = workingDirectory;
            }

            if (stage == "windowTimeout")
            {
                var tree = WindowTreePicker.Descendants(process.Id, _processParents());
                var snapshot = _windows.EnumerateTopLevel().ToList();
                data["windows"] = snapshot
                    .Where(w => tree.Contains(w.Pid))
                    .Select(w => $"hwnd={w.Hwnd} pid={w.Pid} class={w.ClassName} title=\"{TitleOf(w.Hwnd)}\" visible={w.Visible} owner={w.HasOwner}")
                    .ToList();
                if (failedStage is null)
                {
                    foreach (var (key, value) in HarnessMoonlight.TimeoutDiagnostics(snapshot, tree, windowClass, ReadMoonlightLogLines()))
                    {
                        data[key] = value;
                    }
                }
                _log.Warn(Src, "appLaunchStage", data);
            }
            else
            {
                _log.Info(Src, "appLaunchStage", data);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "devHarnessStageLogFailed", ex, new Dictionary<string, object?> { ["appId"] = appId });
        }
    }

    private WindowInfo? FindWindow(int rootPid, string? windowClass)
    {
        var tree = WindowTreePicker.Descendants(rootPid, _processParents());
        return WindowTreePicker.Pick(_windows.EnumerateTopLevel(), tree, windowClass);
    }


    private void Unregister(string appId, Session session)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(appId, out var current) && ReferenceEquals(current, session))
            {
                _sessions.Remove(appId);
            }
        }
    }

    /// <summary>Łagodne zamknięcie okna głównego (do 3 s), potem zabicie całego drzewa procesów.</summary>
    private static void StopSession(Session session)
    {
        session.Stopping = true;
        session.StopRequested = true;
        if (session.Process is not { } process)
        {
            return;
        }

        try
        {
            process.CloseMainWindow();
            process.WaitForExit(GracefulCloseTimeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Proces już zakończony albo bez okna: przechodzimy do zabicia drzewa.
        }

        Kill(process);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Proces już zakończony: stop jest idempotentny.
        }
    }

    private void FailWith(StartApp command, string reason)
    {
        _log.Warn(Src, "devHarnessStartFailed", new Dictionary<string, object?>
        {
            ["cmdId"] = command.CmdId,
            ["appId"] = command.AppId,
            ["reason"] = reason,
        });
        _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
    }

    private void Fail(Command command, Exception ex)
    {
        _log.Error(Src, "devHarnessCommandFailed", ex, new Dictionary<string, object?> { ["cmdId"] = command.CmdId });
        _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
    }

    private sealed record WindowHit(WindowInfo Window, int RootPid);

    private sealed class Session
    {
        public volatile Process? Process;

        /// <summary>Sesja jest zamykana (Stop, ponowny start albo timeout): watcher nie raportuje <see cref="ProcessExited"/>.</summary>
        public volatile bool Stopping;

        /// <summary>Zamknięcie zażądane z zewnątrz (Stop albo ponowny start), a nie z timeoutu okna.</summary>
        public volatile bool StopRequested;
    }
}
