using System;
using System.Collections.Generic;
using System.Linq;
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
}

/// <summary>
/// Wstępne sprawdzenie configu: czy każdy z trzech plików istnieje, da się go odczytać i jest poprawnym JSON.
/// Czysta funkcja; pełna walidacja schematów przychodzi w story 4 i zastępuje tę funkcję bez zmiany ekranu ani mostu.
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
            var error = state switch
            {
                ConfigFileState.Missing => new ConfigError(file, null, MissingReason),
                ConfigFileState.Unreadable u => new ConfigError(
                    file,
                    null,
                    string.IsNullOrWhiteSpace(u.Detail) ? UnreadableReason : $"{UnreadableReason}: {u.Detail}"),
                ConfigFileState.Present p => CheckJson(file, p.Text),
                _ => null,
            };

            if (error is not null)
            {
                errors.Add(error);
            }
        }

        return errors;
    }

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
        var path = new StringBuilder("$");
        foreach (var frame in frames.Reverse())
        {
            if (frame.IsArray)
            {
                if (frame.Index >= 0)
                {
                    path.Append('[').Append(frame.Index).Append(']');
                }
            }
            else if (frame.Property is { } name)
            {
                if (IsPlainName(name))
                {
                    path.Append('.').Append(name);
                }
                else
                {
                    path.Append("['").Append(name.Replace("'", "\\'")).Append("']");
                }
            }
        }

        return path.ToString();
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
