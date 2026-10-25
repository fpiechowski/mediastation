using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaStation.Core.Config;

namespace MediaStation.Core.Input;

/// <summary>Wynik zapisu przycisku Klawiatura: nowy tekst <c>input.json</c> albo powód odmowy (dokładnie jedno z nich).</summary>
public sealed record KeyboardMappingResult(string? Text, string? Error);

/// <summary>
/// Zapis wybranego w kalibracji przycisku Klawiatura do <c>input.json</c> (AD-8): przycisk z VK idzie do <c>keys</c>
/// (<c>"0xNN": "keyboard"</c>), przycisk pilota bez VK do <c>hid</c> (<c>"0x000C:0x00CF": "keyboard"</c>) urządzenia
/// <c>source: remote</c> o sygnaturze wpisu. Czysta funkcja bez I/O (AD-1): wynik musi przejść walidację configu,
/// inaczej zwraca błąd i nie ma tekstu do zapisania. Poprzedni przycisk Klawiatura w tej samej sekcji jest zastępowany.
/// </summary>
public static class KeyboardButtonMapping
{
    private const string Keyboard = "keyboard";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static KeyboardMappingResult Apply(string inputJson, RawInputEntry entry)
    {
        ArgumentNullException.ThrowIfNull(inputJson);
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            return ApplyCore(inputJson, entry);
        }
        catch (InvalidOperationException)
        {
            // JsonNode rzuca na złym typie węzła (np. korzeń tablicą, nietekstowa sygnatura albo Action).
            return Fail("input.json ma strukturę niezgodną ze schematem");
        }
    }

    private static KeyboardMappingResult ApplyCore(string inputJson, RawInputEntry entry)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(inputJson);
        }
        catch (JsonException)
        {
            return Fail("input.json nie jest poprawnym JSON");
        }

        var device = (root?["devices"] as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(d => (string?)d["signature"] == entry.Signature && (string?)d["source"] == "remote");
        if (device is null)
        {
            return Fail($"brak urządzenia remote o sygnaturze {entry.Signature}");
        }

        string section;
        string key;
        if (entry.HasVk)
        {
            if (entry.VirtualKey is not { } virtualKey)
            {
                return Fail("wpis z hasVk bez kodu VK");
            }

            (section, key) = ("keys", $"0x{virtualKey:X2}");
        }
        else if (entry.UsagePage is { } page && entry.Usage is { } usage)
        {
            (section, key) = ("hid", $"0x{page:X4}:0x{usage:X4}");
        }
        else
        {
            return Fail("wpis bez VK i bez usage nie ma czego zapisać");
        }

        var map = device[section] as JsonObject;
        if (device[section] is not null && map is null)
        {
            return Fail($"sekcja {section} nie jest obiektem");
        }

        if (map is null)
        {
            map = [];
            device[section] = map;
        }

        if (map[key] is { } existing && (string?)existing != Keyboard)
        {
            return Fail($"klucz {key} w {section} ma już Action {existing}");
        }

        foreach (var other in map.Where(p => p.Key != key && (string?)p.Value == Keyboard).Select(p => p.Key).ToList())
        {
            map.Remove(other);
        }

        map[key] = Keyboard;

        var text = root!.ToJsonString(WriteOptions);
        var errors = ConfigValidation
            .Validate(new Dictionary<string, ConfigFileState> { [ConfigPreflight.InputFile] = new ConfigFileState.Present(text) })
            .Errors
            .Where(e => e.File == ConfigPreflight.InputFile)
            .ToList();
        return errors.Count == 0
            ? new KeyboardMappingResult(text, null)
            : Fail($"wynik nie przechodzi walidacji: {errors[0].Path} {errors[0].Reason}");
    }

    private static KeyboardMappingResult Fail(string error) => new(null, error);
}
