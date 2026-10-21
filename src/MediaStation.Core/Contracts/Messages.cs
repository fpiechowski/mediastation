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
/// Komputer wrócił z uśpienia; adapter power raportuje fakt, a Orchestrator przechodzi na ekran główny
/// z nowym <c>seq</c> fokusu. <see cref="Source"/> niesie nazwę sygnału Windows 1:1 do logu i JSON-a.
/// </summary>
public sealed record SystemResumed(string Source) : Event;

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
