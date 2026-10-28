using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaStation.App.Hosts.DevHarness;

/// <summary>
/// Czyste decyzje dev harnessu dla klienta Moonlight bez I/O: plik <c>Moonlight.exe</c> ze zmiennej środowiskowej,
/// argumenty <c>stream</c> i dopasowanie żywego procesu do wpisu (<c>host</c>, <c>app</c>) przy adopcji.
/// </summary>
internal static class HarnessMoonlight
{
    /// <summary>Zmienna ze ścieżką do <c>Moonlight.exe</c>; brak albo nieistniejący plik to <c>moonlightNotFound</c>.</summary>
    public const string ExeEnvVarName = "MEDIASTATION_HARNESS_MOONLIGHT";

    /// <summary>
    /// Klasa okna strumienia (SDL). Moonlight-qt najpierw pokazuje okno łączenia Qt (<c>Qt6…QWindowIcon</c>, „Uruchamianie
    /// input stream establishment…”), które po starcie strumienia się ukrywa; tylko okno SDL niesie obraz (bug 5.8).
    /// </summary>
    public const string StreamWindowClass = "SDL_app";

    /// <summary>Argumenty startu: <c>stream &lt;host&gt; &lt;app&gt;</c> w trybie borderless z wyciszeniem po utracie fokusu.</summary>
    public static IReadOnlyList<string> BuildArguments(string host, string app)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);

        return ["stream", host, app, "--display-mode", "borderless", "--mute-on-focus-loss"];
    }

    /// <summary>Kontrakt startu procesu Moonlight: plik, argumenty, katalog roboczy i zmienne środowiska do usunięcia z dziecka.</summary>
    public sealed record MoonlightLaunch(
        string Exe,
        IReadOnlyList<string> Args,
        string? WorkingDirectory,
        IReadOnlyList<string> EnvironmentToRemove);

    private const string MediaStationEnvPrefix = "MEDIASTATION_";

    /// <summary>Kontrakt MediaStation: katalog roboczy = folder exe, bez zmiennych <c>MEDIASTATION_*</c> w dziecku.</summary>
    public static MoonlightLaunch BuildLaunch(string exe, string host, string app, IEnumerable<string> environmentNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exe);
        ArgumentNullException.ThrowIfNull(environmentNames);

        return new MoonlightLaunch(exe, BuildArguments(host, app), WorkingDirectoryOf(exe), EnvironmentToRemove(environmentNames));
    }

    /// <summary>Ręczny start: <c>Moonlight.exe stream host app</c> z folderu exe, całe środowisko bez zmian.</summary>
    public static MoonlightLaunch ManualLaunch(string exe, string host, string app)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exe);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);

        return new MoonlightLaunch(exe, ["stream", host, app], WorkingDirectoryOf(exe), []);
    }

    /// <summary>Opisy różnic między kontraktami (argumenty dodatkowe, katalog roboczy, zmienne środowiska).</summary>
    public static IReadOnlyList<string> Differences(MoonlightLaunch manual, MoonlightLaunch actual)
    {
        ArgumentNullException.ThrowIfNull(manual);
        ArgumentNullException.ThrowIfNull(actual);

        var result = new List<string>();
        if (!string.Equals(manual.Exe, actual.Exe, StringComparison.OrdinalIgnoreCase))
        {
            result.Add($"exe: {manual.Exe} -> {actual.Exe}");
        }

        if (!string.Equals(manual.WorkingDirectory, actual.WorkingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            result.Add($"cwd: {manual.WorkingDirectory ?? "<inherited>"} -> {actual.WorkingDirectory ?? "<inherited>"}");
        }

        foreach (var arg in actual.Args.Except(manual.Args, StringComparer.Ordinal))
        {
            result.Add($"arg+: {arg}");
        }

        foreach (var arg in manual.Args.Except(actual.Args, StringComparer.Ordinal))
        {
            result.Add($"arg-: {arg}");
        }

        // Ręczny start dziedziczy środowisko powłoki bez MEDIASTATION_*; usunięcie tych zmiennych z dziecka to zgodność, nie różnica.
        foreach (var name in actual.EnvironmentToRemove.Where(n => !n.StartsWith(MediaStationEnvPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add($"env-: {name}");
        }

        return result;
    }

    /// <summary>Nazwy zmiennych <c>MEDIASTATION_*</c> (bez rozróżniania wielkości liter) do usunięcia z dziecka.</summary>
    public static IReadOnlyList<string> EnvironmentToRemove(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return [.. names.Where(n => n.StartsWith(MediaStationEnvPrefix, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>Opis środowiska <c>NAZWA=wartość</c>; wartości zmiennych z KEY, TOKEN, SECRET lub PASSWORD w nazwie to <c>&lt;redacted&gt;</c>.</summary>
    public static IReadOnlyList<string> DescribeEnvironment(IEnumerable<KeyValuePair<string, string?>> vars)
    {
        ArgumentNullException.ThrowIfNull(vars);

        string[] secretMarkers = ["KEY", "TOKEN", "SECRET", "PASSWORD"];
        return
        [
            .. vars
                .OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .Select(v => secretMarkers.Any(m => v.Key.Contains(m, StringComparison.OrdinalIgnoreCase))
                    ? $"{v.Key}=<redacted>"
                    : $"{v.Key}={v.Value}"),
        ];
    }

    /// <summary>Nakłada kontrakt na <paramref name="info"/>: katalog roboczy i usunięcie zmiennych ze środowiska dziecka.</summary>
    public static void Apply(System.Diagnostics.ProcessStartInfo info, MoonlightLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(launch);

        if (launch.WorkingDirectory is { } cwd)
        {
            info.WorkingDirectory = cwd;
        }

        foreach (var name in launch.EnvironmentToRemove)
        {
            info.Environment.Remove(name);
        }
    }

    private static string? WorkingDirectoryOf(string exe) => System.IO.Path.GetDirectoryName(exe) is { Length: > 0 } dir ? dir : null;

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
