using MediaStation.App.Shell;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class ShellMessageGateTests
{
    private const string Origin = "http://localhost:5173/";

    private static readonly ShellMessageGate Gate = new(new Uri(Origin));

    private static string Message(string type, string payload = "null", int v = 1) =>
        "{\"v\":" + v + ",\"type\":\"" + type + "\",\"payload\":" + payload + "}";

    private static ShellMessage.Rejected Rejected(string json, string? source = Origin) =>
        Assert.IsType<ShellMessage.Rejected>(Gate.Evaluate(json, source));

    [Fact]
    public void Ready_from_shell_origin_is_accepted()
    {
        Assert.IsType<ShellMessage.Ready>(Gate.Evaluate(Message("ready"), Origin));
        Assert.IsType<ShellMessage.Ready>(Gate.Evaluate(Message("ready"), "http://localhost:5173/index.html?x=1"));
    }

    [Theory]
    [InlineData("launchApp", typeof(LaunchApp))]
    [InlineData("restartApp", typeof(RestartApp))]
    [InlineData("closeApp", typeof(CloseApp))]
    public void Intent_from_shell_origin_is_accepted(string type, Type expected)
    {
        var result = Gate.Evaluate(Message(type, "{\"appId\":\"netflix\"}"), Origin);

        var accepted = Assert.IsType<ShellMessage.IntentAccepted>(result);
        Assert.IsType(expected, accepted.Intent);
    }

    [Fact]
    public void Launch_intent_carries_app_id()
    {
        var accepted = Assert.IsType<ShellMessage.IntentAccepted>(
            Gate.Evaluate(Message("launchApp", "{\"appId\":\"netflix\"}"), Origin));

        Assert.Equal(new LaunchApp("netflix"), accepted.Intent);
    }

    [Theory]
    [InlineData("http://evil.example/")]
    [InlineData("http://localhost:5174/")]
    [InlineData("https://localhost:5173/")]
    [InlineData("http://localhost:5173.evil.example/")]
    [InlineData("http://localhost.evil.example:5173/")]
    [InlineData("not a uri")]
    [InlineData("")]
    [InlineData(null)]
    public void Foreign_or_unreadable_origin_is_rejected_for_any_type(string? source)
    {
        Assert.Equal("ready", Rejected(Message("ready"), source).Type);
        Assert.Equal("launchApp", Rejected(Message("launchApp", "{\"appId\":\"netflix\"}"), source).Type);
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("action")]
    [InlineData("showShell")]
    [InlineData("nope")]
    public void Types_other_than_ready_and_intents_are_rejected(string type)
    {
        var rejected = Rejected(Message(type, "{\"appId\":\"netflix\"}"));

        Assert.Equal(type, rejected.Type);
        Assert.Equal(1, rejected.V);
    }

    [Fact]
    public void Wrong_version_is_rejected()
    {
        Assert.Equal(2, Rejected(Message("ready", v: 2)).V);
        Assert.Equal(2, Rejected(Message("launchApp", "{\"appId\":\"x\"}", v: 2)).V);
    }

    [Theory]
    [InlineData("{\"appId\":\"\"}")]
    [InlineData("{\"appId\":null}")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"netflix\"")]
    [InlineData("[]")]
    [InlineData("{\"appId\":5}")]
    public void Bad_intent_payload_is_rejected(string payload)
    {
        Assert.Equal("launchApp", Rejected(Message("launchApp", payload)).Type);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"v\":1,\"type\":\"ready\"}")]
    public void Bad_json_is_rejected(string json)
    {
        Assert.IsType<ShellMessage.Rejected>(Gate.Evaluate(json, Origin));
    }
}
