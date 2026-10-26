using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Hosts.DevHarness;

/// <summary>
/// Host aplikacji web tylko dla dev harnessu (<c>MEDIASTATION_DEV_HARNESS=1</c>): uruchamia Edge/Chrome z osobnym
/// profilem, znajduje jego okno główne w drzewie procesów i raportuje <see cref="AppWindowAttached"/>, a przy
/// zamknięciu procesu <see cref="ProcessExited"/>. Harness sam nie wynosi okna na wierzch (AD-14/15): robi to
/// WindowManager po komendzie Orchestratora. Bez portu CDP. Praca idzie na zadaniach w tle, więc metody portu
/// nie blokują pętli Orchestratora; żaden wyjątek nie przechodzi przez granicę portu (AD-4).
/// </summary>
internal sealed class DevHarnessWebHostPort : IAppHostPort
{
    /// <summary>Źródło wpisów logu hosta.</summary>
    public const string Src = "host";

    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly ILog _log;
    private readonly Action<IOrchestratorMessage> _post;
    private readonly IReadOnlyDictionary<string, WebApp> _apps;
    private readonly IWindowApi _windows;
    private readonly Func<IReadOnlyList<KeyValuePair<int, int>>> _processParents;
    private readonly Func<IReadOnlyList<KeyValuePair<int, string>>> _commandLines;
    private readonly string _profilesDir;
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    internal DevHarnessWebHostPort(
        ILog log,
        Action<IOrchestratorMessage> post,
        IReadOnlyDictionary<string, WebApp> apps,
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
                if (session.Process is { } own && !own.HasExited && FindWindow(own.Id) is { } ownWindow)
                {
                    _post(new AppWindowAttached(command.AppId, ownWindow.Hwnd, own.Id));
                }

                return;
            }

            var roots = HarnessBrowser.MatchingRoots(_commandLines(), Path.Combine(_profilesDir, command.AppId));
            foreach (var pid in roots)
            {
                if (FindWindow(pid) is not { } window)
                {
                    continue;
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

            var candidates = HarnessBrowser.ExecutableCandidates(
                app.Browser,
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetEnvironmentVariable("LocalAppData"));
            var exe = HarnessBrowser.ResolveExecutable(candidates, File.Exists);
            if (exe is null)
            {
                FailWith(command, "browserNotFound");
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
            var url = HarnessBrowser.ResolveUrl(Environment.GetEnvironmentVariable(HarnessBrowser.UrlEnvVarName));
            foreach (var arg in HarnessBrowser.BuildArguments(Path.Combine(_profilesDir, app.Id), url))
            {
                info.ArgumentList.Add(arg);
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

            var window = session.Stopping ? null : WaitForWindow(session);
            if (window is null || session.Stopping)
            {
                // Sesję zdejmujemy tylko własną (reference): nowszy start mógł już wpisać następną.
                Unregister(app.Id, session);
                var stoppedFromOutside = session.StopRequested;
                session.Stopping = true;
                Kill(started);
                FailWith(command, stoppedFromOutside ? "stoppedDuringStart" : "windowTimeout");
                return;
            }

            _log.Info(Src, "appWindowAttached", new Dictionary<string, object?>
            {
                ["appId"] = app.Id,
                ["hwnd"] = window.Hwnd,
                ["rootPid"] = started.Id,
            });
            _post(new AppWindowAttached(app.Id, window.Hwnd, started.Id));
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

    private WindowInfo? WaitForWindow(Session session)
    {
        var process = session.Process!;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < WindowTimeout && !session.Stopping && !process.HasExited)
        {
            if (FindWindow(process.Id) is { } window)
            {
                return window;
            }

            Thread.Sleep(PollInterval);
        }

        return null;
    }

    private WindowInfo? FindWindow(int rootPid)
    {
        var tree = WindowTreePicker.Descendants(rootPid, _processParents());
        return WindowTreePicker.Pick(_windows.EnumerateTopLevel(), tree);
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

    private sealed class Session
    {
        public volatile Process? Process;

        /// <summary>Sesja jest zamykana (Stop, ponowny start albo timeout): watcher nie raportuje <see cref="ProcessExited"/>.</summary>
        public volatile bool Stopping;

        /// <summary>Zamknięcie zażądane z zewnątrz (Stop albo ponowny start), a nie z timeoutu okna.</summary>
        public volatile bool StopRequested;
    }
}
