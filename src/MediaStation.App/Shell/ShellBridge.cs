using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Threading;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Orchestrator;
using Microsoft.Web.WebView2.Core;

namespace MediaStation.App.Shell;

/// <summary>
/// Most WebView2 postMessage z kopertą <c>{ v, type, payload }</c> (AD-16).
/// <see cref="Publish"/> tylko zapisuje najnowszy snapshot i planuje flush na wątku UI (latest-wins, AD-14);
/// <c>ready</c> z UI odsyła najnowszy snapshot bez logu, więc handshake usuwa wyścig nawigacji z pierwszą publikacją.
/// Odrzucone wiadomości trafiają do logu jako <c>bridgeMessageRejected</c>.
/// </summary>
public sealed class ShellBridge
{
    private const string Src = "shell";

    private readonly ILog _log;
    private readonly Dispatcher _dispatcher;
    private readonly LatestSnapshotSlot _slot;
    private CoreWebView2? _webView;

    public ShellBridge(ShellSnapshot snapshot, ILog log, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _slot = new LatestSnapshotSlot(snapshot);
        _log = log;
        _dispatcher = dispatcher;
    }

    public void Attach(CoreWebView2 webView)
    {
        _webView = webView;
        webView.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>
    /// Zapisuje najnowszy snapshot i planuje jeden flush na wątku UI. Kolejne wywołania przed flushem
    /// tylko podmieniają snapshot — wywołujący nigdy nie czeka na UI (AD-14).
    /// </summary>
    public void Publish(ShellSnapshot snapshot)
    {
        if (_slot.Publish(snapshot))
        {
            _dispatcher.BeginInvoke(new global::System.Action(Flush));
        }
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

        Send(_slot.Latest);
    }

    /// <summary>Wysyła najnowszy snapshot na wątek UI i loguje jedną publikację na flush.</summary>
    private void Flush()
    {
        if (_slot.TakePending() is not { } pending)
        {
            return;
        }

        Send(pending);

        _log.Info(Src, "snapshotPublished", new Dictionary<string, object?>
        {
            ["rev"] = pending.Rev,
            ["focusRequest"] = pending.FocusRequest is { } focus
                ? new Dictionary<string, object?>
                {
                    ["target"] = focus.Target,
                    ["seq"] = focus.Seq,
                }
                : null,
        });
    }

    private void Reject(int? v, string? type, string reason) =>
        _log.Warn(Src, "bridgeMessageRejected", new Dictionary<string, object?>
        {
            ["v"] = v,
            ["type"] = type,
            ["reason"] = reason,
        });

    private void Send(ShellSnapshot snapshot) =>
        _webView?.PostWebMessageAsJson(ContractJson.Serialize(Envelope.Snapshot(snapshot)));
}
