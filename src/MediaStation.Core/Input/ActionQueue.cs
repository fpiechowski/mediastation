using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Input;

/// <summary>
/// Kolejka Actions z wątku Input do wątku, który je opróżnia (AD-2, AD-8). Bez ograniczenia rozmiaru:
/// zapis z callbacku hooka nigdy nie czeka i nigdy nie gubi Action, także Home. <c>wake</c> jest wołany po
/// każdym zapisie na wątku wołającego, więc czytelnik zbudzony nim widzi już Action; musi nie blokować i nie
/// rzucać (App daje nim sygnał dla wątku ze story 2, a wyjątek z callbacku trafiłby do hooka LL).
/// Wątkowo bezpieczna.
/// </summary>
public sealed class ActionQueue
{
    private readonly ConcurrentQueue<ActionMessage> _queue = new();
    private readonly Action? _wake;

    public ActionQueue(Action? wake = null)
    {
        _wake = wake;
    }

    /// <summary>Odkłada Action i budzi czytelnika; nigdy nie czeka i nie gubi Action.</summary>
    public void Enqueue(ActionMessage action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _queue.Enqueue(action);
        _wake?.Invoke();
    }

    /// <summary>Zabiera najstarszą Action w kolejności FIFO; <c>false</c>, gdy kolejka jest pusta.</summary>
    public bool TryDequeue([MaybeNullWhen(false)] out ActionMessage action) => _queue.TryDequeue(out action);
}
