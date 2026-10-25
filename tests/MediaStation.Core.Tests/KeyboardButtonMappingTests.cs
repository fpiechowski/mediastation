using System.Text.Json;
using System.Text.Json.Nodes;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using Microsoft.Extensions.Time.Testing;

namespace MediaStation.Core.Tests;

/// <summary>Zapis przycisku Klawiatura z kalibracji do <c>input.json</c>: VK do <c>keys</c>, usage bez VK do <c>hid</c> (AD-8).</summary>
public class KeyboardButtonMappingTests
{
    private const string Dongle = "VID_1915&PID_1025";

    private const string DonglePath =
        @"\\?\HID#VID_1915&PID_1025&MI_01#7&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const string Json = """
        {
          "devices": [
            { "signature": "VID_1915&PID_1025", "source": "remote",
              "keys": { "0x0D": "ok", "0xA6": "back", "0xAC": "home" } },
            { "signature": "*", "source": "keyboard", "keys": { "0x0D": "ok" } }
          ],
          "gamepadHome": [ "back", "start" ]
        }
        """;

    private static RawInputEntry NoVk(int usage = 0x00CF) => new(Dongle, null, null, 0x000C, usage, null, false, null);

    private static RawInputEntry WithVk(int vk = 0x71) => new(Dongle, vk, 0x3C, 0x000C, 0x00E9, true, true, 12);

    private static JsonObject RemoteDevice(string text) =>
        (JsonObject)JsonNode.Parse(text)!["devices"]![0]!;

    /// <summary>Waliduje tekst regułami configu (3.15) i zwraca wczytany <see cref="InputConfig"/>.</summary>
    private static InputConfig Load(string text)
    {
        var result = ConfigValidation.Validate(new Dictionary<string, ConfigFileState>
        {
            [ConfigPreflight.InputFile] = new ConfigFileState.Present(text),
        });
        Assert.DoesNotContain(result.Errors, e => e.File == ConfigPreflight.InputFile);
        return JsonSerializer.Deserialize<InputConfig>(text, ConfigJson.Options)!;
    }

    [Fact]
    public void Button_without_vk_is_written_to_hid()
    {
        var result = KeyboardButtonMapping.Apply(Json, NoVk());

        Assert.Null(result.Error);
        var device = RemoteDevice(result.Text!);
        Assert.Equal("keyboard", (string?)device["hid"]!["0x000C:0x00CF"]);
        Assert.Null(device["keys"]!["0x00CF"]);
    }

    [Fact]
    public void Button_with_vk_is_written_to_keys()
    {
        var result = KeyboardButtonMapping.Apply(Json, WithVk());

        Assert.Null(result.Error);
        var device = RemoteDevice(result.Text!);
        Assert.Equal("keyboard", (string?)device["keys"]!["0x71"]);
        Assert.Null(device["hid"]);
    }

    [Fact]
    public void Written_file_passes_validation_and_keeps_other_mappings()
    {
        var config = Load(KeyboardButtonMapping.Apply(Json, NoVk()).Text!);

        var remote = config.Devices[0];
        Assert.Equal(ActionKind.Home, remote.Keys["0xAC"]);
        Assert.Equal(ActionKind.Keyboard, remote.Hid!["0x000C:0x00CF"]);
        Assert.Equal(2, config.Devices.Count);
    }

    [Fact]
    public void Written_hid_entry_gives_keyboard_remote_from_policy()
    {
        var config = Load(KeyboardButtonMapping.Apply(Json, NoVk()).Text!);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        var policy = new InputPolicy(config, time, new InputRoutingState(), new AlwaysManaged());

        var decision = policy.OnHidConsumer(new HidConsumerReport(DonglePath, 1000, 0x0C, 0x00CF));

        var action = Assert.Single(decision.Actions);
        Assert.Equal(ActionKind.Keyboard, action.Kind);
        Assert.Equal(ActionSource.Remote, action.Source);
        Assert.False(decision.Swallow);
    }

    [Fact]
    public void Previous_keyboard_button_in_the_same_section_is_replaced()
    {
        var first = KeyboardButtonMapping.Apply(Json, NoVk(0x00CF)).Text!;

        var second = KeyboardButtonMapping.Apply(first, NoVk(0x0040));

        Assert.Null(second.Error);
        var hid = (JsonObject)RemoteDevice(second.Text!)["hid"]!;
        Assert.Equal("keyboard", (string?)hid["0x000C:0x0040"]);
        Assert.Null(hid["0x000C:0x00CF"]);
    }

    [Fact]
    public void Repeated_choice_of_the_same_button_is_idempotent()
    {
        var first = KeyboardButtonMapping.Apply(Json, NoVk()).Text!;

        var second = KeyboardButtonMapping.Apply(first, NoVk());

        Assert.Equal(first, second.Text);
    }

    [Fact]
    public void Vk_already_mapped_to_another_action_is_refused()
    {
        var result = KeyboardButtonMapping.Apply(Json, WithVk(0x0D));

        Assert.Null(result.Text);
        Assert.Contains("0x0D", result.Error);
    }

    [Theory]
    [InlineData(0x0223)]
    [InlineData(0x0224)]
    public void Reserved_home_back_usage_is_refused_by_validation(int usage)
    {
        var result = KeyboardButtonMapping.Apply(Json, NoVk(usage));

        Assert.Null(result.Text);
        Assert.Contains("hid", result.Error);
    }

    [Fact]
    public void Unknown_signature_is_refused()
    {
        var result = KeyboardButtonMapping.Apply(Json, NoVk() with { Signature = "VID_0000&PID_0000" });

        Assert.Null(result.Text);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Entry_without_vk_and_usage_is_refused()
    {
        var result = KeyboardButtonMapping.Apply(Json, NoVk() with { UsagePage = null, Usage = null });

        Assert.Null(result.Text);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Malformed_json_is_refused()
    {
        var result = KeyboardButtonMapping.Apply("{ nope", NoVk());

        Assert.Null(result.Text);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{ "devices": [ { "signature": 5, "source": "remote" } ] }""")]
    [InlineData("""{ "devices": [ { "signature": "VID_1915&PID_1025", "source": "remote", "hid": { "0x000C:0x0040": 5 } } ] }""")]
    public void Wrongly_typed_json_is_refused_not_thrown(string json)
    {
        var result = KeyboardButtonMapping.Apply(json, NoVk());

        Assert.Null(result.Text);
        Assert.NotNull(result.Error);
    }

    private sealed class AlwaysManaged : IForegroundProbe
    {
        public bool IsManaged() => true;
    }
}
