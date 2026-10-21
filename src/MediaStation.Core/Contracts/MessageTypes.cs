using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MediaStation.Core.Contracts;

/// <summary>
/// Rejestr typów komunikatów przesyłanych w kopercie <c>{ v, type, payload }</c> (AD-16).
/// <c>type</c> to camelCase nazwy rekordu. Kolejne epiki dopisują tu swoje Intents, Commands i Events.
/// </summary>
public static class MessageTypes
{
    /// <summary>Wszystkie konkretne rekordy komunikatów.</summary>
    public static IReadOnlyList<Type> All { get; } =
    [
        typeof(Action),
        typeof(LaunchApp),
        typeof(CloseApp),
        typeof(RestartApp),
        typeof(SetInputRouting),
        typeof(StartApp),
        typeof(StopApp),
        typeof(ActivateWindow),
        typeof(MinimizeWindow),
        typeof(ShowShell),
        typeof(CommandCompleted),
        typeof(CommandFailed),
        typeof(OrchestratorStarted),
        typeof(SystemResumed),
        typeof(AppWindowAttached),
        typeof(ProcessExited),
        typeof(CdpLost),
        typeof(ForegroundChanged),
    ];

    private static readonly Dictionary<string, Type> ByName = BuildIndex(All);

    private static readonly Dictionary<Type, string> NamesByType = Invert(ByName);

    /// <summary>Nazwa <c>type</c> w kopercie dla zarejestrowanego rekordu.</summary>
    /// <exception cref="ArgumentException">Typ nie jest w rejestrze.</exception>
    public static string NameOf(Type type) =>
        NamesByType.TryGetValue(type, out var name)
            ? name
            : throw new ArgumentException($"Typ {type.FullName} nie jest zarejestrowany w {nameof(MessageTypes)}.", nameof(type));

    /// <summary>Rekord dla nazwy <c>type</c> z koperty.</summary>
    public static bool TryGet(string name, out Type type) => ByName.TryGetValue(name, out type!);

    internal static Dictionary<string, Type> BuildIndex(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var index = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(type.Name);
            if (name is Envelope.SnapshotType or Envelope.ReadyType)
            {
                throw new InvalidOperationException(
                    $"Nazwa '{name}' jest zarezerwowana dla koperty mostu i nie może być w {nameof(MessageTypes)}.");
            }

            // Add rzuca przy duplikacie: dwie takie same nazwy w kopercie to błąd rejestru.
            index.Add(name, type);
        }

        return index;
    }

    private static Dictionary<Type, string> Invert(Dictionary<string, Type> byName)
    {
        var inverted = new Dictionary<Type, string>();
        foreach (var (name, type) in byName)
        {
            inverted.Add(type, name);
        }

        return inverted;
    }
}
