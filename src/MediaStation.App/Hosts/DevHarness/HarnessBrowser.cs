using System;
using System.Collections.Generic;
using System.Linq;
using MediaStation.App.Hosts.Browser;
using MediaStation.Core.Config;

namespace MediaStation.App.Hosts.DevHarness;

/// <summary>
/// Czyste decyzje dev harnessu bez I/O: włączenie przez <c>MEDIASTATION_DEV_HARNESS=1</c>, kandydaci na plik
/// wykonywalny przeglądarki i linia poleceń. Harness nie otwiera portu CDP (to zadanie prawdziwego BrowserHosta).
/// </summary>
internal static class HarnessBrowser
{
    internal sealed record WebLaunch(string Executable, string ProfileDir, string Url, IReadOnlyList<string> Arguments)
    {
        public string DiagnosticUrl => SafeDiagnosticUrl(Url);
        public IReadOnlyList<string> DiagnosticArguments => Arguments.Count == 0
            ? Arguments
            : [.. Arguments.Take(Arguments.Count - 1), DiagnosticUrl];
    }
    /// <summary>Nazwa zmiennej środowiskowej czytanej raz przy starcie, wyłącznie w trybie głównym.</summary>
    public const string EnvVarName = "MEDIASTATION_DEV_HARNESS";

    /// <summary>Zmienna z adresem okna harnessu; pusta albo brak oznacza <see cref="DefaultUrl"/>.</summary>
    public const string UrlEnvVarName = "MEDIASTATION_HARNESS_URL";

    /// <summary>Domyślny adres okna harnessu; harness sprawdza tylko okno, nie treść.</summary>
    public const string DefaultUrl = "about:blank";

    /// <summary>Adres z <see cref="UrlEnvVarName"/> albo <see cref="DefaultUrl"/>, gdy wartość jest pusta.</summary>
    public static string ResolveUrl(string? value) => string.IsNullOrWhiteSpace(value) ? DefaultUrl : value.Trim();

    /// <summary>Wartość argumentu <c>--user-data-dir</c> dla aplikacji (wspólna z BrowserHostem, <see cref="BrowserLaunch"/>).</summary>
    public static string ProfileArgument(string profileDir) => BrowserLaunch.ProfileArgument(profileDir);

    /// <summary>Czy linia poleceń należy do przeglądarki z profilem <paramref name="profileDir"/> (wspólne z BrowserHostem, <see cref="BrowserLaunch"/>).</summary>
    public static bool CommandLineMatchesProfile(string? commandLine, string profileDir) =>
        BrowserLaunch.CommandLineMatchesProfile(commandLine, profileDir);

    /// <summary>PID-y procesów z profilem aplikacji, proces główny przed pomocniczymi (wspólne z BrowserHostem, <see cref="BrowserLaunch"/>).</summary>
    public static IReadOnlyList<int> MatchingRoots(IEnumerable<KeyValuePair<int, string>> commandLines, string profileDir) =>
        BrowserLaunch.MatchingRoots(commandLines, profileDir);

    /// <summary>Harness działa tylko dla wartości dokładnie <c>1</c>; literówka nigdy go nie włącza.</summary>
    public static bool IsEnabled(string? value) => string.Equals(value?.Trim(), "1", StringComparison.Ordinal);

    /// <summary>Kolejność poszukiwań pliku wykonywalnego przeglądarki; pierwszy istniejący wygrywa (<see cref="BrowserLaunch"/>).</summary>
    public static IReadOnlyList<string> ExecutableCandidates(
        BrowserKind browser,
        string? programFiles,
        string? programFilesX86,
        string? localAppData) =>
        BrowserLaunch.ExecutableCandidates(browser, programFiles, programFilesX86, localAppData);

    /// <summary>Pierwszy kandydat, dla którego <paramref name="exists"/> zwraca <c>true</c>; <c>null</c>, gdy przeglądarki brak.</summary>
    public static string? ResolveExecutable(IEnumerable<string> candidates, Func<string, bool> exists) =>
        BrowserLaunch.ResolveExecutable(candidates, exists);

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

    /// <summary>Pure launch value shared by fresh starts and profile URL handoffs.</summary>
    public static WebLaunch BuildLaunch(string executable, string profileDir, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return new WebLaunch(executable, profileDir, url, BuildArguments(profileDir, url));
    }

    /// <summary>Removes query, fragment and authority credentials from launch diagnostics while retaining the destination.</summary>
    public static string SafeDiagnosticUrl(string url) => BrowserLaunch.SafeDiagnosticUrl(url);
}
