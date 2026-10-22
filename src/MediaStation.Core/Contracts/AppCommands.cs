using System;

namespace MediaStation.Core.Contracts;

/// <summary>
/// Komenda startu aplikacji do hosta jej <see cref="AppType"/> (AD-4, AD-11). Niesie wyłącznie <see cref="AppId"/>,
/// bo to jedyny identyfikator aplikacji w systemie, a host zna swoje aplikacje.
/// </summary>
public sealed record StartApp(long CmdId, string AppId) : Command(CmdId);

/// <summary>
/// Komenda zamknięcia aplikacji do hosta jej <see cref="AppType"/> (AD-4, AD-11). Niesie wyłącznie
/// <see cref="AppId"/>, bo host zna swoje aplikacje i wie, jak zakończyć proces.
/// </summary>
public sealed record StopApp(long CmdId, string AppId) : Command(CmdId);

/// <summary>
/// Komenda sondowania hosta po restarcie (AD-4, AD-10): host podpina żywe okno aplikacji i raportuje
/// <see cref="AppWindowAttached"/>, albo milczy. Cisza jest poprawną odpowiedzią — brak żywej aplikacji
/// nie jest porażką komendy, więc sonda nie ma korelacji <see cref="CommandCompleted"/>/<see cref="CommandFailed"/>.
/// Niesie wyłącznie <see cref="AppId"/>, bo host zna swoje aplikacje.
/// </summary>
public sealed record ProbeExisting(long CmdId, string AppId) : Command(CmdId);

/// <summary>
/// Baza komend WindowManagera, jedynego kodu zmieniającego widoczność, Z-order i foreground okien (AD-15).
/// </summary>
public abstract record WindowCommand(long CmdId) : Command(CmdId);

/// <summary>WindowManager przenosi okno aplikacji na pierwszy plan.</summary>
public sealed record ActivateWindow(long CmdId, long Hwnd) : WindowCommand(CmdId);

/// <summary>WindowManager minimalizuje okno aplikacji; dla przeglądarki nigdy <c>SW_HIDE</c> (AD-5).</summary>
public sealed record MinimizeWindow(long CmdId, long Hwnd) : WindowCommand(CmdId);

/// <summary>WindowManager pokazuje ekran główny nad aplikacjami.</summary>
public sealed record ShowShell(long CmdId) : WindowCommand(CmdId);

/// <summary>
/// Baza komend schedulera timerów: Orchestrator planuje i anuluje terminy, a scheduler w Core
/// publikuje <see cref="TimerElapsed"/> z powrotem do kolejki. <c>cmdId</c> nie ma korelacji —
/// timer nie jest zadanym efektem, tylko terminem, więc scheduler nie odpowiada <see cref="CommandCompleted"/>.
/// </summary>
public abstract record TimerCommand(long CmdId) : Command(CmdId);

/// <summary>
/// Jednorazowy timer na <see cref="Due"/>. <see cref="Owner"/> to aplikacja, do której należy termin
/// (korelacja timeoutów <c>Starting</c> i <c>Stopping</c> żyje w Orchestratorze); duplikat <see cref="TimerId"/> zastępuje poprzedni timer.
/// </summary>
public sealed record ScheduleTimer(long CmdId, long TimerId, string Owner, DateTimeOffset Due) : TimerCommand(CmdId);

/// <summary>Anuluje timer <see cref="TimerId"/>; nieznany identyfikator jest no-opem.</summary>
public sealed record CancelTimer(long CmdId, long TimerId) : TimerCommand(CmdId);
