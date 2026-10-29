using System;

namespace MediaStation.App.Hosts.Browser;

/// <summary>
/// Wersja przeglądarki (AD-13): parsowanie tekstu z <c>FileVersion</c> binarki i pliku <c>Last Version</c> profilu,
/// odczyt z <c>product</c> odpowiedzi <c>Browser.getVersion</c>, porównanie i zapis do logu. Czyste funkcje bez I/O.
/// </summary>
internal static class BrowserVersion
{
    /// <summary>Wartość w logu, gdy wersji nie da się ustalić.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// Tekst po przycięciu jako <see cref="Version"/> z brakującym build/revision ustawionym na 0 (<c>156.0.4314</c> ==
    /// <c>156.0.4314.0</c>); <c>null</c> dla braku, pustego albo niesparsowanego tekstu.
    /// </summary>
    public static Version? Parse(string? text)
    {
        if (text is null || !Version.TryParse(text.Trim(), out var version))
        {
            return null;
        }

        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }

    /// <summary>
    /// Wersja z <c>product</c> (<c>Edg/156.0.4314.50</c>, <c>Chrome/…</c>): tekst po ostatnim <c>/</c>; <c>null</c>, gdy
    /// <c>/</c> nie ma albo wersja się nie parsuje.
    /// </summary>
    public static Version? FromProduct(string? product)
    {
        var slash = product?.LastIndexOf('/') ?? -1;
        return slash < 0 ? null : Parse(product![(slash + 1)..]);
    }

    /// <summary>Zmiana jest tylko wtedy, gdy obie wersje są znane i różne.</summary>
    public static bool Changed(Version? from, Version? to) => from is not null && to is not null && from != to;

    /// <summary>Wersja do logu: cztery części albo <see cref="Unknown"/>.</summary>
    public static string Format(Version? version) => version?.ToString() ?? Unknown;
}
