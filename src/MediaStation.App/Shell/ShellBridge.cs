using System.Collections.Generic;
using System.Text.Json;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using Microsoft.Web.WebView2.Core;

namespace MediaStation.App.Shell;

/// <summary>
/// Most WebView2 postMessage z kopertą <c>{ v, type, payload }</c> (AD-16).
/// Na <c>ready</c> z UI odsyła najnowszy snapshot; handshake usuwa wyścig nawigacji z pierwszym snapshotem.
/// Odrzucone wiadomości trafiają do logu jako <c>bridgeMessageRejected</c>.
/// </summary>
public sealed class ShellBridge
{
    private const string Src = "shell";

    private readonly ILog _log;
    private CoreWebView2? _webView;
    private ShellSnapshot _snapshot;

    public ShellBridge(ShellSnapshot snapshot, ILog log)
    {
        _snapshot = snapshot;
        _log = log;
    }

    public void Attach(CoreWebView2 webView)
    {
        _webView = webView;
        webView.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Podmienia snapshot (latest-wins) i wysyła go, jeśli UI już działa.</summary>
    public void Publish(ShellSnapshot snapshot)
    {
        _snapshot = snapshot;
        Send();
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        Envelope<JsonElement?>? envelope;
        try
        {
            envelope = ContractJson.Deserialize<Envelope<JsonElement?>>(e.WebMessageAsJson);
        }
        catch (JsonException ex)
        {
            Reject(null, null, $"nieczytelny JSON: {ex.Message}");
            return;
        }

        if (envelope is null)
        {
            Reject(null, null, "koperta jest null");
            return;
        }

        if (envelope.V != Envelope.Version)
        {
            Reject(envelope.V, envelope.Type, $"nieobsługiwana wersja koperty (oczekiwano {Envelope.Version})");
            return;
        }

        if (envelope.Type != Envelope.ReadyType)
        {
            Reject(envelope.V, envelope.Type, "nieobsługiwany typ komunikatu (oczekiwano „ready”)");
            return;
        }

        Send();
    }

    private void Reject(int? v, string? type, string reason) =>
        _log.Warn(Src, "bridgeMessageRejected", new Dictionary<string, object?>
        {
            ["v"] = v,
            ["type"] = type,
            ["reason"] = reason,
        });

    private void Send()
    {
        _webView?.PostWebMessageAsJson(ContractJson.Serialize(Envelope.Snapshot(_snapshot)));
    }
}
