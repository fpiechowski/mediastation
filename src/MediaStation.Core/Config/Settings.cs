namespace MediaStation.Core.Config;

/// <summary>
/// <c>config/settings.json</c>: wartości polityk (AD-11). Plik jest wymagany, ale każde pole ma wartość domyślną,
/// więc <c>{}</c> jest poprawne. Czasy w pełnych sekundach, wszystkie liczby &gt; 0.
/// </summary>
public sealed record Settings
{
    /// <summary>Dni bez kolejnego odcinka, po których rekord <c>pending</c> daje kartę „Do oceny” (AD-12).</summary>
    public int RatingPendingDays { get; init; } = 7;

    /// <summary>Timeout stanu <c>Starting</c> w Orchestratorze, w sekundach (AD-14).</summary>
    public int StartingTimeout { get; init; } = 30;

    /// <summary>Czas oczekiwania BrowserHost na port CDP, w sekundach; krótszy niż <see cref="StartingTimeout"/> (AD-13).</summary>
    public int CdpConnectTimeout { get; init; } = 15;

    /// <summary>Czas reconnectu zerwanego CDP przed przejściem w <c>Failed</c>, w sekundach (AD-10).</summary>
    public int CdpReconnectTimeout { get; init; } = 30;

    public UpdateSettings Update { get; init; } = new();
}

/// <summary>Źródło aktualizacji z GitHub Releases (AD-17).</summary>
public sealed record UpdateSettings
{
    /// <summary>Repo w formacie <c>owner/name</c>.</summary>
    public string Repo { get; init; } = "fpiechowski/mediastation";
}
