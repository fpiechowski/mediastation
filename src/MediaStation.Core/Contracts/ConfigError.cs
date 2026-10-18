namespace MediaStation.Core.Contracts;

/// <summary>
/// Jeden błąd konfiguracji pokazywany na ekranie błędu: plik, ścieżka JSON (albo <c>null</c>) i powód.
/// </summary>
public sealed record ConfigError(string File, string? Path, string Reason);
