using System;

namespace MediaStation.App.Hosts.DevHarness;

/// <summary>Pętla oczekiwania harnessu na okno aplikacji z wstrzykiwanymi zależnościami (czas, uśpienie, stan procesu).</summary>
internal static class HarnessWait
{
    /// <summary>
    /// Odpytuje <paramref name="find"/> aż do znalezienia okna; zwraca <c>null</c> po <paramref name="timeout"/>,
    /// po zakończeniu procesu albo po zatrzymaniu sesji. <paramref name="elapsed"/> zwraca czas od startu oczekiwania.
    /// </summary>
    public static T? FindWindow<T>(
        Func<T?> find,
        Func<bool> hasExited,
        Func<bool> stopping,
        System.Action sleep,
        Func<TimeSpan> elapsed,
        TimeSpan timeout)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(hasExited);
        ArgumentNullException.ThrowIfNull(stopping);
        ArgumentNullException.ThrowIfNull(sleep);
        ArgumentNullException.ThrowIfNull(elapsed);

        while (elapsed() < timeout && !stopping() && !hasExited())
        {
            if (find() is { } window)
            {
                return window;
            }

            sleep();
        }

        return null;
    }
}
