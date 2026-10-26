using System;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Config;

/// <summary>
/// Snapshot startowy shella z wyniku walidacji configu (AD-11): błędy dają ekran błędu z tą samą listą,
/// a ich brak — placeholder z aplikacjami w stanie <c>notRunning</c> i bez żądania fokusu.
/// </summary>
public static class ShellSnapshotFactory
{
    public static ShellSnapshot FromConfig(ConfigResult result, long rev)
    {
        ArgumentNullException.ThrowIfNull(result);

        var hasErrors = result.Errors.Count > 0;
        var apps = hasErrors ? Array.Empty<AppSnapshot>() : Apps(result.Config);

        return new ShellSnapshot(
            rev,
            hasErrors ? ShellScreen.ConfigError : ShellScreen.Placeholder,
            result.Errors,
            apps,
            null);
    }

    private static AppSnapshot[] Apps(LoadedConfig? config)
    {
        if (config is null)
        {
            return [];
        }

        var apps = new AppSnapshot[config.Apps.Apps.Count];
        for (var i = 0; i < apps.Length; i++)
        {
            var app = config.Apps.Apps[i];
            apps[i] = new AppSnapshot(app.Id, app.Category, AppLifecycleState.NotRunning, app.Title);
        }

        return apps;
    }
}
