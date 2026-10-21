using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using CoreOrchestrator = MediaStation.Core.Orchestrator.Orchestrator;

namespace MediaStation.App.Orchestrator;

/// <summary>
/// Wątek Orchestratora (AD-2): jedna kolejka i jeden konsument. Core pozostaje czystym handlerem,
/// a App dostarcza kolejkę, wątek i publikację snapshotów (AD-14).
/// </summary>
public sealed class OrchestratorLoop : IDisposable
{
    private const string Src = "orchestrator";

    private readonly CoreOrchestrator _orchestrator;
    private readonly Action<IReadOnlyList<Command>> _dispatch;
    private readonly Action<ShellSnapshot> _publish;
    private readonly ILog _log;
    private readonly BlockingCollection<IOrchestratorMessage> _queue = new();
    private readonly Thread _thread;
    private bool _started;
    private volatile bool _disposed;

    public OrchestratorLoop(
        CoreOrchestrator orchestrator,
        Action<IReadOnlyList<Command>> dispatch,
        Action<ShellSnapshot> publish,
        ILog log)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(log);

        _orchestrator = orchestrator;
        _dispatch = dispatch;
        _publish = publish;
        _log = log;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "orchestrator",
        };
    }

    /// <summary>Startuje wątek i wkłada <see cref="OrchestratorStarted"/> jako pierwszy komunikat kolejki.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Pętla Orchestratora została już wystartowana.");
        }

        _started = true;
        _thread.Start();
        _queue.Add(new OrchestratorStarted());
    }

    /// <summary>
    /// Wkłada komunikat do kolejki: jedyne wejście dla źródeł spoza pętli (adaptery, wykonawcy).
    /// Po <see cref="Dispose"/> jest no-opem, a wyścig z <see cref="BlockingCollection{T}.CompleteAdding"/>
    /// nie wypuszcza wyjątku z wywołania.
    /// </summary>
    public void Post(IOrchestratorMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_disposed)
        {
            return;
        }

        try
        {
            _queue.Add(message);
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding/Dispose wygrało wyścig (ObjectDisposedException też jest InvalidOperationException):
            // komunikat odpada razem z zamkniętą kolejką, bez wyjątku u wołającego.
        }
    }

    /// <summary>Kończy kolejkę i czeka na konsumenta; wywoływane na <c>Application.Exit</c>.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        if (_started)
        {
            _thread.Join();
        }

        _queue.Dispose();
    }

    private void Run()
    {
        foreach (var message in _queue.GetConsumingEnumerable())
        {
            try
            {
                var result = _orchestrator.Handle(message);

                // Porażka wykonawcy to w tej story tylko ślad w logu; przejście w `Failed` dokłada story 4.
                if (message is CommandFailed failed)
                {
                    _log.Warn(Src, "commandFailed", new Dictionary<string, object?>
                    {
                        ["cmdId"] = failed.CmdId,
                        ["errorCode"] = failed.ErrorCode,
                    });
                }

                // Zmiana foregroundu to fakt zapisany w logu bez reakcji: tryb i dźwięk ustala epic-windows-audio.
                if (message is ForegroundChanged changed)
                {
                    _log.Info(Src, "foregroundChanged", new Dictionary<string, object?>
                    {
                        ["hwnd"] = changed.Hwnd,
                    });
                }

                // Komendy są fire-and-forget (AD-14): dispatcher kieruje je do portów bez czekania na wynik.
                // Snapshot publikujemy dopiero po wysłaniu komend, żeby wykonawca nie wyprzedził widoku.
                _dispatch(result.Commands);

                if (result.Snapshot is { } snapshot)
                {
                    _publish(snapshot);
                }
            }
            catch (Exception ex)
            {
                // Jeden zły komunikat nie może zabić pętli ani Home (AD-2).
                _log.Error(Src, "orchestratorFailed", ex, new Dictionary<string, object?>
                {
                    ["type"] = message.GetType().Name,
                });
            }
        }
    }
}
