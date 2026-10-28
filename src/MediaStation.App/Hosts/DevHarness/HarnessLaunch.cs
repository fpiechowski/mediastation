using System;
using System.Collections.Generic;
using System.IO;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;

namespace MediaStation.App.Hosts.DevHarness;

/// <summary>Czyste decyzje hosta harnessu zależne od typu wpisu: klasa okna głównego i wybór procesów do adopcji.</summary>
internal static class HarnessLaunch
{
    /// <summary>
    /// Klasa okna głównego: przeglądarka ma <c>Chrome_WidgetWin_1</c>, Moonlight okno strumienia SDL
    /// (<see cref="HarnessMoonlight.StreamWindowClass"/>), a nie okno łączenia Qt, które znika po starcie strumienia.
    /// </summary>
    public static string? WindowClassOf(AppEntry? app) =>
        app is MoonlightApp ? HarnessMoonlight.StreamWindowClass : WindowTreePicker.BrowserWindowClass;

    /// <summary>
    /// Przeglądarka, której launcher kończy się kodem 0, mogła przekazać żądanie działającej instancji profilu:
    /// okna trzeba szukać dalej w innych procesach profilu. Niezerowy kod to awaria; Moonlight nie przekazuje.
    /// </summary>
    public static bool LauncherExitIsHandoff(AppEntry? app, int exitCode) => app is WebApp && exitCode == 0;

    /// <summary>
    /// Czeka na okno aplikacji: najpierw w drzewie własnego launchera (<paramref name="own"/>), a po jego wyjściu z
    /// kodem przekazania (<see cref="LauncherExitIsHandoff"/>) także w pozostałych procesach profilu
    /// (<paramref name="other"/>). Wyjście launchera z innym kodem przerywa czekanie od razu.
    /// <paramref name="exitCode"/> zwraca kod wyjścia launchera albo <c>null</c>, gdy launcher żyje.
    /// </summary>
    public static T? FindWindow<T>(
        AppEntry? app,
        Func<T?> own,
        Func<int?> exitCode,
        Func<T?> other,
        Func<bool> stopping,
        System.Action sleep,
        Func<TimeSpan> elapsed,
        TimeSpan timeout)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(exitCode);
        ArgumentNullException.ThrowIfNull(other);

        return HarnessWait.FindWindow(
            () => own() ?? (exitCode() is { } code && LauncherExitIsHandoff(app, code) ? other() : null),
            () => exitCode() is { } code && !LauncherExitIsHandoff(app, code),
            stopping,
            sleep,
            elapsed,
            timeout);
    }

    /// <summary>Etap, na którym padł start przeglądarki: <c>launcherExited</c>, <c>launcherHandoff</c> albo <c>windowAttach</c>.</summary>
    public static string WebFailedStage(bool launcherExited, int exitCode) =>
        !launcherExited ? "windowAttach" : exitCode == 0 ? "launcherHandoff" : "launcherExited";

    /// <summary>PID-y kandydatów do adopcji: Moonlight po <c>host</c>/<c>app</c>, reszta po profilu <c>profiles\&lt;id&gt;</c>.</summary>
    public static IReadOnlyList<int> MatchingRootsFor(
        AppEntry entry,
        IEnumerable<KeyValuePair<int, string>> commandLines,
        string profilesDir) =>
        entry is MoonlightApp moonlight
            ? HarnessMoonlight.MatchingRoots(commandLines, moonlight.Host, moonlight.App)
            : HarnessBrowser.MatchingRoots(commandLines, Path.Combine(profilesDir, entry.Id));
}
