using System.Text.Json;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>
/// Adapter tylko-log WindowManagera: <c>windowCommand {type, cmdId, hwnd}</c>, bez efektu ubocznego
/// i bez <c>CommandCompleted</c>. <c>hwnd</c> nie występuje dla <see cref="ShowShell"/>.
/// </summary>
public class LogWindowManagerPortTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Activate_and_minimize_log_type_cmd_id_and_hwnd()
    {
        var log = new RecordingLog();
        var port = new LogWindowManagerPort(log);

        port.Execute(new ActivateWindow(4, 6699));
        port.Execute(new MinimizeWindow(5, 6700));

        Assert.Collection(
            log.Entries,
            entry =>
            {
                Assert.Equal(LogLevel.Info, entry.Level);
                Assert.Equal(LogWindowManagerPort.Src, entry.Src);
                Assert.Equal("windowCommand", entry.Msg);
                Assert.Equal("ActivateWindow", entry.Data!["type"]);
                Assert.Equal(4L, entry.Data["cmdId"]);
                Assert.Equal(6699L, entry.Data["hwnd"]);
            },
            entry =>
            {
                Assert.Equal("MinimizeWindow", entry.Data!["type"]);
                Assert.Equal(5L, entry.Data["cmdId"]);
                Assert.Equal(6700L, entry.Data["hwnd"]);
            });
    }

    [Fact]
    public void Show_shell_log_has_no_hwnd_field()
    {
        var log = new RecordingLog();

        new LogWindowManagerPort(log).Execute(new ShowShell(6));

        var entry = Assert.Single(log.Entries);
        Assert.Equal("ShowShell", entry.Data!["type"]);
        Assert.Equal(6L, entry.Data["cmdId"]);
        Assert.False(entry.Data.ContainsKey("hwnd"));
    }

    [Fact]
    public void Log_line_serializes_window_command_data_as_json()
    {
        var log = new RecordingLog();
        new LogWindowManagerPort(log).Execute(new ActivateWindow(4, 6699));

        var data = FormatData(Assert.Single(log.Entries));

        Assert.Equal("ActivateWindow", data.GetProperty("type").GetString());
        Assert.Equal(4, data.GetProperty("cmdId").GetInt64());
        Assert.Equal(6699, data.GetProperty("hwnd").GetInt64());
        Assert.Equal(["type", "cmdId", "hwnd"], data.EnumerateObject().Select(property => property.Name).ToArray());
    }

    private static JsonElement FormatData(LogEntry entry)
    {
        var line = JsonLinesLog.Format(T0, entry.Level, entry.Src, entry.Msg, entry.Data, null);
        return JsonDocument.Parse(line).RootElement.GetProperty("data");
    }
}
