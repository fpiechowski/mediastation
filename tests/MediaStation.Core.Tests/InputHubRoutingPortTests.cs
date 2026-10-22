using System.Text.Json;
using MediaStation.App.Platform.Input;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>
/// Adapter routingu wejścia (AD-8): <c>inputRouting {cmdId, mode, appType, pointer, epoch}</c> z enumami
/// camelCase i bez <c>CommandCompleted</c>, zapis lokalnej kopii tylko dla nowszego <c>epoch</c> oraz
/// <c>warn inputRoutingStale {cmdId, epoch}</c> dla równego i starszego. <c>appType</c> jest pominięty,
/// gdy jest <c>null</c>.
/// </summary>
public class InputHubRoutingPortTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Shell_mode_logs_cmd_id_mode_pointer_and_epoch_without_app_type()
    {
        var log = new RecordingLog();
        new InputHubRoutingPort(new InputRoutingState(), log).Execute(new SetInputRouting(1, InputMode.Shell, null, PointerPolicy.Blocked, 1));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal(InputHubRoutingPort.Src, entry.Src);
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
        new InputHubRoutingPort(new InputRoutingState(), log).Execute(new SetInputRouting(4, InputMode.App, AppType.Web, PointerPolicy.Blocked, 2));

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

    [Fact]
    public void Newer_epoch_updates_the_local_copy_after_the_log_entry()
    {
        var log = new RecordingLog();
        var routing = new InputRoutingState();
        var port = new InputHubRoutingPort(routing, log);

        port.Execute(new SetInputRouting(4, InputMode.App, AppType.Moonlight, PointerPolicy.Allowed, 2));

        Assert.Equal(new InputRouting(InputMode.App, AppType.Moonlight, PointerPolicy.Allowed, 2), routing.Current);
        var entry = Assert.Single(log.Entries);
        Assert.Equal("inputRouting", entry.Msg);
        Assert.Equal(LogLevel.Info, entry.Level);
    }

    [Theory]
    [InlineData(3L)]
    [InlineData(2L)]
    public void Equal_or_older_epoch_keeps_the_copy_and_logs_a_warn(long epoch)
    {
        var log = new RecordingLog();
        var routing = new InputRoutingState();
        var port = new InputHubRoutingPort(routing, log);
        port.Execute(new SetInputRouting(1, InputMode.App, AppType.Moonlight, PointerPolicy.Allowed, 3));
        var before = routing.Current;

        port.Execute(new SetInputRouting(9, InputMode.Shell, null, PointerPolicy.Blocked, epoch));

        Assert.Equal(before, routing.Current);

        // Odrzucony `epoch` też zostawia wpis `inputRouting`, a po nim przychodzi `warn inputRoutingStale`.
        Assert.Equal(3, log.Entries.Count);
        Assert.Equal("inputRouting", log.Entries[0].Msg);
        Assert.Equal(LogLevel.Info, log.Entries[0].Level);
        Assert.Equal("inputRouting", log.Entries[1].Msg);
        Assert.Equal(epoch, Assert.IsType<long>(log.Entries[1].Data!["epoch"]));

        var stale = log.Entries[2];
        Assert.Equal(LogLevel.Warn, stale.Level);
        Assert.Equal(InputHubRoutingPort.Src, stale.Src);
        Assert.Equal("inputRoutingStale", stale.Msg);
        Assert.Equal(["cmdId", "epoch"], stale.Data!.Keys.ToArray());
        Assert.Equal(9L, Assert.IsType<long>(stale.Data["cmdId"]));
        Assert.Equal(epoch, Assert.IsType<long>(stale.Data["epoch"]));
    }

    [Fact]
    public void Clip_hwnd_reaches_the_local_copy_without_a_new_log_key()
    {
        var log = new RecordingLog();
        var routing = new InputRoutingState();

        new InputHubRoutingPort(routing, log).Execute(
            new SetInputRouting(5, InputMode.Osk, null, PointerPolicy.Allowed, 2, ClipHwnd: 0x1234));

        Assert.Equal(0x1234, routing.Current.ClipHwnd);
        var data = FormatData(Assert.Single(log.Entries));
        Assert.Equal(["cmdId", "mode", "pointer", "epoch"], data.EnumerateObject().Select(property => property.Name).ToArray());
    }

    private static JsonElement FormatData(LogEntry entry)
    {
        var line = JsonLinesLog.Format(T0, entry.Level, entry.Src, entry.Msg, entry.Data, null);
        return JsonDocument.Parse(line).RootElement.GetProperty("data");
    }
}
