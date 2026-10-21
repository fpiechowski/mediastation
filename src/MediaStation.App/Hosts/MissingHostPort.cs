using System;
using System.Collections.Generic;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;

namespace MediaStation.App.Hosts;

/// <summary>
/// Adapter „brak hosta” do czasu epik hostów: <see cref="StartApp"/> i <see cref="StopApp"/> nie robią niczego,
/// tylko logują <c>startWithoutHost</c>/<c>stopWithoutHost</c> i odpowiadają <see cref="CommandFailed"/> z tym samym
/// <c>cmdId</c> (AD-4). Composition root stawia po jednym adapterze na <see cref="AppType"/>, żeby epiki hostów
/// podmieniały osobno.
/// </summary>
public sealed class MissingHostPort : IAppHostPort
{
    /// <summary>Źródło wpisów logu adaptera hosta.</summary>
    public const string Src = "host";

    private readonly ILog _log;
    private readonly Action<IOrchestratorMessage> _post;

    public MissingHostPort(ILog log, Action<IOrchestratorMessage> post)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(post);

        _log = log;
        _post = post;
    }

    /// <summary>Loguje jawny fakt braku hosta startu i wkłada wynik komendy do kolejki Orchestratora; nie rzuca.</summary>
    public void Start(StartApp command)
    {
        ArgumentNullException.ThrowIfNull(command);

        _log.Warn(Src, "startWithoutHost", new Dictionary<string, object?>
        {
            ["cmdId"] = command.CmdId,
            ["appId"] = command.AppId,
        });

        _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
    }

    /// <summary>Loguje jawny fakt braku hosta stopu i wkłada wynik komendy do kolejki Orchestratora; nie rzuca.</summary>
    public void Stop(StopApp command)
    {
        ArgumentNullException.ThrowIfNull(command);

        _log.Warn(Src, "stopWithoutHost", new Dictionary<string, object?>
        {
            ["cmdId"] = command.CmdId,
            ["appId"] = command.AppId,
        });

        _post(new CommandFailed(command.CmdId, ErrorCode.Unexpected));
    }
}
