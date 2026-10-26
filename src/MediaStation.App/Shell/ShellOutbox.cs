using MediaStation.Core.Contracts;
using MediaStation.Core.Orchestrator;

namespace MediaStation.App.Shell;

/// <summary>Jedna dostawa z <see cref="ShellOutbox.Drain"/>, w kolejności wysyłki.</summary>
public abstract record ShellDelivery
{
    /// <summary>Snapshot do wysłania (latest-wins).</summary>
    public sealed record SnapshotDelivery(ShellSnapshot Snapshot) : ShellDelivery;

    /// <summary><c>action</c> do wysłania.</summary>
    public sealed record ActionDelivery(Action Action) : ShellDelivery;

    /// <summary><c>action</c> odrzucona (<see cref="ShellOutbox.NotReady"/> albo <see cref="ShellOutbox.Stale"/>), bez buforowania.</summary>
    public sealed record DroppedAction(Action Action, string Reason) : ShellDelivery;
}

/// <summary>
/// Kolejka host → UI (AD-14, AD-16) bez WebView2 i Dispatchera: snapshot latest-wins oraz Actions w kolejności FIFO.
/// <c>Publish*</c> woła wątek Orchestratora (tylko pod lockiem, bez czekania na UI) i zwraca, czy trzeba zaplanować
/// <see cref="Drain"/> na wątku UI. Oczekujący snapshot idzie przed Actions, więc Action jest oceniana na stanie
/// z kroku lub nowszym. Action jest odrzucana, gdy UI nie zgłosił <c>ready</c> (od startu albo od ostatniej
/// nawigacji) lub jest starsza niż <see cref="MaxActionAgeMs"/>; nic nie jest buforowane do późniejszego <c>ready</c>.
/// </summary>
public sealed class ShellOutbox
{
    /// <summary>Powód odrzucenia: brak <c>ready</c> od startu lub od ostatniej nawigacji.</summary>
    public const string NotReady = "notReady";

    /// <summary>Powód odrzucenia: Action starsza niż <see cref="MaxActionAgeMs"/>.</summary>
    public const string Stale = "stale";

    /// <summary>Maksymalny wiek Action w chwili wysyłki.</summary>
    public const long MaxActionAgeMs = 1000;

    private readonly object _gate = new();
    private readonly LatestSnapshotSlot _slot;
    private readonly Func<long> _nowMs;
    private readonly Queue<Action> _actions = new();
    private bool _actionFlushScheduled;
    private bool _ready;

    /// <param name="initial">Snapshot startowy, odsyłany na <c>ready</c> przed pierwszą publikacją.</param>
    /// <param name="nowMs">
    /// Zegar w milisekundach o tej samej skali co <see cref="Action.TsMono"/> (ms od startu systemu, jak <c>time</c> hooka LL).
    /// Wiek liczony jest modulo 2^32, bo <c>TsMono</c> pochodzi z 32-bitowego licznika.
    /// </param>
    public ShellOutbox(ShellSnapshot initial, Func<long> nowMs)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(nowMs);

        _slot = new LatestSnapshotSlot(initial);
        _nowMs = nowMs;
    }

    /// <summary>Najnowszy snapshot, także przed pierwszą publikacją; odpowiedź na <c>ready</c>.</summary>
    public ShellSnapshot Latest => _slot.Latest;

    /// <summary>Zapisuje najnowszy snapshot; <c>true</c>, gdy wywołujący ma zaplanować <see cref="Drain"/>.</summary>
    public bool PublishSnapshot(ShellSnapshot snapshot) => _slot.Publish(snapshot);

    /// <summary>Dokłada Action na koniec kolejki; <c>true</c>, gdy wywołujący ma zaplanować <see cref="Drain"/>.</summary>
    public bool PublishAction(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_gate)
        {
            _actions.Enqueue(action);
            if (_actionFlushScheduled)
            {
                return false;
            }

            _actionFlushScheduled = true;
            return true;
        }
    }

    /// <summary>UI zgłosiło <c>ready</c>: od teraz Actions są dostarczane.</summary>
    public void MarkReady()
    {
        lock (_gate)
        {
            _ready = true;
        }
    }

    /// <summary>Nawigacja strony (<c>NavigationStarting</c>): Actions są odrzucane do kolejnego <c>ready</c>.</summary>
    public void MarkNavigated()
    {
        lock (_gate)
        {
            _ready = false;
        }
    }

    /// <summary>Zabiera wszystko, co czeka: najpierw snapshot, potem Actions w kolejności publikacji.</summary>
    public IReadOnlyList<ShellDelivery> Drain()
    {
        // Kolejność jest istotna: Orchestrator publikuje snapshot kroku przed jego Action, więc Actions zabrane
        // jako pierwsze mają snapshot swojego kroku już w slocie (albo już dostarczony). Odwrotna kolejność
        // pozwoliłaby wyprzedzić snapshot przez Action opublikowane między tymi dwoma krokami.
        Action[] actions;
        bool ready;
        lock (_gate)
        {
            actions = [.. _actions];
            _actions.Clear();
            _actionFlushScheduled = false;
            ready = _ready;
        }

        var deliveries = new List<ShellDelivery>();
        if (_slot.TakePending() is { } snapshot)
        {
            deliveries.Add(new ShellDelivery.SnapshotDelivery(snapshot));
        }

        var now = _nowMs();
        foreach (var action in actions)
        {
            if (!ready)
            {
                deliveries.Add(new ShellDelivery.DroppedAction(action, NotReady));
            }
            else if (IsStale(now, action.TsMono))
            {
                deliveries.Add(new ShellDelivery.DroppedAction(action, Stale));
            }
            else
            {
                deliveries.Add(new ShellDelivery.ActionDelivery(action));
            }
        }

        return deliveries;
    }

    /// <summary>
    /// Wiek modulo 2^32: <c>TsMono</c> może mieć inną liczbę „obiegów” licznika niż zegar hosta (hub startuje
    /// z <c>high = 0</c>), a różnica 32-bitowa jest na to odporna. Wiek ≥ 2^31 to Action „z przyszłości”, nie stara.
    /// </summary>
    private static bool IsStale(long now, long tsMono)
    {
        var age = unchecked((uint)(now - tsMono));
        return age > MaxActionAgeMs && age < 0x8000_0000;
    }
}
