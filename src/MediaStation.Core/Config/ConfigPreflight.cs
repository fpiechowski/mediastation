using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Config;

/// <summary>Stan jednego pliku configu odczytany przez App (Core nie robi I/O).</summary>
public abstract record ConfigFileState
{
    private ConfigFileState()
    {
    }

    public sealed record Present(string Text) : ConfigFileState;

    public sealed record Missing : ConfigFileState;

    /// <summary>Plik istnieje, ale nie dało się go odczytać (blokada, brak uprawnień).</summary>
    public sealed record Unreadable(string? Detail) : ConfigFileState;

    /// <summary>
    /// Stan pliku dla wyjątku I/O z odczytu; <c>null</c> = wyjątek nie jest I/O i leci dalej.
    /// Brak pliku i katalogu to <see cref="Missing"/>, a błąd dostępu lub odczytu — <see cref="Unreadable"/>.
    /// </summary>
    public static ConfigFileState? FromReadError(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error switch
        {
            FileNotFoundException or DirectoryNotFoundException => new Missing(),
            IOException or UnauthorizedAccessException or SecurityException => new Unreadable(error.Message),
            _ => null,
        };
    }
}

/// <summary>
/// Wstępne sprawdzenie configu: czy każdy z trzech plików istnieje, da się go odczytać i jest poprawnym JSON.
/// Czysta funkcja; pierwszy krok <see cref="ConfigValidation.Validate"/>, który dokłada schemat i reguły semantyczne.
/// </summary>
public static class ConfigPreflight
{
    public const string AppsFile = "apps.json";
    public const string InputFile = "input.json";
    public const string SettingsFile = "settings.json";

    /// <summary>Pliki configu w kolejności raportowania błędów.</summary>
    public static IReadOnlyList<string> RequiredFiles { get; } = [AppsFile, InputFile, SettingsFile];

    public const string MissingReason = "brak pliku";
    public const string UnreadableReason = "nie można odczytać pliku";

    /// <param name="files">Stan każdego pliku po nazwie; brak klucza = brak pliku.</param>
    public static IReadOnlyList<ConfigError> Check(IReadOnlyDictionary<string, ConfigFileState> files)
    {
        var errors = new List<ConfigError>();
        foreach (var file in RequiredFiles)
        {
            var state = files.TryGetValue(file, out var s) ? s : new ConfigFileState.Missing();
            if (CheckFile(file, state) is { } error)
            {
                errors.Add(error);
            }
        }

        return errors;
    }

    /// <summary>Preflight jednego pliku: brak, nieczytelny albo niepoprawny JSON; <c>null</c>, gdy plik przechodzi.</summary>
    internal static ConfigError? CheckFile(string file, ConfigFileState state) => state switch
    {
        ConfigFileState.Missing => new ConfigError(file, null, MissingReason),
        ConfigFileState.Unreadable u => new ConfigError(
            file,
            null,
            string.IsNullOrWhiteSpace(u.Detail) ? UnreadableReason : $"{UnreadableReason}: {u.Detail}"),
        ConfigFileState.Present p => CheckJson(file, p.Text),
        _ => null,
    };

    private static ConfigError? CheckJson(string file, string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return null;
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line
                ? $" (linia {line + 1}, znak {CharColumn(text, line, ex.BytePositionInLine ?? 0) + 1})"
                : string.Empty;
            return new ConfigError(file, ex.Path ?? JsonErrorPath.Locate(text), $"nieczytelny JSON{where}");
        }
    }

    /// <summary>Zamienia offset w bajtach UTF-8 w linii (z <see cref="JsonException"/>) na kolumnę w znakach.</summary>
    private static long CharColumn(string text, long lineIndex, long bytePosition)
    {
        var lines = text.Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length)
        {
            return bytePosition;
        }

        var bytes = Encoding.UTF8.GetBytes(lines[lineIndex]);
        var count = (int)Math.Min(bytePosition, bytes.Length);
        return Encoding.UTF8.GetCharCount(bytes, 0, count);
    }
}

/// <summary>
/// Ścieżka JSON (<c>$.a</c>, <c>$.apps[2]</c>) miejsca, w którym parser przerwał odczyt.
/// <see cref="JsonDocument"/> nie podaje jej w <see cref="JsonException"/>, więc liczymy ją jednym przebiegiem readera.
/// </summary>
internal static class JsonErrorPath
{
    private sealed class Frame(bool isArray)
    {
        public bool IsArray { get; } = isArray;

        public string? Property { get; set; }

        public int Index { get; set; } = -1;
    }

    public static string? Locate(string text)
    {
        var frames = new Stack<Frame>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), isFinalBlock: true, state: default);
        try
        {
            while (reader.Read())
            {
                var token = reader.TokenType;
                if (token is JsonTokenType.EndObject or JsonTokenType.EndArray)
                {
                    frames.Pop();
                    ValueCompleted(frames);
                    continue;
                }

                if (token == JsonTokenType.PropertyName)
                {
                    frames.Peek().Property = reader.GetString();
                    continue;
                }

                if (frames.TryPeek(out var top) && top.IsArray)
                {
                    top.Index++;
                }

                if (token is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    frames.Push(new Frame(token == JsonTokenType.StartArray));
                }
                else
                {
                    ValueCompleted(frames);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return Format(frames);
        }
    }

    // Wartość właściwości skończona: błąd dalej w obiekcie wskazuje obiekt, nie poprzednią właściwość.
    private static void ValueCompleted(Stack<Frame> frames)
    {
        if (frames.TryPeek(out var top) && !top.IsArray)
        {
            top.Property = null;
        }
    }

    private static string Format(Stack<Frame> frames)
    {
        var path = "$";
        foreach (var frame in frames.Reverse())
        {
            if (frame.IsArray)
            {
                if (frame.Index >= 0)
                {
                    path = Index(path, frame.Index);
                }
            }
            else if (frame.Property is { } name)
            {
                path = Property(path, name);
            }
        }

        return path;
    }

    /// <summary>Ścieżka do właściwości: <c>$.a</c>, a dla nazw spoza identyfikatorów <c>$['a b']</c>.</summary>
    public static string Property(string parent, string name) =>
        IsPlainName(name) ? $"{parent}.{name}" : $"{parent}['{name.Replace("'", "\\'")}']";

    /// <summary>Ścieżka do elementu tablicy: <c>$.apps[2]</c>.</summary>
    public static string Index(string parent, int index) => $"{parent}[{index}]";

    /// <summary>Segment ścieżki: nazwa właściwości (<see cref="Name"/>) albo indeks tablicy (<see cref="Index"/>).</summary>
    public sealed record Segment(string? Name, int Index);

    /// <summary>
    /// Rozbiera ścieżkę z <see cref="JsonException.Path"/> (<c>$.devices[0].keys.0x26</c>, <c>$['a b']</c>) na segmenty;
    /// <c>null</c>, gdy ścieżka ma nieznany kształt.
    /// </summary>
    public static IReadOnlyList<Segment>? Parse(string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '$')
        {
            return null;
        }

        var segments = new List<Segment>();
        var i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var end = path.IndexOfAny(['.', '['], i + 1);
                end = end < 0 ? path.Length : end;
                segments.Add(new Segment(path[(i + 1)..end], -1));
                i = end;
            }
            else if (path[i] == '[' && i + 1 < path.Length && path[i + 1] == '\'')
            {
                var end = path.IndexOf("']", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    return null;
                }

                segments.Add(new Segment(path[(i + 2)..end], -1));
                i = end + 2;
            }
            else if (path[i] == '[')
            {
                var end = path.IndexOf(']', i + 1);
                if (end < 0 || !int.TryParse(path.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                {
                    return null;
                }

                segments.Add(new Segment(null, index));
                i = end + 1;
            }
            else
            {
                return null;
            }
        }

        return segments;
    }

    /// <summary>Składa segmenty w ścieżkę w formacie preflight (<see cref="Property"/>, <see cref="Index"/>).</summary>
    public static string Format(IEnumerable<Segment> segments)
    {
        var path = "$";
        foreach (var segment in segments)
        {
            path = segment.Name is { } name ? Property(path, name) : Index(path, segment.Index);
        }

        return path;
    }

    private static bool IsPlainName(string name)
    {
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
