using System.Collections.Generic;

namespace MediaStation.Core.Contracts;

/// <summary>Ekran, który shell ma pokazać.</summary>
public enum ShellScreen
{
    Placeholder,
    ConfigError,
}

/// <summary>Stan cyklu życia aplikacji widziany przez Shell (AD-10).</summary>
public enum AppLifecycleState
{
    NotRunning,
    Starting,
    Foreground,
    Background,
    Stopping,
    Failed,
}

/// <summary>
/// Kafelek aplikacji w snapshocie; <see cref="AppId"/> jest stabilnym kluczem fokusu UI (AD-16),
/// a <see cref="Title"/> to podpis kafelka z <c>apps.json</c>. <see cref="Icon"/> to sama nazwa pliku w <c>config/icons/</c>
/// albo <c>null</c>, gdy App nie znalazł pliku (kafelek pokazuje wtedy sam tytuł); <see cref="Type"/> wybiera wariant
/// chipa i linii stanu (np. „Wstrzymane” tylko dla <c>web</c>).
/// </summary>
public sealed record AppSnapshot(
    string AppId,
    AppCategory Category,
    AppLifecycleState State,
    string Title,
    string? Icon,
    AppType Type);

/// <summary>
/// Żądanie fokusu dla UI (AD-16). <see cref="Target"/> to <see cref="DefaultTarget"/> albo <c>appId</c>;
/// UI stosuje każde nowe <see cref="Seq"/> dokładnie raz.
/// </summary>
public sealed record FocusRequest(string Target, long Seq)
{
    /// <summary>Zakładka Video, pierwszy kafelek, a bez kafelków Video pierwszy kafelek pierwszej niepustej zakładki (zimny start, restart, wybudzenie).</summary>
    public const string DefaultTarget = "default";
}

/// <summary>
/// Pełny snapshot widoku wysyłany z Core do Shell (AD-16). UI odrzuca snapshot ze starszym <see cref="Rev"/>.
/// Kolejność pól jest kontraktem JSON i schematu.
/// </summary>
public sealed record ShellSnapshot(
    long Rev,
    ShellScreen Screen,
    IReadOnlyList<ConfigError> ConfigErrors,
    IReadOnlyList<AppSnapshot> Apps,
    FocusRequest? FocusRequest);
