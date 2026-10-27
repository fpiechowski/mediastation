using System;
using System.Collections.Generic;
using System.IO;
using MediaStation.Core.Config;

namespace MediaStation.App.Hosts.DevHarness;

/// <summary>
/// Czyste decyzje dev harnessu bez I/O: włączenie przez <c>MEDIASTATION_DEV_HARNESS=1</c>, kandydaci na plik
/// wykonywalny przeglądarki i linia poleceń. Harness nie otwiera portu CDP (to zadanie prawdziwego BrowserHosta).
/// </summary>
internal static class HarnessBrowser
{
    /// <summary>Nazwa zmiennej środowiskowej czytanej raz przy starcie, wyłącznie w trybie głównym.</summary>
    public const string EnvVarName = "MEDIASTATION_DEV_HARNESS";

    /// <summary>Zmienna z adresem okna harnessu; pusta albo brak oznacza <see cref="DefaultUrl"/>.</summary>
    public const string UrlEnvVarName = "MEDIASTATION_HARNESS_URL";

    /// <summary>Domyślny adres okna harnessu; harness sprawdza tylko okno, nie treść.</summary>
    public const string DefaultUrl = "about:blank";

    /// <summary>Adres z <see cref="UrlEnvVarName"/> albo <see cref="DefaultUrl"/>, gdy wartość jest pusta.</summary>
    public static string ResolveUrl(string? value) => string.IsNullOrWhiteSpace(value) ? DefaultUrl : value.Trim();

    /// <summary>Wartość argumentu <c>--user-data-dir</c> dla aplikacji.</summary>
    public static string ProfileArgument(string profileDir) => $"--user-data-dir={profileDir}";

    /// <summary>
    /// Czy linia poleceń należy do przeglądarki harnessu z profilem <paramref name="profileDir"/>: argument
    /// <c>--user-data-dir</c> równy profilowi (z cudzysłowem lub bez, bez rozróżniania wielkości liter i końcowego ukośnika).
    /// </summary>
    public static bool CommandLineMatchesProfile(string? commandLine, string profileDir)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            return false;
        }

        // Cudzysłowy (cały argument albo sama ścieżka) nie niosą znaczenia dla porównania, więc znikają.
        var line = commandLine.Replace("\"", string.Empty, StringComparison.Ordinal);
        var needle = ProfileArgument(profileDir.TrimEnd('\\', '/'));
        var from = 0;
        while (line.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase) is var at and >= 0)
        {
            var end = at + needle.Length;
            if (end == line.Length || line[end] is ' ' or '\\' or '/')
            {
                return true;
            }

            from = end;
        }

        return false;
    }

    /// <summary>
    /// PID-y procesów z profilem aplikacji, w kolejności prób adopcji: proces główny (bez <c>--type=</c>) przed
    /// procesami pomocniczymi (renderer, GPU), bo to on jest korzeniem drzewa z oknem.
    /// </summary>
    public static IReadOnlyList<int> MatchingRoots(IEnumerable<KeyValuePair<int, string>> commandLines, string profileDir)
    {
        ArgumentNullException.ThrowIfNull(commandLines);

        var main = new List<int>();
        var helpers = new List<int>();
        foreach (var (pid, line) in commandLines)
        {
            if (!CommandLineMatchesProfile(line, profileDir))
            {
                continue;
            }

            (line.Contains(" --type=", StringComparison.Ordinal) ? helpers : main).Add(pid);
        }

        main.AddRange(helpers);
        return main;
    }

    /// <summary>Harness działa tylko dla wartości dokładnie <c>1</c>; literówka nigdy go nie włącza.</summary>
    public static bool IsEnabled(string? value) => string.Equals(value?.Trim(), "1", StringComparison.Ordinal);

    /// <summary>Kolejność poszukiwań pliku wykonywalnego przeglądarki; pierwszy istniejący wygrywa.</summary>
    public static IReadOnlyList<string> ExecutableCandidates(
        BrowserKind browser,
        string? programFiles,
        string? programFilesX86,
        string? localAppData)
    {
        var roots = new List<string>();
        foreach (var root in new[] { programFilesX86, programFiles, localAppData })
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                roots.Add(root);
            }
        }

        var relative = browser switch
        {
            BrowserKind.Edge => Path.Combine("Microsoft", "Edge", "Application", "msedge.exe"),
            BrowserKind.Chrome => Path.Combine("Google", "Chrome", "Application", "chrome.exe"),
            _ => throw new ArgumentOutOfRangeException(nameof(browser)),
        };

        var candidates = new List<string>(roots.Count);
        foreach (var root in roots)
        {
            candidates.Add(Path.Combine(root, relative));
        }

        return candidates;
    }

    /// <summary>Pierwszy kandydat, dla którego <paramref name="exists"/> zwraca <c>true</c>; <c>null</c>, gdy przeglądarki brak.</summary>
    public static string? ResolveExecutable(IEnumerable<string> candidates, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(exists);

        foreach (var candidate in candidates)
        {
            if (exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Argumenty startu: osobny profil na aplikację, bez ekranów powitalnych, tryb okna z 6.1 (<c>--kiosk</c>), autoplay bez gestu i adres na końcu.</summary>
    public static IReadOnlyList<string> BuildArguments(string profileDir, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return
        [
            ProfileArgument(profileDir),
            "--no-first-run",
            "--no-default-browser-check",
            "--kiosk",
            "--autoplay-policy=no-user-gesture-required",
            url,
        ];
    }
}
