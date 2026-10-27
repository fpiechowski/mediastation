using System;
using System.Collections.Generic;

namespace MediaStation.App.Hosts.DevHarness;

/// <summary>
/// Czyste decyzje dev harnessu dla klienta Moonlight bez I/O: plik <c>Moonlight.exe</c> ze zmiennej środowiskowej,
/// argumenty <c>stream</c> i dopasowanie żywego procesu do wpisu (<c>host</c>, <c>app</c>) przy adopcji.
/// </summary>
internal static class HarnessMoonlight
{
    /// <summary>Zmienna ze ścieżką do <c>Moonlight.exe</c>; brak albo nieistniejący plik to <c>moonlightNotFound</c>.</summary>
    public const string ExeEnvVarName = "MEDIASTATION_HARNESS_MOONLIGHT";

    /// <summary>Argumenty startu: <c>stream &lt;host&gt; &lt;app&gt;</c> w trybie borderless z wyciszeniem po utracie fokusu.</summary>
    public static IReadOnlyList<string> BuildArguments(string host, string app)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);

        return ["stream", host, app, "--display-mode", "borderless", "--mute-on-focus-loss"];
    }

    /// <summary>Plik z <paramref name="envValue"/> (bez cudzysłowów i spacji brzegowych), jeśli istnieje; inaczej <c>null</c>.</summary>
    public static string? ResolveExecutable(string? envValue, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        var path = envValue?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(path) || !exists(path) ? null : path;
    }

    /// <summary>
    /// Czy linia poleceń to <c>Moonlight.exe stream &lt;host&gt; &lt;app&gt;</c> dla wpisu: tokeny po <c>stream</c> równe
    /// hostowi i aplikacji (bez rozróżniania wielkości liter, cudzysłowy ignorowane; aplikacja może mieć spacje).
    /// </summary>
    public static bool CommandLineMatches(string? commandLine, string host, string app)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            return false;
        }

        var line = commandLine.Replace("\"", string.Empty, StringComparison.Ordinal);
        var needle = $" stream {host} {app}";
        var from = 0;
        while (line.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase) is var at and >= 0)
        {
            var end = at + needle.Length;
            if (end == line.Length || line[end] == ' ')
            {
                return line.Contains("moonlight", StringComparison.OrdinalIgnoreCase);
            }

            from = at + 1;
        }

        return false;
    }

    /// <summary>PID-y procesów <c>Moonlight.exe</c> dla wpisu, w kolejności z <paramref name="commandLines"/>.</summary>
    public static IReadOnlyList<int> MatchingRoots(IEnumerable<KeyValuePair<int, string>> commandLines, string host, string app)
    {
        ArgumentNullException.ThrowIfNull(commandLines);

        var roots = new List<int>();
        foreach (var (pid, line) in commandLines)
        {
            if (CommandLineMatches(line, host, app))
            {
                roots.Add(pid);
            }
        }

        return roots;
    }
}
