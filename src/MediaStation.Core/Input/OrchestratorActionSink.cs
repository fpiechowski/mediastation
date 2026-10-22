using System;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Input;

/// <summary>
/// Konsument Actions z wątku Input (AD-8): każde <see cref="Deliver"/> zostawia wpis
/// <c>action {kind, phase, source}</c> przez <see cref="LogActionSink"/> i wkłada ten sam komunikat do kolejki
/// Orchestratora bez czekania. Wyjątek <c>post</c> nie jest łapany tutaj — <c>ActionPump</c> melduje go jako
/// <c>inputHubError {operation: actionSink}</c> i dostarcza kolejne Actions. <see cref="Report"/> należy wyłącznie
/// do logu, bo porażki huba nie są komunikatami Orchestratora.
/// </summary>
public sealed class OrchestratorActionSink : IActionSink, IInputHubErrorSink
{
    private readonly LogActionSink _log;
    private readonly Action<IOrchestratorMessage> _post;

    public OrchestratorActionSink(ILog log, Action<IOrchestratorMessage> post)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(post);

        _log = new LogActionSink(log);
        _post = post;
    }

    /// <summary>Loguje jedną Action i wkłada ją do kolejki Orchestratora w tej samej kolejności.</summary>
    public void Deliver(ActionMessage action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _log.Deliver(action);
        _post(action);
    }

    /// <summary>Loguje porażkę huba; nie wkłada nic do kolejki Orchestratora.</summary>
    public void Report(InputHubError error) => _log.Report(error);
}
