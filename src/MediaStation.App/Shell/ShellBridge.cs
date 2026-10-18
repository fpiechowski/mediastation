using System.Text.Json;
using MediaStation.Core.Contracts;
using Microsoft.Web.WebView2.Core;

namespace MediaStation.App.Shell;

/// <summary>
/// Most WebView2 postMessage z kopertą <c>{ v, type, payload }</c> (AD-16).
/// Na <c>ready</c> z UI odsyła najnowszy snapshot; handshake usuwa wyścig nawigacji z pierwszym snapshotem.
/// </summary>
public sealed class ShellBridge
{
    private CoreWebView2? _webView;
    private ShellSnapshot _snapshot;

    public ShellBridge(ShellSnapshot snapshot)
    {
        _snapshot = snapshot;
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
        catch (JsonException)
        {
            return;
        }

        if (envelope is null || envelope.V != Envelope.Version)
        {
            return;
        }

        if (envelope.Type == Envelope.ReadyType)
        {
            Send();
        }
    }

    private void Send()
    {
        _webView?.PostWebMessageAsJson(ContractJson.Serialize(Envelope.Snapshot(_snapshot)));
    }
}
