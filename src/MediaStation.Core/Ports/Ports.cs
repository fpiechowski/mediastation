using System;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Ports;

/// <summary>
/// Port hosta aplikacji jednego <see cref="AppType"/> (AD-4). Orchestrator kieruje tu wyłącznie komendy,
/// a wynik wraca do jego kolejki jako <see cref="CommandCompleted"/> albo <see cref="CommandFailed"/>.
/// Wyjątkiem jest sonda <see cref="ProbeExisting"/>: host albo raportuje <see cref="AppWindowAttached"/>,
/// albo milczy, bo cisza jest poprawną odpowiedzią i sonda nie ma korelacji wyniku.
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
/// Port schedulera timerów (AD-14): wykonawca liczy termin na wstrzykniętym <see cref="TimeProvider"/>
/// i publikuje <see cref="TimerElapsed"/> do kolejki Orchestratora. Wykonawca nie przepuszcza wyjątku
/// przez granicę portu (AD-4).
/// </summary>
public interface ITimerPort
{
    /// <summary>Planuje albo anuluje timer; duplikat <c>timerId</c> zastępuje poprzedni termin.</summary>
    void Execute(TimerCommand command);
}
