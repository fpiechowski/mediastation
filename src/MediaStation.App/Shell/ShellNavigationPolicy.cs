namespace MediaStation.App.Shell;

/// <summary>
/// Decyzje nawigacji hosta shella (AD-16), bez typów WebView2/WPF, żeby dały się testować. Przepuszcza wyłącznie
/// URI z origin shella (scheme + host + port), zarówno dla głównej ramki, jak i ramek.
/// </summary>
public sealed class ShellNavigationPolicy
{
    private readonly Uri _origin;

    public ShellNavigationPolicy(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _origin = origin;
    }

    /// <summary>Czy nawigacja pod <paramref name="uri"/> jest dozwolona; nieparsowalny lub pusty URI to <c>false</c>.</summary>
    public bool IsAllowed(string? uri) => SameOrigin(_origin, uri);

    /// <summary>Origin to scheme + host + port po sparsowaniu <paramref name="uri"/>, nie prefiks napisu.</summary>
    public static bool SameOrigin(Uri origin, string? uri)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && string.Equals(parsed.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parsed.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
            && parsed.Port == origin.Port;
    }

    /// <summary>Anulowana nawigacja (<c>OperationCanceled</c>) nie jest awarią; każdy inny błąd tak.</summary>
    public static bool ShouldLogFailure(bool isSuccess, bool operationCanceled) => !isSuccess && !operationCanceled;
}
