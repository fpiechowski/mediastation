using System.Text.Json;
using MediaStation.Core.Contracts;

namespace MediaStation.App.Shell;

/// <summary>Wynik oceny wiadomości z Shell przez <see cref="ShellMessageGate"/>.</summary>
public abstract record ShellMessage
{
    /// <summary><c>ready</c>: UI jest gotowy na snapshot.</summary>
    public sealed record Ready : ShellMessage;

    /// <summary>Poprawny Intent z origin shella, do kolejki Orchestratora.</summary>
    public sealed record IntentAccepted(Intent Intent) : ShellMessage;

    /// <summary>Wiadomość odrzucona; <see cref="V"/> i <see cref="Type"/> tylko, jeśli dało się je odczytać.</summary>
    public sealed record Rejected(int? V, string? Type, string Reason) : ShellMessage;
}

/// <summary>
/// Walidacja wiadomości UI → host (AD-16), bez typów WebView2/WPF, żeby dała się testować. Przyjmuje wyłącznie
/// <c>ready</c> oraz Intents <c>launchApp</c>/<c>restartApp</c>/<c>closeApp</c>, i to tylko z origin shella;
/// <c>snapshot</c>, <c>action</c> i nieznane typy od Shell są odrzucane.
/// </summary>
public sealed class ShellMessageGate
{
    private readonly Uri _origin;

    public ShellMessageGate(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _origin = origin;
    }

    /// <summary>Ocenia surowy JSON koperty i <c>Source</c> zdarzenia WebView2 (może być <c>null</c> albo nieczytelny).</summary>
    public ShellMessage Evaluate(string json, string? source)
    {
        Envelope<JsonElement?>? envelope;
        try
        {
            envelope = ContractJson.Deserialize<Envelope<JsonElement?>>(json);
        }
        catch (JsonException ex)
        {
            return new ShellMessage.Rejected(null, null, $"nieczytelny JSON: {ex.Message}");
        }

        if (envelope is null)
        {
            return new ShellMessage.Rejected(null, null, "koperta jest null");
        }

        if (!IsShellOrigin(source))
        {
            return new ShellMessage.Rejected(envelope.V, envelope.Type, "wiadomość spoza origin shella");
        }

        if (envelope.V != Envelope.Version)
        {
            return new ShellMessage.Rejected(envelope.V, envelope.Type, $"nieobsługiwana wersja koperty (oczekiwano {Envelope.Version})");
        }

        if (envelope.Type == Envelope.ReadyType)
        {
            return new ShellMessage.Ready();
        }

        return envelope.Type switch
        {
            "launchApp" => ParseIntent<LaunchApp>(envelope, static intent => intent.AppId),
            "restartApp" => ParseIntent<RestartApp>(envelope, static intent => intent.AppId),
            "closeApp" => ParseIntent<CloseApp>(envelope, static intent => intent.AppId),
            _ => new ShellMessage.Rejected(envelope.V, envelope.Type, "nieobsługiwany typ komunikatu od Shell"),
        };
    }

    private static ShellMessage ParseIntent<TIntent>(Envelope<JsonElement?> envelope, Func<TIntent, string?> appId)
        where TIntent : Intent
    {
        if (envelope.Payload is not { ValueKind: JsonValueKind.Object } payload)
        {
            return new ShellMessage.Rejected(envelope.V, envelope.Type, "payload nie jest obiektem");
        }

        TIntent? intent;
        try
        {
            intent = payload.Deserialize<TIntent>(ContractJson.ReadOptions);
        }
        catch (JsonException ex)
        {
            return new ShellMessage.Rejected(envelope.V, envelope.Type, $"niepoprawny payload: {ex.Message}");
        }

        if (intent is null || string.IsNullOrEmpty(appId(intent)))
        {
            return new ShellMessage.Rejected(envelope.V, envelope.Type, "pusty appId");
        }

        return new ShellMessage.IntentAccepted(intent);
    }

    /// <summary>Origin to scheme + host + port po sparsowaniu <c>Source</c>, nie prefiks napisu.</summary>
    private bool IsShellOrigin(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, _origin.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, _origin.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == _origin.Port;
}
