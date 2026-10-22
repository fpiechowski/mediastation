using System;
using System.Runtime.ExceptionServices;

namespace MediaStation.App;

/// <summary>
/// Kolejność zwalniania adapterów przy wyjściu z aplikacji (DW-5): hub wejścia, pompa Actions, pompa wstrzyknięć,
/// adapter power, scheduler timerów i pętla Orchestratora. Composition root zna tę kolejność, ale typ jest wolny od
/// WPF/Win32, więc testy Core linkują go tak jak <c>UiHang.cs</c> i <c>ClipCursorGate.cs</c>.
/// </summary>
/// <remarks>
/// Każdy adapter jest zwalniany osobno: awaria jednego nie zostawia kolejnych niezamkniętych, a na zewnątrz leci
/// <b>pierwszy</b> wyjątek (macierz Intent). Dawna siatka <c>try/finally</c> przy dwóch awariach przepuszczała
/// ostatni wyjątek, bo kolejne <c>finally</c> zastępowały wcześniejszy — różnica obejmuje wyłącznie wyjście
/// z wieloma awariami i jest pinowana testem <c>Dispose_keeps_releasing_and_rethrows_the_first_exception</c>.
/// </remarks>
internal sealed class TeardownSequence : IDisposable
{
    private readonly IDisposable? _inputHub;
    private readonly IDisposable? _actionPump;
    private readonly IDisposable _keyInjection;
    private readonly IDisposable? _notifications;
    private readonly IDisposable? _reporter;
    private readonly IDisposable _timers;
    private readonly IDisposable _loop;

    /// <summary>
    /// Sloty opcjonalne (<c>null</c>) odpowiadają adapterom, których start padł albo jeszcze się nie odbył; pola
    /// wymagane to te, które composition root tworzy przed rejestracją handlera wyjścia.
    /// </summary>
    internal TeardownSequence(
        IDisposable? inputHub,
        IDisposable? actionPump,
        IDisposable keyInjection,
        IDisposable? notifications,
        IDisposable? reporter,
        IDisposable timers,
        IDisposable loop)
    {
        ArgumentNullException.ThrowIfNull(keyInjection);
        ArgumentNullException.ThrowIfNull(timers);
        ArgumentNullException.ThrowIfNull(loop);

        _inputHub = inputHub;
        _actionPump = actionPump;
        _keyInjection = keyInjection;
        _notifications = notifications;
        _reporter = reporter;
        _timers = timers;
        _loop = loop;
    }

    /// <summary>
    /// Zwalnia adaptery w kolejności teardownu i rzuca pierwszy wyjątek po zwolnieniu reszty; brak wyjątku kończy
    /// wyjście normalnie.
    /// </summary>
    public void Dispose()
    {
        ExceptionDispatchInfo? first = null;

        // Handler `app.Exit` powstaje przed startem adapterów, więc sekwencję buduje się w chwili wyjścia,
        // a nie przy rejestracji: część slotów jest wtedy jeszcze `null`.
        Release(_inputHub, ref first);
        Release(_actionPump, ref first);
        Release(_keyInjection, ref first);
        Release(_notifications, ref first);
        Release(_reporter, ref first);
        Release(_timers, ref first);
        Release(_loop, ref first);

        first?.Throw();
    }

    private static void Release(IDisposable? adapter, ref ExceptionDispatchInfo? first)
    {
        if (adapter is null)
        {
            return;
        }

        try
        {
            adapter.Dispose();
        }
        catch (Exception ex)
        {
            // Ten sam obiekt i ślad stosu wyjątku lecą dalej z `Dispose`, po zwolnieniu pozostałych adapterów.
            first ??= ExceptionDispatchInfo.Capture(ex);
        }
    }
}
