using System.Text.Json;
using MediaStation.App.Platform.Input;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>
/// Adapter tylko-log routingu wejścia: <c>inputRouting {cmdId, mode, appType, pointer, epoch}</c> z enumami
/// camelCase, bez efektu ubocznego i bez <c>CommandCompleted</c>. <c>appType</c> jest pominięty, gdy jest <c>null</c>.
/// </summary>
public class LogInputRoutingPortTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Shell_mode_logs_cmd_id_mode_pointer_and_epoch_without_app_type()
    {
        var log = new RecordingLog();
        new LogInputRoutingPort(log).Execute(new SetInputRouting(1, InputMode.Shell, null, PointerPolicy.Blocked, 1));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal(LogInputRoutingPort.Src, entry.Src);
        Assert.Equal("inputRouting", entry.Msg);
        Assert.Equal(1L, entry.Data!["cmdId"]);
        Assert.Equal(InputMode.Shell, entry.Data["mode"]);
        Assert.Equal(PointerPolicy.Blocked, entry.Data["pointer"]);
        Assert.Equal(1L, entry.Data["epoch"]);
        Assert.False(entry.Data.ContainsKey("appType"));

        var data = FormatData(entry);
        Assert.Equal(["cmdId", "mode", "pointer", "epoch"], data.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("shell", data.GetProperty("mode").GetString());
        Assert.Equal("blocked", data.GetProperty("pointer").GetString());
        Assert.Equal(1, data.GetProperty("epoch").GetInt64());
        Assert.False(data.TryGetProperty("appType", out _));
    }

    [Fact]
    public void App_mode_logs_app_type_between_mode_and_pointer()
    {
        var log = new RecordingLog();
        new LogInputRoutingPort(log).Execute(new SetInputRouting(4, InputMode.App, AppType.Web, PointerPolicy.Blocked, 2));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(AppType.Web, entry.Data!["appType"]);

        var data = FormatData(entry);
        Assert.Equal(
            ["cmdId", "mode", "appType", "pointer", "epoch"],
            data.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("app", data.GetProperty("mode").GetString());
        Assert.Equal("web", data.GetProperty("appType").GetString());
        Assert.Equal("blocked", data.GetProperty("pointer").GetString());
        Assert.Equal(2, data.GetProperty("epoch").GetInt64());
    }

    private static JsonElement FormatData(LogEntry entry)
    {
        var line = JsonLinesLog.Format(T0, entry.Level, entry.Src, entry.Msg, entry.Data, null);
        return JsonDocument.Parse(line).RootElement.GetProperty("data");
    }
}
