using MediaStation.App.Orchestrator;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.Core.Tests;

/// <summary>
/// Jedno wyjście Orchestratora: komenda trafia do portu po typie, a <c>StartApp</c>, <c>StopApp</c>,
/// <c>ProbeExisting</c> i <c>RenewSession</c> do hosta po <see cref="AppType"/> z mapy <c>appId → AppType</c>.
/// Brak portu i nieznana aplikacja to <c>warn</c>, nie wyjątek.
/// </summary>
public class CommandRouterTests
{
    [Fact]
    public void Start_app_goes_to_host_of_its_app_type()
    {
        var (router, hosts, _, _, _, _, _) = CreateRouter();

        router.Dispatch([new StartApp(1, "netflix"), new StartApp(2, "plex"), new StartApp(3, "moonlight-pc")]);

        Assert.Equal([new StartApp(1, "netflix")], hosts[AppType.Web].Started);
        Assert.Equal([new StartApp(2, "plex")], hosts[AppType.Native].Started);
        Assert.Equal([new StartApp(3, "moonlight-pc")], hosts[AppType.Moonlight].Started);
    }

    [Fact]
    public void Stop_app_goes_to_host_of_its_app_type()
    {
        var (router, hosts, _, _, _, _, _) = CreateRouter();

        router.Dispatch([new StopApp(1, "netflix"), new StopApp(2, "plex"), new StopApp(3, "moonlight-pc")]);

        Assert.Equal([new StopApp(1, "netflix")], hosts[AppType.Web].Stopped);
        Assert.Equal([new StopApp(2, "plex")], hosts[AppType.Native].Stopped);
        Assert.Equal([new StopApp(3, "moonlight-pc")], hosts[AppType.Moonlight].Stopped);
    }

    [Fact]
    public void Probe_existing_goes_to_host_of_its_app_type()
    {
        var (router, hosts, _, _, _, _, _) = CreateRouter();

        router.Dispatch([
            new ProbeExisting(1, "netflix"),
            new ProbeExisting(2, "plex"),
            new ProbeExisting(3, "moonlight-pc"),
        ]);

        Assert.Equal([new ProbeExisting(1, "netflix")], hosts[AppType.Web].Probed);
        Assert.Equal([new ProbeExisting(2, "plex")], hosts[AppType.Native].Probed);
        Assert.Equal([new ProbeExisting(3, "moonlight-pc")], hosts[AppType.Moonlight].Probed);
    }

    [Fact]
    public void Probe_existing_for_unknown_app_logs_warn_without_throwing()
    {
        var (router, hosts, _, _, _, _, log) = CreateRouter();

        var exception = Record.Exception(() => router.Dispatch([new ProbeExisting(13, "nope")]));

        Assert.Null(exception);
        Assert.All(hosts.Values, host => Assert.Empty(host.Probed));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("commandForUnknownApp", entry.Msg);
        Assert.Equal(13L, entry.Data!["cmdId"]);
        Assert.Equal("nope", entry.Data["appId"]);
    }

    [Fact]
    public void Renew_session_goes_to_host_of_its_app_type()
    {
        var (router, hosts, _, _, _, _, _) = CreateRouter();

        router.Dispatch([
            new RenewSession(1, "netflix"),
            new RenewSession(2, "plex"),
            new RenewSession(3, "moonlight-pc"),
        ]);

        Assert.Equal([new RenewSession(1, "netflix")], hosts[AppType.Web].Renewed);
        Assert.Equal([new RenewSession(2, "plex")], hosts[AppType.Native].Renewed);
        Assert.Equal([new RenewSession(3, "moonlight-pc")], hosts[AppType.Moonlight].Renewed);
    }

    [Fact]
    public void Renew_session_for_unknown_app_logs_warn_without_throwing()
    {
        var (router, hosts, _, _, _, _, log) = CreateRouter();

        var exception = Record.Exception(() => router.Dispatch([new RenewSession(14, "nope")]));

        Assert.Null(exception);
        Assert.All(hosts.Values, host => Assert.Empty(host.Renewed));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("commandForUnknownApp", entry.Msg);
        Assert.Equal(14L, entry.Data!["cmdId"]);
        Assert.Equal("nope", entry.Data["appId"]);
    }

    [Fact]
    public void Window_commands_go_to_window_manager()
    {
        var (router, _, window, _, _, _, _) = CreateRouter();

        router.Dispatch([new ActivateWindow(4, 6699), new MinimizeWindow(5, 6699), new ShowShell(6)]);

        Assert.Equal<WindowCommand>(
            [new ActivateWindow(4, 6699), new MinimizeWindow(5, 6699), new ShowShell(6)],
            window.Executed);
    }

    [Fact]
    public void Input_routing_goes_to_input_port()
    {
        var (router, _, _, input, _, _, _) = CreateRouter();
        var routing = new SetInputRouting(7, InputMode.App, AppType.Web, PointerPolicy.Blocked, 2);

        router.Dispatch([routing]);

        Assert.Equal([routing], input.Executed);
    }

    [Fact]
    public void Send_key_goes_to_key_injection_port()
    {
        var (router, _, window, input, keyInjection, timers, log) = CreateRouter();
        var command = new SendKey(15, 172);

        router.Dispatch([command]);

        Assert.Equal([command], keyInjection.Executed);
        Assert.Empty(window.Executed);
        Assert.Empty(input.Executed);
        Assert.Empty(timers.Executed);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Timer_commands_go_to_timer_port()
    {
        var (router, _, _, _, _, timers, _) = CreateRouter();
        var schedule = new ScheduleTimer(13, 42, "netflix", new DateTimeOffset(2026, 10, 22, 12, 0, 30, TimeSpan.Zero));
        var cancel = new CancelTimer(14, 42);

        router.Dispatch([schedule, cancel]);

        Assert.Equal<TimerCommand>([schedule, cancel], timers.Executed);
    }

    [Fact]
    public void Start_app_for_unknown_app_logs_warn_without_throwing()
    {
        var (router, hosts, _, _, _, _, log) = CreateRouter();

        router.Dispatch([new StartApp(8, "nope")]);

        Assert.All(hosts.Values, host => Assert.Empty(host.Started));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("commandForUnknownApp", entry.Msg);
        Assert.Equal(8L, entry.Data!["cmdId"]);
        Assert.Equal("nope", entry.Data["appId"]);
    }

    [Fact]
    public void Start_app_for_known_app_without_host_logs_warn_without_throwing()
    {
        var log = new RecordingLog();
        var web = new FakeHost();
        var router = new CommandRouter(
            new Dictionary<string, AppType> { ["netflix"] = AppType.Web, ["plex"] = AppType.Native },
            new Dictionary<AppType, IAppHostPort> { [AppType.Web] = web },
            new FakeWindowManager(),
            new FakeInputRouting(),
            new FakeKeyInjection(),
            new FakeTimerPort(),
            log);

        router.Dispatch([new StartApp(9, "plex")]);

        Assert.Empty(web.Started);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("commandWithoutPort", entry.Msg);
        Assert.Equal(9L, entry.Data!["cmdId"]);
        Assert.Equal("plex", entry.Data["appId"]);
        Assert.Equal(AppType.Native, entry.Data["appType"]);
    }

    [Fact]
    public void Stop_app_for_unknown_app_logs_warn_without_throwing()
    {
        var (router, hosts, _, _, _, _, log) = CreateRouter();

        router.Dispatch([new StopApp(11, "nope")]);

        Assert.All(hosts.Values, host => Assert.Empty(host.Stopped));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("commandForUnknownApp", entry.Msg);
        Assert.Equal(11L, entry.Data!["cmdId"]);
        Assert.Equal("nope", entry.Data["appId"]);
    }

    [Fact]
    public void Stop_app_for_known_app_without_host_logs_warn_without_throwing()
    {
        var log = new RecordingLog();
        var web = new FakeHost();
        var router = new CommandRouter(
            new Dictionary<string, AppType> { ["netflix"] = AppType.Web, ["plex"] = AppType.Native },
            new Dictionary<AppType, IAppHostPort> { [AppType.Web] = web },
            new FakeWindowManager(),
            new FakeInputRouting(),
            new FakeKeyInjection(),
            new FakeTimerPort(),
            log);

        router.Dispatch([new StopApp(12, "plex")]);

        Assert.Empty(web.Stopped);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("commandWithoutPort", entry.Msg);
        Assert.Equal(12L, entry.Data!["cmdId"]);
        Assert.Equal("plex", entry.Data["appId"]);
        Assert.Equal(AppType.Native, entry.Data["appType"]);
    }

    [Fact]
    public void Command_without_port_logs_warn_without_throwing()
    {
        var (router, hosts, window, input, _, _, log) = CreateRouter();

        var exception = Record.Exception(() => router.Dispatch([new UnknownCommand(10)]));

        Assert.Null(exception);
        Assert.All(hosts.Values, host => Assert.Empty(host.Started));
        Assert.Empty(window.Executed);
        Assert.Empty(input.Executed);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("commandWithoutPort", entry.Msg);
        Assert.Equal(10L, entry.Data!["cmdId"]);
        Assert.Equal(nameof(UnknownCommand), entry.Data["type"]);
    }

    private static (CommandRouter Router, Dictionary<AppType, FakeHost> Hosts, FakeWindowManager Window, FakeInputRouting Input, FakeKeyInjection KeyInjection, FakeTimerPort Timers, RecordingLog Log) CreateRouter()
    {
        var hosts = new Dictionary<AppType, FakeHost>
        {
            [AppType.Web] = new FakeHost(),
            [AppType.Moonlight] = new FakeHost(),
            [AppType.Native] = new FakeHost(),
        };
        var window = new FakeWindowManager();
        var input = new FakeInputRouting();
        var keyInjection = new FakeKeyInjection();
        var timers = new FakeTimerPort();
        var log = new RecordingLog();
        var router = new CommandRouter(
            new Dictionary<string, AppType>
            {
                ["netflix"] = AppType.Web,
                ["moonlight-pc"] = AppType.Moonlight,
                ["plex"] = AppType.Native,
            },
            hosts.ToDictionary(entry => entry.Key, entry => (IAppHostPort)entry.Value),
            window,
            input,
            keyInjection,
            timers,
            log);
        return (router, hosts, window, input, keyInjection, timers, log);
    }

    private sealed record UnknownCommand(long CmdId) : Command(CmdId);

    private sealed class FakeHost : IAppHostPort
    {
        public List<StartApp> Started { get; } = [];

        public List<StopApp> Stopped { get; } = [];

        public List<ProbeExisting> Probed { get; } = [];

        public List<RenewSession> Renewed { get; } = [];

        public void Start(StartApp command) => Started.Add(command);

        public void Stop(StopApp command) => Stopped.Add(command);

        public void Probe(ProbeExisting command) => Probed.Add(command);

        public void Renew(RenewSession command) => Renewed.Add(command);
    }

    private sealed class FakeWindowManager : IWindowManagerPort
    {
        public List<WindowCommand> Executed { get; } = [];

        public void Execute(WindowCommand command) => Executed.Add(command);
    }

    private sealed class FakeInputRouting : IInputRoutingPort
    {
        public List<SetInputRouting> Executed { get; } = [];

        public void Execute(SetInputRouting command) => Executed.Add(command);
    }

    private sealed class FakeKeyInjection : IKeyInjectionPort
    {
        public List<SendKey> Executed { get; } = [];

        public void Execute(SendKey command) => Executed.Add(command);
    }

    private sealed class FakeTimerPort : ITimerPort
    {
        public List<TimerCommand> Executed { get; } = [];

        public void Execute(TimerCommand command) => Executed.Add(command);
    }
}
