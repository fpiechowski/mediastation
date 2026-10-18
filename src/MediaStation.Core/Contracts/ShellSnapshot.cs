using System.Collections.Generic;

namespace MediaStation.Core.Contracts;

/// <summary>Ekran, który shell ma pokazać.</summary>
public enum ShellScreen
{
    Placeholder,
    ConfigError,
}

/// <summary>
/// Pełny snapshot widoku wysyłany z Core do Shell (AD-16). UI odrzuca snapshot ze starszym <see cref="Rev"/>.
/// </summary>
public sealed record ShellSnapshot(long Rev, ShellScreen Screen, IReadOnlyList<ConfigError> ConfigErrors);
