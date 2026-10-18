using MediaStation.Core.Config;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class ConfigPreflightTests
{
    private const string ValidJson = """{ "ok": true }""";

    private static Dictionary<string, ConfigFileState> AllValid() => new()
    {
        [ConfigPreflight.AppsFile] = new ConfigFileState.Present(ValidJson),
        [ConfigPreflight.InputFile] = new ConfigFileState.Present(ValidJson),
        [ConfigPreflight.SettingsFile] = new ConfigFileState.Present(ValidJson),
    };

    [Fact]
    public void Happy_path_gives_no_errors()
    {
        Assert.Empty(ConfigPreflight.Check(AllValid()));
    }

    [Fact]
    public void Missing_file_gives_entry_with_null_path()
    {
        var files = AllValid();
        files[ConfigPreflight.SettingsFile] = new ConfigFileState.Missing();

        var error = Assert.Single(ConfigPreflight.Check(files));

        Assert.Equal(new ConfigError("settings.json", null, "brak pliku"), error);
    }

    [Fact]
    public void Absent_dictionary_key_counts_as_missing_file()
    {
        var files = AllValid();
        files.Remove(ConfigPreflight.AppsFile);

        var error = Assert.Single(ConfigPreflight.Check(files));

        Assert.Equal(new ConfigError("apps.json", null, "brak pliku"), error);
    }

    [Fact]
    public void Unparseable_json_gives_path_and_line_number()
    {
        var files = AllValid();
        files[ConfigPreflight.SettingsFile] = new ConfigFileState.Present("""{ "a": """);

        var error = Assert.Single(ConfigPreflight.Check(files));

        Assert.Equal("settings.json", error.File);
        Assert.Equal("$.a", error.Path);
        Assert.StartsWith("nieczytelny JSON", error.Reason);
        Assert.Contains("linia 1", error.Reason);
    }

    [Theory]
    [InlineData("""{ "apps": [ { "id": 1 }, { "id": } ] }""", "$.apps[1].id")]
    [InlineData("""[ 1, 2, ]""", "$[1]")]
    [InlineData("""{ "a b": [ 1 }""", "$['a b'][0]")]
    [InlineData("""nope""", "$")]
    [InlineData("""{ "a": """, "$.a")]
    [InlineData("""{"a":1,}""", "$")]
    [InlineData("""{"a": {"b": 1}, "c" 2}""", "$")]
    [InlineData("""{"x": {"a": [1], }}""", "$.x")]
    public void Unparseable_json_path_points_at_failure(string text, string expectedPath)
    {
        var files = AllValid();
        files[ConfigPreflight.AppsFile] = new ConfigFileState.Present(text);

        var error = Assert.Single(ConfigPreflight.Check(files));

        Assert.Equal(expectedPath, error.Path);
    }

    [Fact]
    public void Column_counts_characters_not_utf8_bytes()
    {
        var files = AllValid();
        // „ąę” to 2 znaki, ale 4 bajty UTF-8; błąd (brak ':') na „2”, 16. znaku (18. bajcie).
        files[ConfigPreflight.SettingsFile] = new ConfigFileState.Present("""{ "ąę": 1, "b" 2 }""");

        var error = Assert.Single(ConfigPreflight.Check(files));

        Assert.Contains("linia 1, znak 16)", error.Reason);
    }

    [Fact]
    public void Line_number_is_one_based()
    {
        var files = AllValid();
        files[ConfigPreflight.InputFile] = new ConfigFileState.Present("{\n  \"a\": 1,\n  \"b\": }\n");

        var error = Assert.Single(ConfigPreflight.Check(files));

        Assert.Contains("linia 3", error.Reason);
    }

    [Fact]
    public void Empty_file_is_unparseable_json()
    {
        var files = AllValid();
        files[ConfigPreflight.AppsFile] = new ConfigFileState.Present("");

        var error = Assert.Single(ConfigPreflight.Check(files));

        Assert.Equal("apps.json", error.File);
        Assert.StartsWith("nieczytelny JSON", error.Reason);
    }

    [Fact]
    public void Several_errors_are_ordered_apps_input_settings()
    {
        var files = new Dictionary<string, ConfigFileState>
        {
            [ConfigPreflight.SettingsFile] = new ConfigFileState.Present(ValidJson),
            [ConfigPreflight.InputFile] = new ConfigFileState.Present("[1, "),
        };

        var errors = ConfigPreflight.Check(files);

        Assert.Collection(
            errors,
            e => Assert.Equal(new ConfigError("apps.json", null, "brak pliku"), e),
            e =>
            {
                Assert.Equal("input.json", e.File);
                Assert.StartsWith("nieczytelny JSON", e.Reason);
            });
    }

    [Fact]
    public void Unreadable_file_gives_read_error_reason()
    {
        var files = AllValid();
        files[ConfigPreflight.SettingsFile] = new ConfigFileState.Unreadable(null);

        var error = Assert.Single(ConfigPreflight.Check(files));

        Assert.Equal(new ConfigError("settings.json", null, "nie można odczytać pliku"), error);
    }

    [Fact]
    public void Unreadable_file_keeps_detail_after_reason()
    {
        var files = AllValid();
        files[ConfigPreflight.SettingsFile] = new ConfigFileState.Unreadable("plik jest używany przez inny proces");

        var error = Assert.Single(ConfigPreflight.Check(files));

        Assert.Equal("nie można odczytać pliku: plik jest używany przez inny proces", error.Reason);
    }
}
