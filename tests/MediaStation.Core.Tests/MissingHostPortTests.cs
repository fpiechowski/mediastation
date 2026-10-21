using MediaStation.App.Hosts;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>
/// Adapter „brak hosta”: <c>StartApp</c> i <c>StopApp</c> nie robią niczego, zostawiają
/// <c>startWithoutHost</c>/<c>stopWithoutHost</c> w logu i wkładają <c>CommandFailed{same cmdId, Unexpected}</c>
/// do kolejki bez wyjątku przez granicę portu.
/// </summary>
public class MissingHostPortTests
{
    [Fact]
    public void Start_logs_and_posts_command_failed_with_same_cmd_id()
    {
        var log = new RecordingLog();
        var posted = new List<IOrchestratorMessage>();
        var port = new MissingHostPort(log, posted.Add);

        port.Start(new StartApp(7, "netflix"));

        var failed = Assert.IsType<CommandFailed>(Assert.Single(posted));
        Assert.Equal(7, failed.CmdId);
        Assert.Equal(ErrorCode.Unexpected, failed.ErrorCode);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal(MissingHostPort.Src, entry.Src);
        Assert.Equal("startWithoutHost", entry.Msg);
        Assert.Equal(7L, entry.Data!["cmdId"]);
        Assert.Equal("netflix", entry.Data["appId"]);
    }

    [Fact]
    public void Stop_logs_and_posts_command_failed_with_same_cmd_id()
    {
        var log = new RecordingLog();
        var posted = new List<IOrchestratorMessage>();
        var port = new MissingHostPort(log, posted.Add);

        port.Stop(new StopApp(9, "netflix"));

        var failed = Assert.IsType<CommandFailed>(Assert.Single(posted));
        Assert.Equal(9, failed.CmdId);
        Assert.Equal(ErrorCode.Unexpected, failed.ErrorCode);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal(MissingHostPort.Src, entry.Src);
        Assert.Equal("stopWithoutHost", entry.Msg);
        Assert.Equal(9L, entry.Data!["cmdId"]);
        Assert.Equal("netflix", entry.Data["appId"]);
    }
}
