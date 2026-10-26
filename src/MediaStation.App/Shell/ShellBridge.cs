using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Threading;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using Microsoft.Web.WebView2.Core;

namespace MediaStation.App.Shell;

/// <summary>
/// Most WebView2 postMessage z kopertą <c>{ v, type, payload }</c> (AD-16): jedyna klasa z <see cref="CoreWebView2"/>.
/// Logika leży poza WebView2: <see cref="ShellOutbox"/> (kolejka host → UI) i <see cref="ShellMessageGate"/>
/// (walidacja UI → host). <see cref="Publish"/> i <see cref="PublishAction"/> tylko zapisują do outboxa i planują
/// jeden <see cref="Flush"/> na wątku UI (AD-14); <c>ready</c> z UI odsyła najnowszy snapshot bez logu, więc handshake
/// usuwa wyścig nawigacji z pierwszą publikacją. Odrzucone wiadomości trafiają do logu jako <c>bridgeMessageRejected</c>,
/// a odrzucone Actions jako <c>shellActionDropped</c>.
/// </summary>
public sealed class ShellBridge
{
    private const string Src = "shell";

    private readonly ILog _log;
    private readonly Dispatcher _dispatcher;
    private readonly ShellOutbox _outbox;
    private readonly ShellMessageGate _gate;
    private readonly Action<Intent> _postIntent;
    private CoreWebView2? _webView;

    /// <param name="nowMs">Zegar o skali <c>Action.TsMono</c> (patrz <see cref="ShellOutbox"/>).</param>
    /// <param name="postIntent">Kolejka Orchestratora; wołana tylko dla Intentów z origin shella.</param>
    public ShellBridge(ShellSnapshot snapshot, ILog log, Dispatcher dispatcher, Func<long> nowMs, Action<Intent> postIntent)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(nowMs);
        ArgumentNullException.ThrowIfNull(postIntent);

        _outbox = new ShellOutbox(snapshot, nowMs);
        _gate = new ShellMessageGate(ShellOrigin.Uri);
        _log = log;
        _dispatcher = dispatcher;
        _postIntent = postIntent;
    }

    public void Attach(CoreWebView2 webView)
    {
        _webView = webView;
        webView.WebMessageReceived += OnWebMessageReceived;
        webView.NavigationStarting += (_, _) => _outbox.MarkNavigated();
    }

    /// <summary>
    /// Zapisuje najnowszy snapshot i planuje jeden flush na wątku UI. Kolejne wywołania przed flushem
    /// tylko podmieniają snapshot — wywołujący nigdy nie czeka na UI (AD-14).
    /// </summary>
    public void Publish(ShellSnapshot snapshot)
    {
        if (_outbox.PublishSnapshot(snapshot))
        {
            ScheduleFlush();
        }
    }

    /// <summary>Dokłada Action dla Shell (po snapshocie tego samego kroku) i planuje flush; bez czekania na UI.</summary>
    public void PublishAction(Action action)
    {
        if (_outbox.PublishAction(action))
        {
            ScheduleFlush();
        }
    }

    private void ScheduleFlush() => _dispatcher.BeginInvoke(new global::System.Action(Flush));

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try
        {
            json = e.WebMessageAsJson;
        }
        catch (ArgumentException ex)
        {
            Reject(null, null, $"nieczytelna wiadomość: {ex.Message}");
            return;
        }

        switch (_gate.Evaluate(json, e.Source))
        {
            case ShellMessage.Ready:
                _outbox.MarkReady();
                Send(_outbox.Latest);
                break;
            case ShellMessage.IntentAccepted accepted:
                _postIntent(accepted.Intent);
                break;
            case ShellMessage.Rejected rejected:
                Reject(rejected.V, rejected.Type, rejected.Reason);
                break;
        }
    }

    /// <summary>Wysyła na wątku UI to, co czeka w outboxie: snapshot (z logiem) przed Actions.</summary>
    private void Flush()
    {
        foreach (var delivery in _outbox.Drain())
        {
            switch (delivery)
            {
                case ShellDelivery.SnapshotDelivery { Snapshot: var pending }:
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
                    break;
                case ShellDelivery.ActionDelivery { Action: var action }:
                    _webView?.PostWebMessageAsJson(ContractJson.SerializeEnvelope(action));
                    break;
                case ShellDelivery.DroppedAction { Action: var dropped, Reason: var reason }:
                    _log.Warn(Src, "shellActionDropped", new Dictionary<string, object?>
                    {
                        ["kind"] = dropped.Kind,
                        ["phase"] = dropped.Phase,
                        ["reason"] = reason,
                    });
                    break;
            }
        }
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
