using System;
using System.Collections.Generic;
using System.Threading;
using MediaStation.Core.Logging;

namespace MediaStation.App.Diagnostics;

/// <summary>
/// Zawieszenie wątku UI na żądanie (S10): wpis <c>debugHangUi</c> i blokada wykonane w callbacku wysłanym na dispatcher
/// z priorytetem niższym niż render, więc okno istnieje i jest widoczne, a InputHub z własnego wątku dalej loguje
/// <c>action</c>. Bez WPF i Win32, więc testy linkują ten plik jak <c>ClipCursorGate</c>.
/// </summary>
internal static class UiHang
{
    /// <summary>Źródło wpisu, ten sam co pozostałe wpisy App.</summary>
    internal const string Src = "app";

    /// <summary>
    /// Jedno <paramref name="post"/> z callbackiem, który loguje <c>warn debugHangUi {seconds}</c> i blokuje wołający
    /// wątek. Niedodatni czas to brak zawieszenia: ani wpisu, ani callbacku.
    /// </summary>
    internal static void Schedule(TimeSpan hang, ILog log, System.Action<System.Action> post)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(post);

        if (hang <= TimeSpan.Zero)
        {
            return;
        }

        post(() =>
        {
            log.Warn(Src, "debugHangUi", new Dictionary<string, object?>
            {
                ["seconds"] = hang.TotalSeconds,
            });

            // Blokada wątku UI, a nie procesu: InputHub i pompa Actions żyją na własnych wątkach.
            Thread.Sleep(hang);
        });
    }
}
