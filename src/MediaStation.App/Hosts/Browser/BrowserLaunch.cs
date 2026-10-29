using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MediaStation.Core.Config;

namespace MediaStation.App.Hosts.Browser;

/// <summary>
/// Czyste decyzje startu przeglądarki bez I/O, wspólne dla BrowserHosta i dev harnessu: kandydaci na plik
/// wykonywalny, argument profilu, linia poleceń AD-1 i skrypt inicjalizujący stronę.
/// </summary>
internal static class BrowserLaunch
{
    /// <summary>Pierwszy dokument okna: serwis otwiera dopiero <c>Page.navigate</c> po instalacji bindingu i skryptu.</summary>
    public const string InitialUrl = "about:blank";

    /// <summary>Wartość argumentu <c>--user-data-dir</c> dla aplikacji.</summary>
    public static string ProfileArgument(string profileDir) => $"--user-data-dir={profileDir}";

    /// <summary>Argument portu CDP z <see cref="BuildArguments"/>.</summary>
    public static string DebuggingPortArgument(int cdpPort) => $"--remote-debugging-port={cdpPort}";

    /// <summary>
    /// Czy linia poleceń należy do przeglądarki z profilem <paramref name="profileDir"/>: argument
    /// <c>--user-data-dir</c> równy profilowi (z cudzysłowem lub bez, bez rozróżniania wielkości liter i końcowego ukośnika).
    /// Po ścieżce musi być koniec linii albo biały znak; końcowe ukośniki są dozwolone tylko przed nimi, więc podkatalog
    /// (<c>netflix\child</c>, <c>netflix\..\other</c>) nie pasuje. Wspólne dla adopcji BrowserHosta (story 6.5) i dev harnessu.
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
            var tail = end;
            while (tail < line.Length && line[tail] is '\\' or '/')
            {
                tail++;
            }

            if (tail == line.Length || char.IsWhiteSpace(line[tail]))
            {
                return true;
            }

            from = end;
        }

        return false;
    }

    /// <summary>Proces główny przeglądarki (korzeń drzewa z oknem): linia poleceń bez <c> --type=</c> procesów pomocniczych.</summary>
    public static bool IsBrowserRoot(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        return !commandLine.Contains(" --type=", StringComparison.Ordinal);
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

            (IsBrowserRoot(line) ? main : helpers).Add(pid);
        }

        main.AddRange(helpers);
        return main;
    }

    /// <summary>
    /// Czy linia poleceń ma dokładnie token <c>--remote-debugging-port=&lt;cdpPort&gt;</c> (z cudzysłowem lub bez):
    /// <c>9222</c> nie pasuje do <c>92220</c>. Adopcja łączy CDP tylko wtedy, gdy przeglądarka słucha na porcie aplikacji
    /// (AD-10), więc cudzy nasłuch na tym porcie nie dostanie bindingu.
    /// </summary>
    public static bool HasDebuggingPort(string? commandLine, int cdpPort)
    {
        if (string.IsNullOrEmpty(commandLine) || cdpPort <= 0)
        {
            return false;
        }

        var line = commandLine.Replace("\"", string.Empty, StringComparison.Ordinal);
        var needle = DebuggingPortArgument(cdpPort);
        var from = 0;
        while (line.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase) is var at and >= 0)
        {
            var end = at + needle.Length;
            var startsToken = at == 0 || char.IsWhiteSpace(line[at - 1]);
            if (startsToken && (end == line.Length || char.IsWhiteSpace(line[end])))
            {
                return true;
            }

            from = end;
        }

        return false;
    }

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

    /// <summary>
    /// Argumenty startu BrowserHosta dokładnie w kolejności AD-1 (spike 6.1): profil, port CDP na loopbacku (bez
    /// <c>--remote-debugging-address</c>, więc Chromium słucha tylko na <c>127.0.0.1</c>), bez ekranów powitalnych,
    /// autoplay bez gestu, <c>--kiosk</c> i <see cref="InitialUrl"/> na końcu.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string profileDir, int cdpPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDir);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cdpPort);

        return
        [
            ProfileArgument(profileDir),
            DebuggingPortArgument(cdpPort),
            "--no-first-run",
            "--no-default-browser-check",
            "--autoplay-policy=no-user-gesture-required",
            "--kiosk",
            InitialUrl,
        ];
    }

    /// <summary>
    /// Nazwa parametru funkcji, w którą host owija bundle PageRuntime; musi się zgadzać z
    /// <c>declare const __msPageConfig</c> w <c>web/page/runtime/main.ts</c>.
    /// </summary>
    public const string PageConfigParameter = "__msPageConfig";

    /// <summary>
    /// Skrypt <c>Page.addScriptToEvaluateOnNewDocument</c>: bundle IIFE z <c>runtime.js</c> dosłownie, owinięty w funkcję
    /// z configiem JSON jako parametrem, <c>(function (__msPageConfig) {\n&lt;plik&gt;\n})(&lt;config&gt;);</c> (AD-6).
    /// Plik nie jest zmieniany: końcowy średnik albo komentarz <c>//</c> w poprawionym ręcznie pliku zostaje bezpieczny,
    /// bo przed zamknięciem funkcji jest nowa linia. Strona nie dostaje globalnej nazwy poza bindingiem hosta.
    /// </summary>
    public static string BuildInitScript(string runtimeSource, string binding, string service, string? driverVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var config = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["binding"] = binding,
            ["service"] = service,
            ["driverVersion"] = driverVersion,
        });
        return $"(function ({PageConfigParameter}) {{\n{runtimeSource}\n}})({config});";
    }

    /// <summary>Usuwa z adresu query, fragment i dane logowania, zostawiając cel (do logów diagnostycznych).</summary>
    public static string SafeDiagnosticUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return "<invalid-url>";
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
        {
            return "<redacted-url>";
        }

        if (string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
        {
            return uri.GetLeftPart(UriPartial.Path);
        }

        var safe = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return safe.Uri.GetLeftPart(UriPartial.Path);
    }
}
