using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MediaStation.Core.Config;

/// <summary>
/// <c>web/page/drivers/&lt;service&gt;/manifest.json</c> (AD-6): jedyne miejsce start URL serwisu. Host czyta plik z dysku
/// przy każdym starcie (hotfix, AD-13), a Core tylko go parsuje.
/// </summary>
/// <param name="Service">Nazwa serwisu; musi być równa <c>service</c> wpisu <c>apps.json</c>.</param>
/// <param name="Matches">Wzorce URL-i serwisu (dla drivera; host ich jeszcze nie używa).</param>
/// <param name="StartUrl">Bezwzględny adres <c>https</c>, na który host nawiguje po instalacji bindingu i skryptu.</param>
/// <param name="DriverVersion">Wersja drivera, którą strona odsyła w <c>hello</c>.</param>
public sealed record DriverManifest(string Service, IReadOnlyList<string> Matches, string StartUrl, string DriverVersion)
{
    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "service", "matches", "startUrl", "driverVersion",
    };

    /// <summary>
    /// Czysty parser: zwraca manifest albo powód odrzucenia (zły JSON, brak pola, nieznane pole, inny <c>service</c>,
    /// <c>startUrl</c> nie jest bezwzględnym <c>https</c>). Nigdy nie rzuca.
    /// </summary>
    public static DriverManifestResult Parse(string? json, string expectedService)
    {
        ArgumentNullException.ThrowIfNull(expectedService);

        if (string.IsNullOrWhiteSpace(json))
        {
            return DriverManifestResult.Rejected("pusty plik");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return DriverManifestResult.Rejected("oczekiwano obiektu JSON");
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!KnownFields.Contains(property.Name))
                {
                    return DriverManifestResult.Rejected($"nieznane pole „{property.Name}”");
                }
            }

            if (!TryString(root, "service", out var service))
            {
                return DriverManifestResult.Rejected("brak tekstowego pola „service”");
            }

            if (!string.Equals(service, expectedService, StringComparison.Ordinal))
            {
                return DriverManifestResult.Rejected($"service „{service}” różni się od „{expectedService}” z apps.json");
            }

            if (!root.TryGetProperty("matches", out var matchesElement) || matchesElement.ValueKind != JsonValueKind.Array)
            {
                return DriverManifestResult.Rejected("brak tablicy „matches”");
            }

            var matches = new List<string>();
            foreach (var match in matchesElement.EnumerateArray())
            {
                if (match.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(match.GetString()))
                {
                    return DriverManifestResult.Rejected("„matches” musi zawierać niepuste teksty");
                }

                matches.Add(match.GetString()!);
            }

            if (matches.Count == 0)
            {
                return DriverManifestResult.Rejected("„matches” nie może być puste");
            }

            if (!TryString(root, "startUrl", out var startUrl))
            {
                return DriverManifestResult.Rejected("brak tekstowego pola „startUrl”");
            }

            if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                return DriverManifestResult.Rejected($"startUrl „{startUrl}” nie jest bezwzględnym adresem https");
            }

            if (!TryString(root, "driverVersion", out var driverVersion))
            {
                return DriverManifestResult.Rejected("brak tekstowego pola „driverVersion”");
            }

            return new DriverManifestResult(new DriverManifest(service, matches, startUrl, driverVersion), null);
        }
        catch (JsonException)
        {
            return DriverManifestResult.Rejected("niepoprawny JSON");
        }
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            return false;
        }

        value = element.GetString()!;
        return true;
    }
}

/// <summary>Wynik <see cref="DriverManifest.Parse"/>: dokładnie jedno z <see cref="Manifest"/> i <see cref="Reason"/> nie jest <c>null</c>.</summary>
public sealed record DriverManifestResult(DriverManifest? Manifest, string? Reason)
{
    public static DriverManifestResult Rejected(string reason) => new(null, reason);
}
