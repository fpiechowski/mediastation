using System;
using System.Collections.Generic;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.App.Platform.System;

/// <summary>
/// Księgowość adaptera power (AD-4): każdy kandydat trafia do logu jako <c>powerSignal</c>, a tylko zwycięzca
/// reguły z <see cref="WakeSignalGate"/> daje <c>systemResumed</c> i Event <see cref="SystemResumed"/> w kolejce
/// Orchestratora. Okno deduplikacji jest tu, w adapterze — nigdy w <c>Orchestrator.Handle</c>.
/// </summary>
internal sealed class PowerSignalReporter : IDisposable
{
    /// <summary>Źródło wpisów logu adaptera.</summary>
    public const string Src = "system";

    private readonly ILog _log;
    private readonly WakeSignalGate _gate;
    private readonly Action<IOrchestratorMessage> _post;
    private volatile bool _disposed;

    public PowerSignalReporter(ILog log, WakeSignalGate gate, Action<IOrchestratorMessage> post)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(post);

        _log = log;
        _gate = gate;
        _post = post;
    }

    /// <summary>
    /// Zgłasza kandydata: zawsze <c>powerSignal {source}</c>, a dla zwycięzcy <c>systemResumed {source}</c>
    /// i Event w kolejce. Po <see cref="Dispose"/> jest no-opem, więc callback po zamknięciu pętli nic nie robi.
    /// </summary>
    public void Report(string source)
    {
        if (_disposed)
        {
            return;
        }

        _log.Info(Src, "powerSignal", new Dictionary<string, object?>
        {
            ["source"] = source,
        });

        if (!_gate.TryAccept(source))
        {
            return;
        }

        _log.Info(Src, "systemResumed", new Dictionary<string, object?>
        {
            ["source"] = source,
        });

        _post(new SystemResumed(source));
    }

    /// <summary>
    /// Jedna linia <c>powerSignalsRegistered</c> po starcie adaptera. Niepełna rejestracja to <c>warn</c>,
    /// a nie błąd: pozostałe źródła nadal działają (np. padnięte <c>RegisterPowerSettingNotification</c>).
    /// </summary>
    public void ReportRegistration(bool displayState, bool windowHook, bool powerModeChanged)
    {
        var data = new Dictionary<string, object?>
        {
            ["displayState"] = displayState,
            ["windowHook"] = windowHook,
            ["powerModeChanged"] = powerModeChanged,
        };

        if (displayState && windowHook && powerModeChanged)
        {
            _log.Info(Src, "powerSignalsRegistered", data);
        }
        else
        {
            _log.Warn(Src, "powerSignalsRegistered", data);
        }
    }

    public void Dispose() => _disposed = true;
}
