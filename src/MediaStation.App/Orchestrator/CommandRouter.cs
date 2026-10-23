using System;
using System.Collections.Generic;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Orchestrator;

/// <summary>
/// Jedno wyjście Orchestratora do wykonawców (decyzja epiki): <see cref="StartApp"/>, <see cref="StopApp"/>,
/// <see cref="ProbeExisting"/> i <see cref="RenewSession"/> trafiają do hosta wybranego po <see cref="AppType"/>
/// z mapy <c>appId → AppType</c> z configu, komendy okienkowe do <see cref="IWindowManagerPort"/>, komendy timerów
/// do <see cref="ITimerPort"/>, <see cref="SendKey"/> do <see cref="IKeyInjectionPort"/>, a <see cref="SetInputRouting"/>
/// do <see cref="IInputRoutingPort"/>. Komenda bez portu i komenda dla nieznanej aplikacji kończą się wpisem
/// <c>warn</c>, nigdy wyjątkiem.
/// </summary>
public sealed class CommandRouter
{
    private const string Src = "orchestrator";

    private readonly IReadOnlyDictionary<string, AppType> _appTypes;
    private readonly IReadOnlyDictionary<AppType, IAppHostPort> _hosts;
    private readonly IWindowManagerPort _windowManager;
    private readonly IInputRoutingPort _inputRouting;
    private readonly IKeyInjectionPort _keyInjection;
    private readonly ITimerPort _timers;
    private readonly ILog _log;

    public CommandRouter(
        IReadOnlyDictionary<string, AppType> appTypes,
        IReadOnlyDictionary<AppType, IAppHostPort> hosts,
        IWindowManagerPort windowManager,
        IInputRoutingPort inputRouting,
        IKeyInjectionPort keyInjection,
        ITimerPort timers,
        ILog log)
    {
        ArgumentNullException.ThrowIfNull(appTypes);
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(windowManager);
        ArgumentNullException.ThrowIfNull(inputRouting);
        ArgumentNullException.ThrowIfNull(keyInjection);
        ArgumentNullException.ThrowIfNull(timers);
        ArgumentNullException.ThrowIfNull(log);

        _appTypes = appTypes;
        _hosts = hosts;
        _windowManager = windowManager;
        _inputRouting = inputRouting;
        _keyInjection = keyInjection;
        _timers = timers;
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
                    if (TryGetHost(start.CmdId, start.AppId, out var startHost))
                    {
                        startHost.Start(start);
                    }

                    break;
                case StopApp stop:
                    if (TryGetHost(stop.CmdId, stop.AppId, out var stopHost))
                    {
                        stopHost.Stop(stop);
                    }

                    break;
                case ProbeExisting probe:
                    if (TryGetHost(probe.CmdId, probe.AppId, out var probeHost))
                    {
                        probeHost.Probe(probe);
                    }

                    break;
                case RenewSession renew:
                    if (TryGetHost(renew.CmdId, renew.AppId, out var renewHost))
                    {
                        renewHost.Renew(renew);
                    }

                    break;
                case WindowCommand window:
                    _windowManager.Execute(window);
                    break;
                case TimerCommand timer:
                    _timers.Execute(timer);
                    break;
                case SetInputRouting routing:
                    _inputRouting.Execute(routing);
                    break;
                case SetManagedWindows managed:
                    _inputRouting.Execute(managed);
                    break;
                case SendKey key:
                    _keyInjection.Execute(key);
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

    /// <summary>
    /// Wspólne wyjście do hosta dla komend aplikacji: nieznana aplikacja (<c>commandForUnknownApp</c>)
    /// i brak hosta dla jej typu (<c>commandWithoutPort</c>) to <c>warn</c> bez wyjątku.
    /// </summary>
    private bool TryGetHost(long cmdId, string appId, out IAppHostPort host)
    {
        if (!_appTypes.TryGetValue(appId, out var appType))
        {
            _log.Warn(Src, "commandForUnknownApp", new Dictionary<string, object?>
            {
                ["cmdId"] = cmdId,
                ["appId"] = appId,
            });
            host = null!;
            return false;
        }

        if (!_hosts.TryGetValue(appType, out var resolved))
        {
            _log.Warn(Src, "commandWithoutPort", new Dictionary<string, object?>
            {
                ["cmdId"] = cmdId,
                ["appId"] = appId,
                ["appType"] = appType,
            });
            host = null!;
            return false;
        }

        host = resolved;
        return true;
    }
}
