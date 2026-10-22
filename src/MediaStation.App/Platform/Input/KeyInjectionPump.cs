using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Adapter portu wstrzyknięć (AD-3, AD-4, AD-8, AD-14): <see cref="Execute"/> tylko kolejkuje komendę, a właściwe
/// <c>SendInput</c> idzie na własnym wątku <c>key-injection</c>, więc wątek pętli Orchestratora nigdy nie woła Win32
/// i Home nie czeka na wstrzyknięcie. Wzór <see cref="ActionPump"/>: kolejka bez ograniczenia rozmiaru nie blokuje
/// producenta, a kolejność wstrzyknięć odpowiada kolejności komend. Pompa sprawdza zakres <c>Vk</c> (1–255) i loguje
/// wyłącznie <c>{cmdId}</c> — bez kodów klawiszy (AD-13). Wyjątek delegata, także przyszły wariant Unicode/Alt,
/// ląduje jako <c>warn keyInjectionFailed {cmdId}</c> i nie przechodzi przez granicę portu (AD-4).
/// <see cref="Dispose"/> domyka kolejkę i porzuca niewstrzyknięte komendy: wstrzyknięcie po zamknięciu nie ma sensu.
/// </summary>
internal sealed class KeyInjectionPump : IKeyInjectionPort, IDisposable
{
    /// <summary>Źródło wpisów logu wstrzyknięć; ta sama domena co routing wejścia.</summary>
    public const string Src = "input";

    /// <summary>Górna granica czekania na wątek w <see cref="Dispose"/>; zawieszone <c>SendInput</c> nie może zamrozić wyjścia.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Zakres kodów wirtualnych klawiszy, które system przyjmuje dla klawiatury.</summary>
    private const int MinVirtualKey = 1;
    private const int MaxVirtualKey = 255;

    private readonly Func<int, bool> _sendKey;
    private readonly ILog _log;
    private readonly BlockingCollection<SendKey> _queue = new();
    private readonly Thread _thread;
    private bool _started;
    private bool _disposed;
    private volatile bool _stopping;

    /// <param name="sendKey">Wstrzyknięcie jednego naciśnięcia; <c>false</c>, gdy system nic nie wstawił.</param>
    /// <param name="log">Log aplikacji.</param>
    public KeyInjectionPump(Func<int, bool> sendKey, ILog log)
    {
        ArgumentNullException.ThrowIfNull(sendKey);
        ArgumentNullException.ThrowIfNull(log);

        _sendKey = sendKey;
        _log = log;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "key-injection",
        };
    }

    /// <summary>Startuje wątek wstrzyknięć; kolejne wywołanie jest błędem wołającego.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Pompa wstrzyknięć została już wystartowana.");
        }

        _thread.Start();
        _started = true;
    }

    /// <summary>
    /// Przyjmuje komendę do kolejki bez czekania na Win32. Wywołanie po <see cref="Dispose"/> jest no-opem,
    /// a wyścig z domykaniem kolejki nie przepuszcza wyjątku przez granicę portu (AD-4).
    /// </summary>
    public void Execute(SendKey command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_stopping)
        {
            return;
        }

        try
        {
            _queue.TryAdd(command);
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding wyprzedził to wywołanie: komenda po zamknięciu nie ma dokąd trafić.
        }
    }

    /// <summary>
    /// Domyka kolejkę i czeka ograniczony czas na wątek; komendy, których wątek nie zdążył wstrzyknąć, są porzucane.
    /// Kolejne wywołanie jest no-opem.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;

        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // Kolejka już domknięta: wątek i tak kończy pętlę po CompleteAdding.
        }

        // Kolejka i wątek nie są zwalniane: spóźniony Execute po Dispose musi być nieszkodliwy, a wynik czekania
        // nie zmienia zachowania — niewstrzyknięte komendy są porzucane świadomie.
        if (_started)
        {
            _thread.Join(ShutdownTimeout);
        }
    }

    private void Run()
    {
        foreach (var command in _queue.GetConsumingEnumerable())
        {
            // Wyścig Dispose → CompleteAdding może zostawić komendę w kolejce; wątek kończy się bez wstrzykiwania.
            if (_stopping)
            {
                return;
            }

            Inject(command);
        }
    }

    private void Inject(SendKey command)
    {
        if (command.Vk is < MinVirtualKey or > MaxVirtualKey)
        {
            _log.Warn(Src, "keyInjectionFailed", CmdId(command));
            return;
        }

        try
        {
            if (_sendKey(command.Vk))
            {
                _log.Info(Src, "keyInjection", CmdId(command));
                return;
            }

            _log.Warn(Src, "keyInjectionFailed", CmdId(command));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Port nie przepuszcza wyjątku (AD-4): porażka jednego wstrzyknięcia nie może zgubić kolejnych ani wątku.
            _log.Warn(Src, "keyInjectionFailed", CmdId(command));
        }
    }

    /// <summary>Wpis logu bez VK (AD-13): sam identyfikator komendy.</summary>
    private static Dictionary<string, object?> CmdId(SendKey command) =>
        new() { ["cmdId"] = command.CmdId };
}
