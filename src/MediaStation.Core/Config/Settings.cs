namespace MediaStation.Core.Config;

/// <summary>
/// <c>config/settings.json</c>: wartości polityk (AD-11). Plik jest wymagany, ale każde pole ma wartość domyślną,
/// więc <c>{}</c> jest poprawne. Czasy w pełnych sekundach, wszystkie liczby &gt; 0; <see cref="StartingTimeout"/>,
/// <see cref="StoppingTimeout"/> i <see cref="WindowResultTimeout"/> nie przekraczają 600 s.
/// </summary>
public sealed record Settings
{
    /// <summary>Dni bez kolejnego odcinka, po których rekord <c>pending</c> daje kartę „Do oceny” (AD-12).</summary>
    public int RatingPendingDays { get; init; } = 7;

    /// <summary>Timeout stanu <c>Starting</c> w Orchestratorze, w sekundach (AD-14).</summary>
    public int StartingTimeout { get; init; } = 30;

    /// <summary>Timeout stanu <c>Stopping</c> w Orchestratorze, w sekundach (AD-14).</summary>
    public int StoppingTimeout { get; init; } = 45;

    /// <summary>Czas oczekiwania BrowserHost na port CDP, w sekundach; krótszy niż <see cref="StartingTimeout"/> (AD-13).</summary>
    public int CdpConnectTimeout { get; init; } = 15;

    /// <summary>Czas reconnectu zerwanego CDP przed przejściem w <c>Failed</c>, w sekundach (AD-10).</summary>
    public int CdpReconnectTimeout { get; init; } = 30;

    /// <summary>Czas ustalenia okna na wierzchu po ostatnim <c>ForegroundChanged</c>, w milisekundach; 100–2000 (AD-15).</summary>
    public int ForegroundSettleMs { get; init; } = 300;

    /// <summary>Czas czekania oceny foreground na wyniki komend okiennych, zanim zostaną uznane za zgubione, w sekundach (AD-15).</summary>
    public int WindowResultTimeout { get; init; } = 5;

    public UpdateSettings Update { get; init; } = new();
}

/// <summary>Źródło aktualizacji z GitHub Releases (AD-17).</summary>
public sealed record UpdateSettings
{
    /// <summary>Repo w formacie <c>owner/name</c>.</summary>
    public string Repo { get; init; } = "fpiechowski/mediastation";
}
