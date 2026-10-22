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

/// <summary>Kafelek aplikacji w snapshocie; <see cref="AppId"/> jest stabilnym kluczem fokusu UI (AD-16).</summary>
public sealed record AppSnapshot(string AppId, AppCategory Category, AppLifecycleState State);

/// <summary>
/// Żądanie fokusu dla UI (AD-16). <see cref="Target"/> to <see cref="DefaultTarget"/> albo <c>appId</c>;
/// UI stosuje każde nowe <see cref="Seq"/> dokładnie raz.
/// </summary>
public sealed record FocusRequest(string Target, long Seq)
{
    /// <summary>Zakładka Video, pierwszy kafelek (zimny start, restart, wybudzenie).</summary>
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
