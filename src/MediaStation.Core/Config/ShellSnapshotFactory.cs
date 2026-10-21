using System;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Config;

/// <summary>
/// Snapshot shella z wyniku walidacji configu (AD-11): błędy dają ekran błędu z tą samą listą, a ich brak — placeholder.
/// </summary>
public static class ShellSnapshotFactory
{
    public static ShellSnapshot FromConfig(ConfigResult result, long rev)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ShellSnapshot(
            rev,
            result.Errors.Count == 0 ? ShellScreen.Placeholder : ShellScreen.ConfigError,
            result.Errors);
    }
}
