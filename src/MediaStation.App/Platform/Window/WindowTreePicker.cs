using System;
using System.Collections.Generic;

namespace MediaStation.App.Platform.Window;

/// <summary>
/// Wybór głównego okna przeglądarki z drzewa procesów harnessu: tylko okno widoczne, bez właściciela, klasy
/// <c>Chrome_WidgetWin_1</c> i z procesu drzewa. Okna pomocnicze (z właścicielem, ukryte) nie są oknem aplikacji.
/// </summary>
internal static class WindowTreePicker
{
    /// <summary>Klasa okna głównego Chromium i Edge.</summary>
    public const string BrowserWindowClass = "Chrome_WidgetWin_1";

    /// <summary>Zwraca pierwsze pasujące okno albo <c>null</c>, gdy drzewo nie ma jeszcze okna głównego.</summary>
    public static WindowInfo? Pick(IEnumerable<WindowInfo> candidates, ISet<int> treePids)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(treePids);

        foreach (var window in candidates)
        {
            if (window.Visible
                && !window.HasOwner
                && string.Equals(window.ClassName, BrowserWindowClass, StringComparison.Ordinal)
                && treePids.Contains(window.Pid))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>Korzeń i wszyscy potomkowie z par <c>(pid, ppid)</c>; odporny na cykle (ponowne użycie PID).</summary>
    public static HashSet<int> Descendants(int rootPid, IEnumerable<KeyValuePair<int, int>> parentOf)
    {
        ArgumentNullException.ThrowIfNull(parentOf);

        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, parent) in parentOf)
        {
            if (!children.TryGetValue(parent, out var list))
            {
                list = [];
                children[parent] = list;
            }

            list.Add(pid);
        }

        var tree = new HashSet<int> { rootPid };
        var pending = new Queue<int>();
        pending.Enqueue(rootPid);
        while (pending.Count > 0)
        {
            if (!children.TryGetValue(pending.Dequeue(), out var kids))
            {
                continue;
            }

            foreach (var kid in kids)
            {
                if (tree.Add(kid))
                {
                    pending.Enqueue(kid);
                }
            }
        }

        return tree;
    }
}
