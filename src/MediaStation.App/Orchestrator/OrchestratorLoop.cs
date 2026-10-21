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
    private readonly Action<ShellSnapshot> _publish;
    private readonly ILog _log;
    private readonly BlockingCollection<IOrchestratorMessage> _queue = new();
    private readonly Thread _thread;
    private bool _started;
    private bool _disposed;

    public OrchestratorLoop(CoreOrchestrator orchestrator, Action<ShellSnapshot> publish, ILog log)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(log);

        _orchestrator = orchestrator;
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

                // Komendy są fire-and-forget (AD-14); story 1 nie produkuje jeszcze żadnej.
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
