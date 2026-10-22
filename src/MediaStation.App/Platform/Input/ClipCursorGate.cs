using System;
using MediaStation.Core.Input;

namespace MediaStation.App.Platform.Input;

/// <summary>Wynik przeliczenia clipu kursora: brak zmiany, zmiana zastosowana albo porażka do zaraportowania.</summary>
internal enum ClipOutcome
{
    /// <summary>Wiersz routingu bez zmian (ten sam <c>epoch</c> albo clip już zwolniony).</summary>
    Unchanged,

    /// <summary>Clip założony albo zwolniony.</summary>
    Changed,

    /// <summary>Porażka <c>GetWindowRect</c> albo <c>ClipCursor</c>; clip zwolniony, wołający raportuje wpis.</summary>
    Failed,
}

/// <summary>
/// Stan ograniczenia kursora (<c>ClipCursor</c>, AD-8) bez Win32: wylicza zmianę z lokalnej kopii routingu i oddaje ją
/// szwom <c>measure</c> (uchwyt okna → prostokąt albo <c>null</c> w porażce) i <c>apply</c> (prostokąt albo <c>null</c>
/// → powodzenie), więc zwolnienie clipu po zmianie trybu i porażkę pokrywają testy, a <c>InputHub</c> zostaje cienki.
/// Stan ma jednego właściciela: wątek Input, który woła <see cref="Apply"/> z callbacku hooka i z <c>WM_APP</c>.
/// </summary>
internal sealed class ClipCursorGate
{
    private readonly Func<long, ScreenRectangle?> _measure;
    private readonly Func<ScreenRectangle?, bool> _apply;
    private long _epoch = long.MinValue;

    internal ClipCursorGate(Func<long, ScreenRectangle?> measure, Func<ScreenRectangle?, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(apply);

        _measure = measure;
        _apply = apply;
    }

    /// <summary>Aktualny prostokąt clipu albo <c>null</c>, gdy kursor nie jest ograniczony.</summary>
    internal ScreenRectangle? Current { get; private set; }

    /// <summary>
    /// Stosuje wiersz routingu: <c>osk</c> z niezerowym <c>clipHwnd</c> więzi kursor w prostokącie okna
    /// (<see cref="PointerClip.WindowOf"/>), a każdy inny wiersz zwalnia clip, więc zmiana trybu nie zostawia kursora
    /// w prostokącie poprzedniej klawiatury. Ten sam <c>epoch</c> jest no-opem, więc zdarzenia myszy nie mierzą okna
    /// po raz drugi. Porażka pomiaru albo samego clipu zwalnia ograniczenie, żeby kursor nie został uwięziony
    /// w nieaktualnym prostokącie.
    /// </summary>
    internal ClipOutcome Apply(InputRouting routing)
    {
        ArgumentNullException.ThrowIfNull(routing);

        if (routing.Epoch == _epoch)
        {
            return ClipOutcome.Unchanged;
        }

        _epoch = routing.Epoch;
        if (PointerClip.WindowOf(routing) is not { } handle)
        {
            return Release();
        }

        if (_measure(handle) is not { } rectangle)
        {
            return FailAfterRelease();
        }

        if (!_apply(rectangle))
        {
            return FailAfterRelease();
        }

        Current = rectangle;
        return ClipOutcome.Changed;
    }

    /// <summary>Zwalnia clip, gdy był założony; wiersz bez clipu nie woła <c>ClipCursor(null)</c> po raz drugi.</summary>
    internal ClipOutcome Release()
    {
        if (Current is null)
        {
            return ClipOutcome.Unchanged;
        }

        Current = null;
        return _apply(null) ? ClipOutcome.Changed : ClipOutcome.Failed;
    }

    /// <summary>
    /// Zwolnienie przy zamykaniu aplikacji: woła <c>ClipCursor(null)</c> bezwarunkowo, bo cache mógł się rozjechać
    /// z rzeczywistym clipem; <c>false</c> znaczy, że wołający ma zaraportować porażkę.
    /// </summary>
    internal bool ForceRelease()
    {
        Current = null;
        return _apply(null);
    }

    private ClipOutcome FailAfterRelease()
    {
        Current = null;

        // Zwolnienie jest częścią porażki: nieudany clip i tak nie może trzymać kursora.
        _apply(null);
        return ClipOutcome.Failed;
    }
}
