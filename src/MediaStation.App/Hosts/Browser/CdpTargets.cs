using System;
using System.Text.Json;

namespace MediaStation.App.Hosts.Browser;

/// <summary>
/// Czysty wybór celu CDP z odpowiedzi <c>GET /json/list</c> (AD-1, AD-13): pierwszy cel <c>type: "page"</c>, który
/// nie jest <c>devtools://</c>. Adres WebSocket musi wskazywać <c>127.0.0.1</c> i ten sam port, inaczej cel jest
/// odrzucany (traktowany jak jeszcze niedostępny), więc host nigdy nie łączy się poza loopback.
/// </summary>
internal static class CdpTargets
{
    /// <summary>Host loopbacku, jedyny dozwolony dla CDP.</summary>
    public const string LoopbackHost = "127.0.0.1";

    /// <summary>Adres listy celów CDP dla portu aplikacji.</summary>
    public static Uri ListUri(int cdpPort) => new($"http://{LoopbackHost}:{cdpPort}/json/list");

    /// <summary>Adres WebSocket celu strony albo <c>null</c>, gdy go brak, odpowiedź jest nieczytelna lub adres nie jest loopbackiem portu.</summary>
    public static Uri? PageWebSocketUrl(string? json, int cdpPort)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var target in document.RootElement.EnumerateArray())
            {
                if (target.ValueKind != JsonValueKind.Object
                    || !target.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String
                    || !string.Equals(type.GetString(), "page", StringComparison.Ordinal))
                {
                    continue;
                }

                if (target.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                    && (url.GetString() ?? string.Empty).StartsWith("devtools://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Pierwszy cel strony rozstrzyga: zły adres nie przechodzi na kolejny cel, tylko czeka na następne odpytanie.
                return target.TryGetProperty("webSocketDebuggerUrl", out var ws) && ws.ValueKind == JsonValueKind.String
                    ? LoopbackWebSocket(ws.GetString(), cdpPort)
                    : null;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Adres <c>ws://127.0.0.1:&lt;cdpPort&gt;/…</c> albo <c>null</c> dla każdego innego hosta, portu lub schematu.</summary>
    public static Uri? LoopbackWebSocket(string? url, int cdpPort)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "ws", StringComparison.Ordinal)
            || !string.Equals(uri.Host, LoopbackHost, StringComparison.Ordinal)
            || uri.Port != cdpPort
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri;
    }
}
