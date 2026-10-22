namespace MediaStation.Core.Input;

/// <summary>
/// Jedno naciśnięcie przycisku urządzenia <c>remote</c> w trybie <c>--calibrate-input</c> (AD-13): surowe kody
/// scalone z hooka LL, HID Consumer i <c>WM_INPUT</c> klawiatury. Kształt wpisu jest stały (osiem pól): brakujące
/// źródło daje <c>null</c>, bo autor porównuje kolumny między naciśnięciami. <see cref="HasVk"/> jest wprost
/// kryterium wyboru przycisku Klawiatura (kandydaci to tylko przyciski z VK).
/// </summary>
/// <param name="Signature">Sygnatura urządzenia z <c>input.json</c> (zawsze urządzenie <c>source: remote</c>).</param>
/// <param name="VirtualKey">Kod VK z hooka albo <c>WM_INPUT</c> klawiatury; <c>null</c>, gdy przycisk nie ma VK.</param>
/// <param name="Scan">Kod scan z hooka albo <c>WM_INPUT</c> klawiatury.</param>
/// <param name="UsagePage">Strona usage raportu HID Consumer (np. <c>0x0C</c>).</param>
/// <param name="Usage">Usage raportu HID Consumer (naciśnięty przycisk).</param>
/// <param name="Injected">Flaga <c>LLKHF_INJECTED</c> dopasowanego zdarzenia hooka.</param>
/// <param name="HasVk">Czy naciśnięcie dało się przypisać do VK (kandydat na przycisk Klawiatura).</param>
/// <param name="LlBeforeRawMs">
/// <c>rawTime − hookTime</c>: dodatnie znaczy hook przed <c>WM_INPUT</c>, ujemne odwrotnie, <c>null</c>, gdy
/// jednego ze źródeł nie było.
/// </param>
public sealed record RawInputEntry(
    string Signature,
    int? VirtualKey,
    int? Scan,
    int? UsagePage,
    int? Usage,
    bool? Injected,
    bool HasVk,
    int? LlBeforeRawMs);
