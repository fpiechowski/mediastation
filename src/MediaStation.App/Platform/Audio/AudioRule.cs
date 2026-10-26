using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaStation.App.Platform.Audio;

/// <summary>
/// Reguła AudioRoutera: słownik <c>rootPid → muted</c> (AD-15). <c>true</c> znaczy, że sesje drzewa korzenia mają
/// być wyciszone (także nowe), <c>false</c> — że sesje wyciszone przez nas wracają do stanu sprzed wyciszenia.
/// Czysta logika bez COM: dopasowanie sesji do korzenia idzie przez <see cref="ProcessTreeMatcher"/>.
/// </summary>
internal sealed class AudioRule
{
    private readonly Dictionary<int, bool> _roots = [];

    /// <summary>Czy jakikolwiek korzeń ma regułę „wycisz”.</summary>
    public bool AnyMuted => _roots.ContainsValue(true);

    /// <summary>Korzenie z regułą.</summary>
    public IReadOnlyCollection<int> Roots => _roots.Keys;

    /// <summary>Ustawia regułę korzenia; powtórzenie zastępuje poprzednią wartość.</summary>
    public void Set(int rootPid, bool muted) => _roots[rootPid] = muted;

    /// <summary>Usuwa regułę korzenia; zwraca <c>true</c>, gdy korzeń ją miał.</summary>
    public bool Remove(int rootPid) => _roots.Remove(rootPid);

    /// <summary>Stan reguły korzenia albo <c>null</c>, gdy korzeń nie ma reguły.</summary>
    public bool? IsMuted(int rootPid) => _roots.TryGetValue(rootPid, out var muted) ? muted : null;

    /// <summary>
    /// Korzeń rządzący procesem <paramref name="pid"/> (najbliższy w łańcuchu rodziców) albo <c>null</c>,
    /// gdy żaden korzeń reguły go nie obejmuje.
    /// </summary>
    public int? Governing(int pid, IReadOnlyDictionary<int, int> parents) =>
        ProcessTreeMatcher.FindRoot(pid, _roots.Keys, parents);

    /// <summary>
    /// Usuwa korzenie, których procesu nie ma już w migawce (PID mógłby zostać użyty ponownie przez obcy proces),
    /// i zwraca je. Pusta migawka oznacza porażkę odczytu, więc nic nie usuwa.
    /// </summary>
    public IReadOnlyList<int> PruneDead(IReadOnlyDictionary<int, int> parents)
    {
        ArgumentNullException.ThrowIfNull(parents);

        if (parents.Count == 0)
        {
            return [];
        }

        var dead = _roots.Keys.Where(root => !parents.ContainsKey(root)).ToArray();
        foreach (var root in dead)
        {
            _roots.Remove(root);
        }

        return dead;
    }
}
