namespace MediaStation.Core.Input;

/// <summary>
/// Znacznik własnych wstrzyknięć klawiatury (AD-3, AD-8): injector App wpisuje <see cref="MediaStation"/>
/// w <c>dwExtraInfo</c> obu zdarzeń <c>SendInput</c>, a polityka rozpoznaje po nim swój klawisz, który wrócił
/// z hooka. Wartość stoi w Core, bo reguła „ignoruj własne wstrzyknięcia” należy do polityki, a App tylko ją wpisuje.
/// Zero zostaje sprzętowi i obcym wstrzyknięciom, więc sama flaga <c>LLKHF_INJECTED</c> nie wystarcza do rozpoznania.
/// </summary>
public static class InjectionMarker
{
    /// <summary>„MS” i wersja znacznika: <c>0x4D53</c> + <c>0001</c>. Jedno źródło dla injectora i polityki.</summary>
    public const long MediaStation = 0x4D53_0001L;

    /// <summary>Czy zdarzenie hooka niesie znacznik MediaStation w <c>dwExtraInfo</c>.</summary>
    /// <param name="extraInfo">Wartość <c>dwExtraInfo</c> zdarzenia; <c>0</c> dla sprzętu i obcych wstrzyknięć.</param>
    public static bool IsOurs(long extraInfo) => extraInfo == MediaStation;
}
