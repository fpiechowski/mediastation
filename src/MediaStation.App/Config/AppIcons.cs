using System.IO;
using MediaStation.Core.Config;

namespace MediaStation.App.Config;

/// <summary>
/// Sprawdza pliki ikon z <c>apps.json</c> w <c>config/icons/</c>. Core nie dotyka dysku (AD-3), więc App podaje
/// wynik jako zbiór <c>appId</c> bez pliku; test istnienia jest wstrzykiwany.
/// </summary>
public static class AppIcons
{
    /// <summary>Wpisy, dla których <paramref name="exists"/> nie widzi pliku <c>iconsDir/icon</c>, w kolejności configu.</summary>
    public static IReadOnlyList<AppEntry> FindMissing(IEnumerable<AppEntry> apps, string iconsDir, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(iconsDir);
        ArgumentNullException.ThrowIfNull(exists);

        var missing = new List<AppEntry>();
        foreach (var app in apps)
        {
            if (!exists(Path.Combine(iconsDir, app.Icon)))
            {
                missing.Add(app);
            }
        }

        return missing;
    }
}
