using System;
using System.Threading;
using MediaStation.Core.Input;
using MediaStation.Core.Ports;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Wątek opróżniający kolejkę Actions z wątku Input (AD-2, AD-8): callback hooka nigdy nie czeka, a Action
/// dociera do <see cref="IActionSink"/> poza wątkiem Input. <see cref="ActionQueue"/> powstała w konstruktorze
/// z budzikiem <see cref="AutoResetEvent"/>. Wyjątek sinka nie zabija wątku — ląduje jako
/// <c>inputHubError {operation: actionSink}</c>, a kolejna Action dochodzi.
/// <see cref="Dispose"/> jest wołany z jednego wątku i musi przyjść po zatrzymaniu producenta (InputHub);
/// budzik nie jest zwalniany, więc spóźniony zapis do kolejki jest nieszkodliwy.
/// </summary>
internal sealed class ActionPump : IDisposable
{
    /// <summary>Górna granica czekania na wątek w <see cref="Dispose"/>; zawieszony sink nie może zamrozić wyjścia.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IActionSink _sink;
    private readonly IInputHubErrorSink _errors;
    private readonly AutoResetEvent _wake = new(initialState: false);
    private readonly Thread _thread;
    private bool _started;
    private bool _disposed;
    private volatile bool _stopping;

    public ActionPump(IActionSink sink, IInputHubErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(errors);

        _sink = sink;
        _errors = errors;

        // Budzik nie może blokować ani rzucać: Enqueue woła go z callbacku hooka.
        Queue = new ActionQueue(() => _wake.Set());
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "action-pump",
        };
    }

    /// <summary>Kolejka zasilana z wątku Input i opróżniana przez wątek <c>action-pump</c>.</summary>
    public ActionQueue Queue { get; }

    /// <summary>Startuje wątek opróżniający; kolejne wywołanie jest błędem wołającego.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Pompa Actions została już wystartowana.");
        }

        _thread.Start();
        _started = true;
    }

    /// <summary>
    /// Zatrzymuje wątek, budzi go, czeka na zakończenie i domyka Actions, które zostały w kolejce.
    /// Wołane z jednego wątku po zatrzymaniu producenta; kolejne wywołanie jest no-opem. Czekanie jest
    /// ograniczone, a jego przekroczenie zostawia <c>inputHubError {operation: actionSink}</c> zamiast
    /// zamrożonego wyjścia; wtedy reszta kolejki zostaje, bo żywy wątek mógłby ją jeszcze czytać.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;
        _wake.Set();

        if (_started && !_thread.Join(ShutdownTimeout))
        {
            ReportError();
            return;
        }

        Drain();

        // Budzik zostaje żywy: spóźniony Enqueue po Dispose musi być nieszkodliwy, a nie rzucić ObjectDisposedException.
    }

    private void Run()
    {
        while (!_stopping)
        {
            if (!Queue.TryDequeue(out var action))
            {
                _wake.WaitOne();
                continue;
            }

            Deliver(action);
        }
    }

    /// <summary>Domyka kolejkę po zatrzymaniu wątku: żadna Action nie ginie przy zamknięciu aplikacji.</summary>
    private void Drain()
    {
        while (Queue.TryDequeue(out var action))
        {
            Deliver(action);
        }
    }

    private void Deliver(ActionMessage action)
    {
        try
        {
            _sink.Deliver(action);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Port nie przepuszcza wyjątku (AD-4): jeden zły sink nie może zgubić kolejnych Actions ani wątku.
            ReportError();
        }
    }

    /// <summary>Raport porażki sinka; wyjątek kanału błędów nie może zabić wątku pompy.</summary>
    private void ReportError()
    {
        try
        {
            _errors.Report(new InputHubError(InputHubOperations.ActionSink, null));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Kanał błędów nie jest częścią dostarczania Actions: jego awaria nie może przerwać pompy.
        }
    }
}
