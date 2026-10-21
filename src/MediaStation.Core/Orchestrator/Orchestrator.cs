using System;
using System.Collections.Generic;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Orchestrator;

/// <summary>
/// Wynik jednego kroku Orchestratora: komendy do wykonawców i opcjonalnie nowy snapshot dla shella.
/// <see cref="Snapshot"/> jest <c>null</c>, gdy komunikat nie zmienia widoku.
/// </summary>
public sealed record OrchestratorResult(IReadOnlyList<Command> Commands, ShellSnapshot? Snapshot)
{
    /// <summary>Brak komend i brak snapshotu — komunikat zignorowany w tej story.</summary>
    public static OrchestratorResult None { get; } = new(Array.Empty<Command>(), null);
}

/// <summary>
/// Jedyny właściciel stanu sesji (AD-3): czysty handler komunikatu na komendy i snapshot (AD-14, AD-16).
/// Bez UI, I/O i czasu ambientowego; woła go wyłącznie jedna pętla w App, więc stan nie potrzebuje blokad.
/// </summary>
public sealed class Orchestrator
{
    private readonly List<AppState> _apps;
    private long _rev;
    private long _focusSeq;

    public Orchestrator(LoadedConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _apps = new List<AppState>(config.Apps.Apps.Count);
        foreach (var app in config.Apps.Apps)
        {
            _apps.Add(new AppState(app.Id, app.Category));
        }
    }

    /// <summary>
    /// Przetwarza jeden komunikat z kolejki. Snapshot powstaje wyłącznie z <see cref="OrchestratorStarted"/>:
    /// <c>rev</c> rośnie monotonicznie, a <c>seq</c> tylko przy nowym żądaniu fokusu.
    /// </summary>
    public OrchestratorResult Handle(IOrchestratorMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message switch
        {
            OrchestratorStarted => OnStarted(),
            _ => OrchestratorResult.None,
        };
    }

    private OrchestratorResult OnStarted()
    {
        _rev++;
        _focusSeq++;

        var apps = new AppSnapshot[_apps.Count];
        for (var i = 0; i < _apps.Count; i++)
        {
            var app = _apps[i];
            apps[i] = new AppSnapshot(app.AppId, app.Category, app.State);
        }

        var snapshot = new ShellSnapshot(
            _rev,
            ShellScreen.Placeholder,
            Array.Empty<ConfigError>(),
            apps,
            new FocusRequest(FocusRequest.DefaultTarget, _focusSeq));

        return new OrchestratorResult(Array.Empty<Command>(), snapshot);
    }

    private sealed class AppState(string appId, AppCategory category)
    {
        public string AppId { get; } = appId;

        public AppCategory Category { get; } = category;

        public AppLifecycleState State { get; set; } = AppLifecycleState.NotRunning;
    }
}
