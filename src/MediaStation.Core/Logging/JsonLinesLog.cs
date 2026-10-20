using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaStation.Core.Logging;

/// <summary>
/// <see cref="ILog"/> w formacie JSON Lines (AD-13): jedna linia = jeden kompaktowy obiekt
/// <c>{ ts, level, src, msg, data?, err? }</c>, <c>ts</c> w UTC ISO-8601 z <c>Z</c>. Czas tylko z <see cref="TimeProvider"/> (AD-14).
/// Rotacja dzienna = inna nazwa pliku (<see cref="LogFileName"/>) między kolejnymi zapisami. Zapis synchroniczny pod lockiem,
/// więc kolejność linii odpowiada kolejności <c>ts</c>, także na granicy dób.
/// </summary>
public sealed class JsonLinesLog : ILog
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly TimeProvider _time;
    private readonly ILogLineSink _sink;
    private readonly object _gate = new();

    public JsonLinesLog(TimeProvider time, ILogLineSink sink)
    {
        _time = time;
        _sink = sink;
    }

    public void Write(LogLevel level, string src, string msg, IReadOnlyDictionary<string, object?>? data = null, Exception? error = null)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var line = Format(now, level, src, msg, data, error);
            _sink.Append(LogFileName.For(now), line);
        }
    }

    /// <summary>Linia logu bez znaku nowej linii; <c>\n</c> w treści jest escapowany.</summary>
    public static string Format(DateTimeOffset time, LogLevel level, string src, string msg, IReadOnlyDictionary<string, object?>? data, Exception? error)
    {
        var ts = time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var err = error is null ? null : new LogError(error.GetType().FullName ?? error.GetType().Name, error.Message, error.ToString());
        try
        {
            return JsonSerializer.Serialize(new LogLine(ts, level, src, msg, data, err), Options);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nieserializowalne `data` (cykl, NaN, nieobsługiwany typ, rzucający getter) nie może zgubić wpisu ani przerwać aplikacji.
            var fallback = new Dictionary<string, object?> { ["serializationError"] = ex.Message };
            return JsonSerializer.Serialize(new LogLine(ts, level, src, msg, fallback, err), Options);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            // Polskie znaki czytelne w pliku; znaki sterujące (w tym \n) i cudzysłowy nadal escapowane.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed record LogLine(
        string Ts,
        LogLevel Level,
        string Src,
        string Msg,
        IReadOnlyDictionary<string, object?>? Data,
        LogError? Err);

    /// <summary><c>stack</c> to pełne <see cref="Exception.ToString"/>, więc niesie też wyjątki wewnętrzne.</summary>
    private sealed record LogError(string Type, string Message, string Stack);
}
