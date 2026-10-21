using System.IO;
using System.Threading;
using MediaStation.Core.Logging;

namespace MediaStation.App;

/// <summary>
/// Named mutex jednej instancji (AD-2): trzymany przez cały czas życia procesu w każdym trybie, także smoke i kalibracji.
/// Mutex jest obiektem OS, więc żyje wyłącznie w App (AD-3); Core go nie zna.
/// </summary>
public static class SingleInstance
{
    private const string Src = "app";

    private const string MutexName = @"Local\MediaStation.SingleInstance";

    private static Mutex? _mutex;

    /// <summary>
    /// Próbuje przejąć mutex bez czekania; <c>false</c> = inna instancja go trzyma albo mutex jest niedostępny
    /// (brak uprawnień, nazwa nie do otwarcia, błąd I/O). Porzucony mutex (poprzedni właściciel padł z otwartym
    /// uchwytem) jest przejmowany i logowany jako <c>mutexAbandoned</c>.
    /// </summary>
    public static bool TryAcquire(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, MutexName);
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            log.Warn(Src, "mutexAbandoned");
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            mutex?.Dispose();
            log.Error(Src, "mutexUnavailable", ex);
            return false;
        }

        // Trzymany do końca procesu; OS zwalnia mutex, więc nie ma ręcznego ReleaseMutex.
        _mutex = mutex;
        return true;
    }

    private static bool IsUnavailable(Exception ex) =>
        ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException;
}
