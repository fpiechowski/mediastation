using System.Text.Json.Serialization;

namespace MediaStation.Core.Contracts;

/// <summary>
/// Wszystko, co trafia do jednej kolejki Orchestratora (AD-2, AD-3): Intent z UI, Event od wykonawców i Action z InputHub.
/// </summary>
public interface IOrchestratorMessage;

/// <summary>
/// Baza intencji z Shell UI do Orchestratora. Konkretne Intents dopisują epiki 2–4.
/// </summary>
public abstract record Intent : IOrchestratorMessage;

/// <summary>
/// Baza komend z Orchestratora do wykonawców (AD-4). Każda komenda ma <see cref="CmdId"/>,
/// a wykonawca odpowiada <see cref="CommandCompleted"/> albo <see cref="CommandFailed"/>.
/// Wyjątkami są <see cref="ProbeExisting"/> i <see cref="RenewSession"/>: obie są fire-and-forget,
/// a cisza hosta jest poprawną odpowiedzią (sonda bez żywej aplikacji, odświeżenie sesji bez wyniku).
/// </summary>
/// <param name="CmdId">Identyfikator komendy nadawany przez Orchestrator; zawsze pierwsze pole payloadu.</param>
public abstract record Command([property: JsonPropertyOrder(-1)] long CmdId);

/// <summary>
/// Baza zdarzeń: fakty raportowane przez wykonawców do Orchestratora (AD-4), nazwy w czasie przeszłym.
/// </summary>
public abstract record Event : IOrchestratorMessage;

/// <summary>
/// Pętla Orchestratora wystartowała; pierwszy komunikat kolejki i jedyne źródło pierwszego snapshotu.
/// </summary>
public sealed record OrchestratorStarted : Event;

/// <summary>
/// Wybór kafelka w Shellu (AD-16): Orchestrator uruchamia aplikację <see cref="AppId"/> ze stanu
/// <c>NotRunning</c> albo przywraca ją z <c>Background</c> (<c>ActivateWindow</c> i routing wejścia);
/// pozostałe stany są ignorowane.
/// </summary>
public sealed record LaunchApp(string AppId) : Intent;

/// <summary>
/// Zamknięcie aplikacji z kafelka (AD-16): działa w <c>Background</c> i <c>Failed</c>, gdzie Orchestrator
/// przechodzi w <c>Stopping</c> i wysyła <c>StopApp</c>.
/// </summary>
public sealed record CloseApp(string AppId) : Intent;

/// <summary>
/// Restart aplikacji z kafelka (AD-16): działa w <c>Background</c> i <c>Failed</c>; po potwierdzonym stopie
/// Orchestrator startuje aplikację ponownie (<c>StartApp</c>).
/// </summary>
public sealed record RestartApp(string AppId) : Intent;

/// <summary>
/// Komputer wrócił z uśpienia; adapter power raportuje fakt, a Orchestrator przechodzi na ekran główny
/// z nowym <c>seq</c> fokusu. Aplikacja z <c>Foreground</c> schodzi w <c>Background</c> batchem
/// <c>MinimizeWindow</c> → <c>ShowShell</c> → routing <c>shell</c>, a każda aplikacja webowa w
/// <c>Foreground</c> albo <c>Background</c> dostaje <see cref="RenewSession"/>. <see cref="Source"/>
/// niesie nazwę sygnału Windows 1:1 do logu i JSON-a.
/// </summary>
public sealed record SystemResumed(string Source) : Event;

/// <summary>
/// Host podpiął okno aplikacji. Przy starcie (<c>Starting</c>) Orchestrator przechodzi w <c>Foreground</c>
/// i wysyła <c>ActivateWindow</c> oraz routing wejścia dla typu aplikacji. Bez wcześniejszego <c>StartApp</c>
/// (<c>NotRunning</c>) okno jest adoptowane: Orchestrator zapamiętuje <see cref="Hwnd"/>, przechodzi w
/// <c>Background</c> i wysyła <c>MinimizeWindow</c>, a <c>ShowShell</c> — tylko gdy żadna inna aplikacja nie jest
/// aktywna — trzyma shell na wierzchu (AD-10, AD-15).
/// </summary>
/// <param name="Hwnd">Uchwyt okna; <c>long</c>, bo <c>IntPtr</c> nie jest kontraktem JSON, a HWND nie mieści się w <c>int</c>.</param>
/// <param name="RootPid">PID korzenia drzewa procesów aplikacji; WindowManager i AudioRouter dostają go w dalszych komendach.</param>
public sealed record AppWindowAttached(string AppId, long Hwnd, int RootPid) : Event;

/// <summary>
/// Proces aplikacji zakończył się (AD-4): host raportuje fakt, który zamyka <c>Stopping</c> (AD-10) bez czekania
/// na <see cref="CommandCompleted"/>. W <c>Starting</c>, <c>Foreground</c> i <c>Background</c> Orchestrator przechodzi
/// wtedy w <c>Failed</c>; w <c>NotRunning</c>, <c>Failed</c> i <c>Stopping</c> bez korelacji stopu fakt jest ignorowany.
/// </summary>
public sealed record ProcessExited(string AppId) : Event;

/// <summary>
/// Połączenie CDP z przeglądarką aplikacji nie wróciło w <c>cdpReconnectTimeout</c> (AD-10): BrowserHost raportuje
/// fakt po nieudanym reconnecie. W <c>Starting</c>, <c>Foreground</c> i <c>Background</c> Orchestrator przechodzi
/// wtedy w <c>Failed</c>; poza żywym cyklem fakt jest ignorowany.
/// </summary>
public sealed record CdpLost(string AppId) : Event;

/// <summary>
/// Termin timera zaplanowanego przez Orchestrator (<see cref="ScheduleTimer"/>) minął. Timer jest jednorazowy,
/// a <see cref="TimerId"/> nieznany albo już anulowany jest ignorowany: nie daje komend ani snapshotu.
/// </summary>
public sealed record TimerElapsed(long TimerId) : Event;

/// <summary>
/// Foreground systemu zmienił się na okno <see cref="Hwnd"/>. Orchestrator nie zmienia stanu —
/// pętla zapisuje fakt w logu, a reakcję (dopasowanie trybu, wyciszenie) ustala epic-windows-audio.
/// </summary>
/// <param name="Hwnd">Uchwyt okna; <c>long</c> z tego samego powodu co w <see cref="AppWindowAttached"/>.</param>
public sealed record ForegroundChanged(long Hwnd) : Event;

/// <summary>Wykonawca zakończył komendę <see cref="CmdId"/> sukcesem (AD-4).</summary>
public sealed record CommandCompleted(long CmdId) : Event;

/// <summary>Wykonawca nie wykonał komendy <see cref="CmdId"/>; powód w <see cref="ErrorCode"/> (AD-4).</summary>
public sealed record CommandFailed(long CmdId, ErrorCode ErrorCode) : Event;

/// <summary>
/// Kody błędów wykonawców w <see cref="CommandFailed"/> (AD-4, AD-13, AD-17). O stanie <c>Failed</c> decyduje Orchestrator.
/// </summary>
public enum ErrorCode
{
    /// <summary>Nieoczekiwany wyjątek złapany na granicy portu (AD-4).</summary>
    Unexpected,

    /// <summary><c>cdpPort</c> aplikacji jest zajęty przed startem przeglądarki (AD-13).</summary>
    PortInUse,

    /// <summary>Profil <c>profiles\&lt;appId&gt;</c> jest używany przez inny proces (AD-13).</summary>
    ProfileInUse,

    /// <summary>Proces przeglądarki żyje, ale port CDP nie przyjął połączenia w <c>cdpConnectTimeout</c> (AD-13).</summary>
    CdpUnavailable,

    /// <summary>Nie udało się pobrać paczki aktualizacji (AD-17).</summary>
    UpdateDownloadFailed,

    /// <summary>Suma kontrolna pobranej paczki nie zgadza się z plikiem <c>.sha256</c> (AD-17).</summary>
    UpdateChecksumMismatch,
}
