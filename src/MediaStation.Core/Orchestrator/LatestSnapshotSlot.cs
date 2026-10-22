using System;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Orchestrator;

/// <summary>
/// Slot publikacji snapshotów (AD-14): trzyma tylko najnowszy snapshot i co najwyżej jedno zaplanowane
/// opróżnienie, więc zawieszony UI nie tworzy kolejki, a wątek Orchestratora nigdy nie czeka na UI.
/// </summary>
public sealed class LatestSnapshotSlot
{
    private readonly object _gate = new();
    private ShellSnapshot _latest;
    private ShellSnapshot? _pending;
    private bool _flushScheduled;

    public LatestSnapshotSlot(ShellSnapshot initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _latest = initial;
    }

    /// <summary>
    /// Zapisuje snapshot jako najnowszy. Zwraca <c>true</c> tylko wtedy, gdy wywołujący ma zaplanować
    /// opróżnienie; kolejne wywołania przed <see cref="TakePending"/> tylko podmieniają snapshot.
    /// </summary>
    public bool Publish(ShellSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            _latest = snapshot;
            _pending = snapshot;
            if (_flushScheduled)
            {
                return false;
            }

            _flushScheduled = true;
            return true;
        }
    }

    /// <summary>Najnowszy snapshot, także przed pierwszym opróżnieniem; używany przez handshake <c>ready</c>.</summary>
    public ShellSnapshot Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>Zabiera snapshot do wysłania; <c>null</c>, gdy nic nie czeka. Znosi zaplanowane opróżnienie.</summary>
    public ShellSnapshot? TakePending()
    {
        lock (_gate)
        {
            var pending = _pending;
            _pending = null;
            _flushScheduled = false;
            return pending;
        }
    }
}
