using System;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Input;

/// <summary>
/// Lokalna kopia trybu routingu (AD-8): pełny wiersz tabeli razem z <c>epoch</c>, po którym polityka
/// rozstrzyga połknięcie klawisza bez czekania na Orchestrator.
/// </summary>
/// <param name="AppType">Typ aktywnej aplikacji albo <c>null</c>, gdy żadna nie jest aktywna.</param>
/// <param name="Epoch">Monotoniczny numer decyzji routingu; rośnie tylko w Orchestratorze.</param>
public sealed record InputRouting(InputMode Mode, AppType? AppType, PointerPolicy Pointer, long Epoch);

/// <summary>
/// Kopia lokalna routingu wejścia (AD-8): stan początkowy to <c>shell</c> z zablokowanym wskaźnikiem i
/// <c>epoch</c> 0, a <see cref="Apply"/> przyjmuje wyłącznie <c>epoch</c> ściśle większy od bieżącego.
/// Wątek Orchestratora pisze, wątek Input czyta; <c>lock</c> obejmuje jedno przypisanie referencji, więc
/// czytelnik nigdy nie czeka na Orchestrator.
/// </summary>
public sealed class InputRoutingState
{
    private readonly object _gate = new();
    private InputRouting _current = new(InputMode.Shell, null, PointerPolicy.Blocked, 0);

    /// <summary>Bieżący wiersz routingu; odczyt pod <c>lock</c> widzi całą decyzję albo poprzednią.</summary>
    public InputRouting Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Zapisuje nową kopię, gdy <c>epoch</c> jest ściśle większy od bieżącego i zwraca <c>true</c>.
    /// Dla <c>epoch</c> równego albo starszego kopia zostaje bez zmian, a metoda zwraca <c>false</c>.
    /// </summary>
    public bool Apply(SetInputRouting command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            if (command.Epoch <= _current.Epoch)
            {
                return false;
            }

            _current = new InputRouting(command.Mode, command.AppType, command.Pointer, command.Epoch);
            return true;
        }
    }
}
