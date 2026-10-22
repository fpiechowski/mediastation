using System;
using System.Collections.Generic;

namespace MediaStation.App.Platform.System;

/// <summary>
/// Księgowość podłączeń shimu power: każdy udany attach pamięta swoje zwolnienie, a podłączenie, które
/// rzuci, zdejmuje wszystko dotąd podłączone w odwrotnej kolejności (DW-2). Nieudana konstrukcja nigdy nie
/// wraca do <c>CompositionRoot</c>, więc bez tego hook HWND zostawałby do końca procesu. Bez Win32/WPF,
/// więc plik jest linkowany do testów; <c>global::System.Action</c> jest kwalifikowany, bo projekt testów
/// aliasuje `Action` na rekord kontraktu, a `System` przesłania segment przestrzeni nazw tego pliku.
/// </summary>
internal sealed class PowerSourcesAttachment : IDisposable
{
    private readonly List<global::System.Action> _releases = [];
    private bool _released;

    /// <summary>Liczba aktywnych podłączeń; po zwolnieniu 0.</summary>
    public int Count => _releases.Count;

    /// <summary>
    /// Wykonuje <paramref name="attach"/> i zapamiętuje <paramref name="release"/>. Gdy attach rzuci,
    /// zwalnia dotąd podłączone źródła i przepuszcza wyjątek dalej — krok, który padł, nie zostawia wpisu.
    /// </summary>
    public void Attach(global::System.Action attach, global::System.Action release)
    {
        ArgumentNullException.ThrowIfNull(attach);
        ArgumentNullException.ThrowIfNull(release);

        try
        {
            attach();
        }
        catch
        {
            Dispose();
            throw;
        }

        _releases.Add(release);
    }

    /// <summary>Zwalnia źródła w odwrotnej kolejności podłączenia; idempotentne.</summary>
    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;

        for (var i = _releases.Count - 1; i >= 0; i--)
        {
            _releases[i]();
        }

        _releases.Clear();
    }
}
