using System;
using System.Collections.Concurrent;

namespace MediaStation.Core.Logging;

/// <summary>
/// Znaczniki „ten wyjątek jest już zalogowany” dla dwóch handlerów UI: Dispatchera (mark) i AppDomain (consume).
/// Wątek UI oznacza wyjątek, a AppDomain zabiera znacznik tylko raz, więc jeden wyjątek daje jeden wpis logu.
/// </summary>
public sealed class DispatcherExceptionLog
{
    private readonly ConcurrentDictionary<Exception, byte> _marked = new();

    /// <summary>Oznacza wyjątek jako zalogowany przez Dispatcher; powtórne oznaczenie nic nie zmienia.</summary>
    public void Mark(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _marked.TryAdd(exception, 0);
    }

    /// <summary>
    /// Zabiera znacznik wyjątku. <c>true</c> tylko dla pierwszego wywołania po <see cref="Mark"/>;
    /// różne wyjątki mają niezależne znaczniki.
    /// </summary>
    public bool TryConsume(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return _marked.TryRemove(exception, out _);
    }
}
