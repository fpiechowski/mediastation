using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MediaStation.App.Platform.Window;

/// <summary>
/// Prawdziwy WindowManager (AD-15): jedyny kod zmieniający widoczność, Z-order i foreground okien. <see cref="Execute"/>
/// tylko kolejkuje komendę i budzi wątek <c>window-manager</c> przez <c>PostThreadMessage</c>, więc pętla Orchestratora
/// nigdy nie woła Win32. Wątek ma własną pętlę komunikatów (kolejka wątku jest potrzebna do <c>AttachThreadInput</c>)
/// i wykonuje komendy przez <see cref="WindowCommandExecutor"/>. Wyjątki są łapane na granicy (AD-4).
/// </summary>
internal sealed class Win32WindowManagerPort : IWindowManagerPort, IDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ILog _log;
    private readonly WindowCommandExecutor _executor;
    private readonly ConcurrentQueue<WindowCommand> _queue = new();
    private readonly ManualResetEventSlim _threadReady = new(initialState: false);
    private readonly Thread _thread;
    private long _shellHwnd;
    private uint _threadId;
    private readonly Action<IOrchestratorMessage> _post;
    private volatile bool _ready;
    private volatile bool _disposed;

    /// <summary>Wątek nie przyjmie już komend (padł, skończył pętlę albo port został zwolniony).</summary>
    private volatile bool _dead;
    private volatile bool _started;

    public Win32WindowManagerPort(ILog log, Action<IOrchestratorMessage> post)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(post);

        _log = log;
        _post = post;
        _executor = new WindowCommandExecutor(
            new Win32WindowApi(),
            log,
            post,
            () => Interlocked.Read(ref _shellHwnd) is var shell and not 0 ? shell : null);
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "window-manager",
        };
    }

    /// <summary>Wpisuje uchwyt okna shella (znany dopiero po <c>Show()</c>); <see cref="ShowShell"/> bez niego daje <c>WindowNotFound</c>.</summary>
    public void SetShellHandle(long hwnd) => Interlocked.Exchange(ref _shellHwnd, hwnd);

    /// <summary>Startuje wątek WindowManagera; kolejne wywołanie jest błędem wołającego.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("WindowManager został już wystartowany.");
        }

        _started = true;
        _thread.Start();
    }

    /// <summary>
    /// Kolejkuje komendę i budzi wątek; nie blokuje się na Win32 i nie rzuca. Martwy, niewystartowany albo zwolniony
    /// port odpowiada <see cref="CommandFailed"/> (<see cref="ErrorCode.Unexpected"/>), nigdy ciszą.
    /// </summary>
    public void Execute(WindowCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            if (_dead || _disposed || !_started)
            {
                Fail(command);
                return;
            }

            // Przed gotowością kolejki wątku komendy opróżnia pierwszy przebieg wątku po ustawieniu znacznika.
            _queue.Enqueue(command);
            if (_dead)
            {
                // Wątek zdążył skończyć między sprawdzeniem a kolejką: nikt już nie opróżni kolejki.
                FailQueued();
            }
            else if (_ready)
            {
                PInvoke.PostThreadMessage(_threadId, PInvoke.WM_APP, default, default);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(WindowCommandExecutor.Src, "windowCommandQueueFailed", ex);
        }
    }

    /// <summary>Kończy pętlę komunikatów przez <c>WM_QUIT</c> i czeka na wątek; nieodebrane komendy są logowane.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_started)
        {
            return;
        }

        if (_threadReady.Wait(ShutdownTimeout))
        {
            PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
            if (_thread.Join(ShutdownTimeout))
            {
                return;
            }
        }

        _log.Warn(WindowCommandExecutor.Src, "windowManagerShutdownTimeout", new Dictionary<string, object?>
        {
            ["droppedCommands"] = _queue.Count,
        });
    }

    private void Fail(WindowCommand command)
    {
        _log.Warn(WindowCommandExecutor.Src, "windowCommand", new Dictionary<string, object?>
        {
            ["type"] = command.GetType().Name,
            ["cmdId"] = command.CmdId,
            ["result"] = ErrorCode.Unexpected.ToString(),
        });
        _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
    }

    private void FailQueued()
    {
        while (_queue.TryDequeue(out var command))
        {
            try
            {
                Fail(command);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.Error(WindowCommandExecutor.Src, "windowCommandQueueFailed", ex);
            }
        }
    }

    private void Run()
    {
        try
        {
            _threadId = PInvoke.GetCurrentThreadId();

            // Samo wywołanie PeekMessage tworzy kolejkę komunikatów wątku, zanim ktokolwiek zrobi PostThreadMessage.
            PInvoke.PeekMessage(out _, default, PInvoke.WM_APP, PInvoke.WM_APP, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
            _ready = true;
            _threadReady.Set();
            Drain();

            while (true)
            {
                var result = PInvoke.GetMessage(out var message, default, wMsgFilterMin: 0, wMsgFilterMax: 0);
                if (result.Value <= 0)
                {
                    return;
                }

                if (message.message == PInvoke.WM_APP)
                {
                    Drain();
                    continue;
                }

                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(WindowCommandExecutor.Src, "windowManagerThreadFailed", ex);
        }
        finally
        {
            _dead = true;
            _ready = false;
            _threadReady.Set();

            // Komendy, które nie zdążyły się wykonać, dostają porażkę zamiast ciszy.
            FailQueued();
        }
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out var command))
        {
            _executor.Execute(command);
        }
    }
}
