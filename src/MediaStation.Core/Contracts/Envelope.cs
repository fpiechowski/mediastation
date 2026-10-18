namespace MediaStation.Core.Contracts;

/// <summary>
/// Koperta mostu Shell ↔ host: <c>{ v, type, payload }</c> (AD-16). <c>type</c> w camelCase.
/// </summary>
public sealed record Envelope<TPayload>(int V, string Type, TPayload Payload);

/// <summary>Stałe i fabryki koperty mostu.</summary>
public static class Envelope
{
    /// <summary>Major wersja protokołu mostu.</summary>
    public const int Version = 1;

    /// <summary>host → UI: pełny snapshot.</summary>
    public const string SnapshotType = "snapshot";

    /// <summary>UI → host: UI gotowy na snapshot.</summary>
    public const string ReadyType = "ready";

    public static Envelope<ShellSnapshot> Snapshot(ShellSnapshot snapshot) => new(Version, SnapshotType, snapshot);
}
