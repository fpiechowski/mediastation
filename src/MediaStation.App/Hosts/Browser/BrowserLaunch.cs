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
            $"--remote-debugging-port={cdpPort}",
            "--no-first-run",
            "--no-default-browser-check",
            "--autoplay-policy=no-user-gesture-required",
            "--kiosk",
            InitialUrl,
        ];
    }

    /// <summary>
    /// Skrypt <c>Page.addScriptToEvaluateOnNewDocument</c>: wywołanie wyrażenia z <c>runtime.js</c> z configiem JSON,
    /// <c>(&lt;runtime&gt;)(&lt;config&gt;);</c>. Końcowy średnik pliku jest zdejmowany, bo w nawiasie byłby błędem składni.
    /// </summary>
    public static string BuildInitScript(string runtimeSource, string binding, string service, string? driverVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var body = runtimeSource.TrimEnd();
        while (body.EndsWith(';'))
        {
            body = body[..^1].TrimEnd();
        }

        var config = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["binding"] = binding,
            ["service"] = service,
            ["driverVersion"] = driverVersion,
        });
        return $"({body}\n)({config});";
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
