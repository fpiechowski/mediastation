using System.Collections.Generic;
using System.IO;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;

namespace MediaStation.App.Hosts.DevHarness;

/// <summary>Czyste decyzje hosta harnessu zależne od typu wpisu: klasa okna głównego i wybór procesów do adopcji.</summary>
internal static class HarnessLaunch
{
    /// <summary>Klasa okna głównego: przeglądarka ma <c>Chrome_WidgetWin_1</c>, Moonlight (SDL/Qt) dowolną (<c>null</c>).</summary>
    public static string? WindowClassOf(AppEntry? app) =>
        app is MoonlightApp ? null : WindowTreePicker.BrowserWindowClass;

    /// <summary>PID-y kandydatów do adopcji: Moonlight po <c>host</c>/<c>app</c>, reszta po profilu <c>profiles\&lt;id&gt;</c>.</summary>
    public static IReadOnlyList<int> MatchingRootsFor(
        AppEntry entry,
        IEnumerable<KeyValuePair<int, string>> commandLines,
        string profilesDir) =>
        entry is MoonlightApp moonlight
            ? HarnessMoonlight.MatchingRoots(commandLines, moonlight.Host, moonlight.App)
            : HarnessBrowser.MatchingRoots(commandLines, Path.Combine(profilesDir, entry.Id));
}
