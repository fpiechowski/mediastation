using System;
using System.Collections.Generic;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Orchestrator;

/// <summary>
/// Jedno wyjście Orchestratora do wykonawców (decyzja epiki): <see cref="StartApp"/> trafia do hosta
/// wybranego po <see cref="AppType"/> z mapy <c>appId → AppType</c> z configu, komendy okienkowe do
/// <see cref="IWindowManagerPort"/>, a <see cref="SetInputRouting"/> do <see cref="IInputRoutingPort"/>.
/// Komenda bez portu i komenda dla nieznanej aplikacji kończą się wpisem <c>warn</c>, nigdy wyjątkiem.
/// </summary>
public sealed class CommandRouter
{
    private const string Src = "orchestrator";

    private readonly IReadOnlyDictionary<string, AppType> _appTypes;
    private readonly IReadOnlyDictionary<AppType, IAppHostPort> _hosts;
    private readonly IWindowManagerPort _windowManager;
    private readonly IInputRoutingPort _inputRouting;
    private readonly ILog _log;

    public CommandRouter(
        IReadOnlyDictionary<string, AppType> appTypes,
        IReadOnlyDictionary<AppType, IAppHostPort> hosts,
        IWindowManagerPort windowManager,
        IInputRoutingPort inputRouting,
        ILog log)
    {
        ArgumentNullException.ThrowIfNull(appTypes);
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(windowManager);
        ArgumentNullException.ThrowIfNull(inputRouting);
        ArgumentNullException.ThrowIfNull(log);

        _appTypes = appTypes;
        _hosts = hosts;
        _windowManager = windowManager;
        _inputRouting = inputRouting;
        _log = log;
    }

    /// <summary>Wysyła komendy z jednego kroku Orchestratora do portów po typie komendy i <see cref="AppType"/>.</summary>
    public void Dispatch(IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        foreach (var command in commands)
        {
            switch (command)
            {
                case StartApp start:
                    DispatchStart(start);
                    break;
                case WindowCommand window:
                    _windowManager.Execute(window);
                    break;
                case SetInputRouting routing:
                    _inputRouting.Execute(routing);
                    break;
                default:
                    _log.Warn(Src, "commandWithoutPort", new Dictionary<string, object?>
                    {
                        ["cmdId"] = command.CmdId,
                        ["type"] = command.GetType().Name,
                    });
                    break;
            }
        }
    }

    private void DispatchStart(StartApp start)
    {
        if (!_appTypes.TryGetValue(start.AppId, out var appType))
        {
            _log.Warn(Src, "commandForUnknownApp", new Dictionary<string, object?>
            {
                ["cmdId"] = start.CmdId,
                ["appId"] = start.AppId,
            });
            return;
        }

        if (!_hosts.TryGetValue(appType, out var host))
        {
            _log.Warn(Src, "commandWithoutPort", new Dictionary<string, object?>
            {
                ["cmdId"] = start.CmdId,
                ["appId"] = start.AppId,
                ["appType"] = appType,
            });
            return;
        }

        host.Start(start);
    }
}
