using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MediaStation.App.Hosts.Browser;

/// <summary>
/// Transport jednej sesji CDP: wysyła i odbiera całe wiadomości tekstowe. <see cref="ReceiveAsync"/> zwraca
/// <c>null</c>, gdy połączenie się zamknęło; żadna metoda nie rzuca z powodu zerwania.
/// </summary>
internal interface ICdpTransport : IDisposable
{
    /// <summary>Adres WebSocket celu, z którym transport się połączył; reconnect wraca dokładnie do niego (story 6.3).</summary>
    Uri Endpoint { get; }

    Task SendAsync(string message, CancellationToken cancellationToken);

    Task<string?> ReceiveAsync();
}

/// <summary>Odpowiedź CDP z polem <c>error</c> albo zamknięcie sesji przed odpowiedzią.</summary>
internal sealed class CdpException(string message) : Exception(message);

/// <summary>
/// Surowy klient CDP (AD-1) niezależny od transportu: rosnące <c>id</c>, mapa oczekujących odpowiedzi, zdarzenia
/// do callbacku i sygnał <see cref="Closed"/>. Nigdy nie wysyła <c>Runtime.enable</c> (wykrywalny ślad CDP): guard
/// rzuca, zanim cokolwiek trafi do transportu. Callback zdarzeń biegnie na pętli odbioru, więc nie może czekać na
/// odpowiedź tej samej sesji.
/// </summary>
internal sealed class CdpSession : IDisposable
{
    /// <summary>Metody CDP zakazane w MediaStation (AD-1).</summary>
    public static readonly IReadOnlySet<string> ForbiddenMethods = new HashSet<string>(StringComparer.Ordinal) { "Runtime.enable" };

    private readonly ICdpTransport _transport;
    private readonly Action<string, JsonElement> _onEvent;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _nextId;
    private int _started;

    public CdpSession(ICdpTransport transport, Action<string, JsonElement> onEvent)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(onEvent);

        _transport = transport;
        _onEvent = onEvent;
    }

    /// <summary>Kończy się, gdy transport się zamknie (albo sesja zostanie zwolniona).</summary>
    public Task Closed => _closed.Task;

    /// <summary>Uruchamia pętlę odbioru; drugie wywołanie nic nie robi.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _ = Task.Run(ReceiveLoopAsync);
        }
    }

    /// <summary>
    /// Wysyła <paramref name="method"/> z <paramref name="parameters"/> i zwraca <c>result</c>. Rzuca
    /// <see cref="InvalidOperationException"/> dla metody zakazanej (przed wysłaniem), <see cref="CdpException"/> dla
    /// odpowiedzi z błędem albo zamkniętej sesji i <see cref="OperationCanceledException"/> po anulowaniu.
    /// </summary>
    public async Task<JsonElement> SendAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (ForbiddenMethods.Contains(method))
        {
            throw new InvalidOperationException($"Metoda CDP {method} jest zakazana (AD-1).");
        }

        if (_closed.Task.IsCompleted)
        {
            throw new CdpException($"{method}: sesja CDP jest zamknięta");
        }

        var id = Interlocked.Increment(ref _nextId);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = reply;
        var message = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new Dictionary<string, object?>(),
        });

        try
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _transport.SendAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException or IOException)
            {
                // Zamknięty albo przerwany socket to zerwana sesja, nie wyjątek przez granicę hosta.
                throw new CdpException($"{method}: wysyłka nie powiodła się ({ex.GetType().Name}: {ex.Message})");
            }
            finally
            {
                _sendGate.Release();
            }

            // Zamknięcie w trakcie wysyłki: pętla odbioru mogła już opróżnić mapę przed wpisaniem tej odpowiedzi.
            if (_closed.Task.IsCompleted)
            {
                reply.TrySetException(new CdpException($"{method}: sesja CDP zamknęła się przed odpowiedzią"));
            }

            return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Zamyka transport; oczekujące odpowiedzi kończą się <see cref="CdpException"/>.</summary>
    public void Dispose()
    {
        _transport.Dispose();
        CloseAndFailPending();
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (await _transport.ReceiveAsync().ConfigureAwait(false) is { } text)
            {
                Dispatch(text);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Transport nie powinien rzucać; gdy jednak rzuci, sesja jest zamknięta jak przy zerwaniu.
        }
        finally
        {
            CloseAndFailPending();
        }
    }

    private void Dispatch(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (root.TryGetProperty("id", out var idElement)
                && idElement.ValueKind == JsonValueKind.Number
                && idElement.TryGetInt64(out var id))
            {
                if (!_pending.TryRemove(id, out var reply))
                {
                    return;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    var detail = error.ValueKind == JsonValueKind.Object
                        && error.TryGetProperty("message", out var m)
                        && m.ValueKind == JsonValueKind.String
                        ? m.GetString()
                        : error.ToString();
                    reply.TrySetException(new CdpException(detail ?? "błąd CDP"));
                }
                else
                {
                    reply.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
                }

                return;
            }

            if (root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String)
            {
                var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : default;
                try
                {
                    _onEvent(method.GetString()!, parameters);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Callback hosta nie może zatrzymać pętli odbioru.
                }
            }
        }
    }

    /// <summary>
    /// Najpierw sygnał zamknięcia, potem opróżnienie mapy: <see cref="SendAsync"/>, który wpisze odpowiedź już po
    /// opróżnieniu, widzi zamkniętą sesję w drugim sprawdzeniu i nie czeka bez końca.
    /// </summary>
    private void CloseAndFailPending()
    {
        _closed.TrySetResult();
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var reply))
            {
                reply.TrySetException(new CdpException("sesja CDP zamknęła się przed odpowiedzią"));
            }
        }
    }
}

/// <summary>
/// Transport CDP na <see cref="ClientWebSocket"/>: bez nagłówka <c>Origin</c>, bez per-message deflate i bez proxy,
/// tylko do adresu z <see cref="CdpTargets.LoopbackWebSocket"/>. Składa wiadomości z wielu ramek.
/// </summary>
internal sealed class WebSocketCdpTransport : ICdpTransport
{
    private readonly ClientWebSocket _socket;

    private WebSocketCdpTransport(ClientWebSocket socket, Uri endpoint)
    {
        _socket = socket;
        Endpoint = endpoint;
    }

    public Uri Endpoint { get; }

    public static async Task<WebSocketCdpTransport> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var socket = new ClientWebSocket();
        try
        {
            // Proxy = null wyłącza proxy systemowe: połączenie idzie wprost na loopback. Deflate zostaje domyślnie wyłączony.
            socket.Options.Proxy = null;
            socket.Options.DangerousDeflateOptions = null;
            await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            return new WebSocketCdpTransport(socket, uri);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReceiveAsync()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            _socket.Abort();
        }
        finally
        {
            _socket.Dispose();
        }
    }
}

/// <summary>
/// Połączenie z CDP przeglądarki: odpytuje <c>http://127.0.0.1:&lt;port&gt;/json/list</c> co 250 ms (bez proxy), aż
/// <see cref="CdpTargets"/> da adres loopback strony i WebSocket się połączy, albo token się anuluje (wtedy <c>null</c>).
/// <see cref="ReconnectAsync"/> to jedna próba powrotu do tego samego celu (story 6.3); przerwy między próbami
/// i termin trzyma <see cref="BrowserHost"/>.
/// </summary>
internal static class CdpConnector
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    public static async Task<ICdpTransport?> ConnectAsync(int cdpPort, CancellationToken cancellationToken)
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = RequestTimeout };
        var listUri = CdpTargets.ListUri(cdpPort);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var json = await http.GetStringAsync(listUri, cancellationToken).ConfigureAwait(false);
                if (CdpTargets.PageWebSocketUrl(json, cdpPort) is { } ws)
                {
                    return await WebSocketCdpTransport.ConnectAsync(ws, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or WebSocketException or TaskCanceledException or IOException)
            {
                // Port jeszcze nie słucha albo cel nie jest gotowy: kolejna próba po przerwie.
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Jedna próba połączenia z dokładnie tym samym celem strony (<paramref name="endpoint"/> utraconego transportu):
    /// bez <c>/json/list</c> i bez wyboru innego celu. Adres jest ponownie sprawdzany przez
    /// <see cref="CdpTargets.LoopbackWebSocket"/>; adres spoza loopbacku portu, błąd połączenia albo anulowanie dają
    /// <c>null</c>, nigdy wyjątek.
    /// </summary>
    public static async Task<ICdpTransport?> ReconnectAsync(Uri endpoint, int cdpPort, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (CdpTargets.LoopbackWebSocket(endpoint.OriginalString, cdpPort) is not { } uri)
        {
            return null;
        }

        try
        {
            return await WebSocketCdpTransport.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or HttpRequestException or OperationCanceledException)
        {
            // Cel zniknął, port nie słucha albo minął termin: próba się nie udała, decyduje pętla hosta.
            return null;
        }
    }
}
