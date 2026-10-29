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
    /// Czysty odczyt koperty z bindingu: zwraca <see cref="PageHello"/> albo powód odrzucenia (<c>unparsable</c>,
    /// <c>version</c>, <c>type</c>, <c>payload</c>). Nigdy nie rzuca: zła wiadomość strony to wpis w logu, nie wyjątek.
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

            if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number
                || !v.TryGetInt32(out var version) || version != Version)
            {
                return PageMessageResult.Rejected("version");
            }

            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), HelloType, StringComparison.Ordinal))
            {
                return PageMessageResult.Rejected("type");
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object
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
}

/// <summary>
/// <c>hello</c> ze strony (AD-7), po każdym nowym dokumencie głównej ramki. <see cref="Webdriver"/> to
/// <c>navigator.webdriver === true</c> zmierzone przez samą stronę, więc host nie potrzebuje <c>Runtime.evaluate</c>.
/// W <c>hello</c> v1 pole jest wymagane, bo weszło razem z komunikatem; kolejne pola AD-7 dodaje się wstecznie
/// zgodnie, bez zmiany <c>v</c>.
/// </summary>
public sealed record PageHello(string Service, string? DriverVersion, bool Webdriver);

/// <summary>Wynik <see cref="PageProtocol.Parse"/>: dokładnie jedno z <see cref="Hello"/> i <see cref="Reason"/> nie jest <c>null</c>.</summary>
public sealed record PageMessageResult(PageHello? Hello, string? Reason)
{
    public static PageMessageResult Rejected(string reason) => new(null, reason);
}
