using System;
using System.Collections.Generic;

namespace MediaStation.App.Platform.Audio;

/// <summary>
/// Czyste dopasowanie procesu do drzewa od <c>rootPid</c> po łańcuchu rodziców z migawki procesów (AD-15).
/// Bez typów COM i Win32: migawkę (<c>pid → ppid</c>) dostarcza wołający.
/// </summary>
internal static class ProcessTreeMatcher
{
    /// <summary>Górna granica długości łańcucha rodziców; chroni przed cyklem w migawce (reuse PID-ów).</summary>
    public const int MaxDepth = 64;

    /// <summary>Migawka <c>pid → ppid</c>; powtórzony <c>pid</c> wygrywa ostatnim wpisem.</summary>
    public static IReadOnlyDictionary<int, int> ToParentMap(IEnumerable<KeyValuePair<int, int>> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        var map = new Dictionary<int, int>();
        foreach (var (pid, parent) in pairs)
        {
            map[pid] = parent;
        }

        return map;
    }

    /// <summary>
    /// Liczba kroków od <paramref name="pid"/> do <paramref name="rootPid"/> w górę łańcucha rodziców
    /// (0 dla samego korzenia) albo <c>null</c>, gdy proces nie należy do drzewa.
    /// </summary>
    public static int? DistanceToRoot(int pid, int rootPid, IReadOnlyDictionary<int, int> parents)
    {
        ArgumentNullException.ThrowIfNull(parents);

        if (rootPid <= 0 || pid <= 0)
        {
            return null;
        }

        var current = pid;
        for (var depth = 0; depth <= MaxDepth; depth++)
        {
            if (current == rootPid)
            {
                return depth;
            }

            if (!parents.TryGetValue(current, out var parent) || parent <= 0 || parent == current)
            {
                return null;
            }

            current = parent;
        }

        return null;
    }

    /// <summary>Czy <paramref name="pid"/> jest korzeniem albo jego potomkiem.</summary>
    public static bool IsInTree(int pid, int rootPid, IReadOnlyDictionary<int, int> parents) =>
        DistanceToRoot(pid, rootPid, parents) is not null;

    /// <summary>
    /// Korzeń z <paramref name="roots"/>, do którego drzewa należy <paramref name="pid"/>; przy zagnieżdżonych
    /// korzeniach wygrywa najbliższy. <c>null</c>, gdy proces nie należy do żadnego drzewa.
    /// </summary>
    public static int? FindRoot(int pid, IEnumerable<int> roots, IReadOnlyDictionary<int, int> parents)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(parents);

        int? best = null;
        var bestDistance = int.MaxValue;
        foreach (var root in roots)
        {
            if (DistanceToRoot(pid, root, parents) is { } distance && distance < bestDistance)
            {
                best = root;
                bestDistance = distance;
            }
        }

        return best;
    }
}
