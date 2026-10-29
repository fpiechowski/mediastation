using System;
using System.Text.Json;

namespace MediaStation.Core.Contracts;

/// <summary>
/// Stałe protokołu strony v1 (AD-7): koperta <c>{ v, type, id, payload }</c> płynie z PageRuntime przez binding CDP
/// <see cref="BindingName"/>. To nie jest most Shell ↔ Core, więc typy strony nie trafiają do <see cref="MessageTypes"/>.
/// </summary>
public static class PageProtocol
{
    /// <summary>Major wersji koperty; inny <c>v</c> host odrzuca (AD-7).</summary>
    public const int Version = 1;

    /// <summary>Nazwa bindingu CDP (<c>Runtime.addBinding</c>), przez który strona wysyła koperty do hosta.</summary>
    public const string BindingName = "__msBridge";

    /// <summary><c>type</c> komunikatu <see cref="PageHello"/>.</summary>
    public const string HelloType = "hello";

    /// <summary>
    /// Czysty odczyt koperty z bindingu: zwraca <see cref="PageHello"/> albo powód odrzucenia, sprawdzany w kolejności:
    /// <c>unparsable</c> (pusty tekst, zły JSON, nie obiekt); <c>envelope</c> (brak <c>v</c> albo <c>v</c> nie jest liczbą
    /// całkowitą); <c>version</c> (całkowite <c>v</c> ≠ 1, z <see cref="PageMessageResult.Version"/>); <c>envelope</c>
    /// (<c>type</c> nie jest niepustym stringiem, <c>id</c> brak albo nie jest <c>null</c>/niepustym stringiem, brak
    /// <c>payload</c>); <c>type</c> (inny niż <c>hello</c>); <c>id</c> (<c>hello</c> z niepustym <c>id</c>);
    /// <c>payload</c>. Nigdy nie rzuca: zła wiadomość strony to wpis w logu, nie wyjątek.
    /// </summary>
    public static PageMessageResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return PageMessageResult.Rejected("unparsable");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return PageMessageResult.Rejected("unparsable");
            }

            if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version))
            {
                return PageMessageResult.Rejected("envelope");
            }

            if (version != Version)
            {
                return PageMessageResult.Rejected("version", version);
            }

            if (ReadEnvelope(root, version) is not { } envelope)
            {
                return PageMessageResult.Rejected("envelope");
            }

            if (!string.Equals(envelope.Type, HelloType, StringComparison.Ordinal))
            {
                return PageMessageResult.Rejected("type");
            }

            // `hello` strona wysyła z własnej inicjatywy, więc nie odpowiada na żadne żądanie hosta.
            if (envelope.Id is not null)
            {
                return PageMessageResult.Rejected("id");
            }

            var payload = envelope.Payload;
            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("service", out var service) || service.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(service.GetString())
                || !payload.TryGetProperty("driverVersion", out var driverVersion)
                || driverVersion.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                || !payload.TryGetProperty("webdriver", out var webdriver)
                || webdriver.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return PageMessageResult.Rejected("payload");
            }

            return new PageMessageResult(
                new PageHello(service.GetString()!, driverVersion.GetString(), webdriver.GetBoolean()),
                null);
        }
        catch (JsonException)
        {
            return PageMessageResult.Rejected("unparsable");
        }
    }

    /// <summary>
    /// Pola koperty po sprawdzonym <c>v</c> albo <c>null</c>, gdy <c>type</c>, <c>id</c> lub <c>payload</c> mają zły kształt.
    /// <see cref="PageEnvelope.Payload"/> jest klonem, więc przeżywa dokument.
    /// </summary>
    private static PageEnvelope? ReadEnvelope(JsonElement root, int version)
    {
        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(type.GetString()))
        {
            return null;
        }

        if (!root.TryGetProperty("id", out var id)
            || !(id.ValueKind == JsonValueKind.Null || (id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString()))))
        {
            return null;
        }

        if (!root.TryGetProperty("payload", out var payload))
        {
            return null;
        }

        return new PageEnvelope(version, type.GetString()!, id.ValueKind == JsonValueKind.Null ? null : id.GetString(), payload.Clone());
    }
}

/// <summary>
/// Koperta protokołu strony v1 (AD-7): każde pole jest zawsze obecne. <see cref="Id"/> to <c>null</c> w komunikatach
/// strony wysłanych z jej inicjatywy, a niepusty string w komunikatach host → page i odpowiedziach na nie.
/// </summary>
public sealed record PageEnvelope(int V, string Type, string? Id, JsonElement Payload);

/// <summary>
/// <c>hello</c> ze strony (AD-7), po każdym nowym dokumencie głównej ramki. <see cref="Webdriver"/> to
/// <c>navigator.webdriver === true</c> zmierzone przez samą stronę, więc host nie potrzebuje <c>Runtime.evaluate</c>.
/// W <c>hello</c> v1 pole jest wymagane, bo weszło razem z komunikatem; kolejne pola AD-7 dodaje się wstecznie
/// zgodnie, bez zmiany <c>v</c>. Rekord jest eksportowany do schematu, więc PageRuntime bierze typ z
/// <c>web/contracts/generated/page-hello.ts</c> (AD-16).
/// </summary>
public sealed record PageHello(string Service, string? DriverVersion, bool Webdriver);

/// <summary>
/// Wynik <see cref="PageProtocol.Parse"/>: dokładnie jedno z <see cref="Hello"/> i <see cref="Reason"/> nie jest <c>null</c>.
/// <see cref="Version"/> niesie odrzucone całkowite <c>v</c> przy powodzie <c>version</c>; w pozostałych <c>null</c>.
/// </summary>
public sealed record PageMessageResult(PageHello? Hello, string? Reason, int? Version = null)
{
    public static PageMessageResult Rejected(string reason, int? version = null) => new(null, reason, version);
}
