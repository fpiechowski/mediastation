using System;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Ports;

/// <summary>
/// Port hosta aplikacji jednego <see cref="AppType"/> (AD-4). Orchestrator kieruje tu wyłącznie komendy,
/// a wynik wraca do jego kolejki jako <see cref="CommandCompleted"/> albo <see cref="CommandFailed"/>.
/// Wyjątkami są sonda <see cref="ProbeExisting"/> i odświeżenie sesji <see cref="RenewSession"/>: żadna nie ma
/// korelacji wyniku. Sonda albo raportuje <see cref="AppWindowAttached"/>, albo milczy; odświeżenie sesji milczy
/// zawsze, bo cisza jest poprawną odpowiedzią.
/// Wykonawca nie przepuszcza wyjątku przez granicę portu: każdy błąd raportuje jako Event.
/// </summary>
public interface IAppHostPort
{
    /// <summary>Uruchamia aplikację; host odpowiada <see cref="CommandCompleted"/> albo <see cref="CommandFailed"/>.</summary>
    void Start(StartApp command);

    /// <summary>Zamyka aplikację; host odpowiada <see cref="CommandCompleted"/> albo <see cref="CommandFailed"/>.</summary>
    void Stop(StopApp command);

    /// <summary>
    /// Sondowanie po restarcie (AD-10): host podpina żywe okno aplikacji i raportuje <see cref="AppWindowAttached"/>,
    /// albo milczy. Cisza jest poprawną odpowiedzią — brak żywej aplikacji nie jest porażką komendy,
    /// więc sonda nie odpowiada <see cref="CommandCompleted"/> ani <see cref="CommandFailed"/>.
    /// </summary>
    void Probe(ProbeExisting command);

    /// <summary>
    /// Odświeżenie sesji CDP po wybudzeniu (AD-10): host odtwarza połączenie i <c>addBinding</c>, a wyniku
    /// nie raportuje. Cisza jest poprawną odpowiedzią — świeża sesja nie jest żadnym zdarzeniem cyklu życia,
    /// a zerwane <c>CDP</c> raportuje osobne <see cref="CdpLost"/>.
    /// </summary>
    void Renew(RenewSession command);
}

/// <summary>
/// Port WindowManagera (AD-15): jedyny kod zmieniający widoczność, Z-order i foreground okien.
/// Wykonawca nie przepuszcza wyjątku przez granicę portu (AD-4).
/// </summary>
public interface IWindowManagerPort
{
    /// <summary>Wykonuje komendę okienkową (aktywacja, minimalizacja, pokazanie shella).</summary>
    void Execute(WindowCommand command);
}

/// <summary>
/// Port routingu wejścia (AD-8): InputHub dostaje nową lokalną kopię trybu i decyduje według niej,
/// bez czekania na Orchestrator. Wykonawca nie przepuszcza wyjątku przez granicę portu (AD-4).
/// </summary>
public interface IInputRoutingPort
{
    /// <summary>Przekazuje InputHubowi nowy tryb routingu wraz z <c>epoch</c>.</summary>
    void Execute(SetInputRouting command);
}

/// <summary>
/// Port wstrzyknięć klawiatury (AD-3, AD-8): wykonawca wysyła key-down i key-up ze znacznikiem
/// <see cref="InjectionMarker.MediaStation"/>, więc polityka odróżni własny klawisz od naciśnięcia pilota.
/// Fire-and-forget jak port routingu z 2.3: <c>SendKey</c> nie dostaje <see cref="CommandCompleted"/>,
/// a porażkę widać w logu. Wykonawca nie przepuszcza wyjątku przez granicę portu (AD-4).
/// </summary>
public interface IKeyInjectionPort
{
    /// <summary>Zleca wstrzyknięcie jednego naciśnięcia klawisza; wywołanie nie blokuje się na Win32 i nie rzuca.</summary>
    void Execute(SendKey command);
}

/// <summary>
/// Port schedulera timerów (AD-14): wykonawca liczy termin na wstrzykniętym <see cref="TimeProvider"/>
/// i publikuje <see cref="TimerElapsed"/> do kolejki Orchestratora. Wykonawca nie przepuszcza wyjątku
/// przez granicę portu (AD-4).
/// </summary>
public interface ITimerPort
{
    /// <summary>Planuje albo anuluje timer; duplikat <c>timerId</c> zastępuje poprzedni termin.</summary>
    void Execute(TimerCommand command);
}

/// <summary>
/// Port konsumenta Actions z wątku Input (AD-8): <see cref="Deliver"/> jest wołany z wątku opróżniającego
/// kolejkę, więc wykonawca nie blokuje się długo, nie czeka na Orchestrator ani UI i nie rzuca wyjątku
/// przez granicę portu (AD-4). Kolejność wywołań odpowiada kolejności kolejki.
/// </summary>
public interface IActionSink
{
    /// <summary>Dostarcza jedną Action; wywołanie nie blokuje się długo i nie rzuca.</summary>
    void Deliver(ActionMessage action);
}

/// <summary>
/// Port raportu porażek InputHubu (AD-13): błędy rejestracji hooka, Raw Input i okna message-only trafiają
/// do logu jako <c>inputHubError</c> bez kodów klawiszy. Osobny od <see cref="IActionSink"/>, bo story 5
/// podmienia tylko sink Actions, a błędy huba zawsze idą do logu. Wołany z wątku Input: nie blokuje i nie rzuca.
/// </summary>
public interface IInputHubErrorSink
{
    /// <summary>Raportuje jedną porażkę operacji huba; wywołanie nie blokuje i nie rzuca.</summary>
    void Report(InputHubError error);
}

/// <summary>
/// Port surowych zdarzeń kalibracji wejścia (AD-13): jedno <see cref="Deliver"/> na jedno naciśnięcie przycisku
/// urządzenia <c>source: remote</c>. Wołany z wątku Input poza callbackiem hooka: nie blokuje się długo i nie rzuca.
/// Surowych kodów nie dostaje żaden inny tryb ani żaden inny port.
/// </summary>
public interface IRawInputSink
{
    /// <summary>Dostarcza jeden scalony wpis kalibracji; wywołanie nie blokuje się długo i nie rzuca.</summary>
    void Deliver(RawInputEntry entry);
}
