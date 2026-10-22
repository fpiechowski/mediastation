namespace MediaStation.Core.Contracts;

/// <summary>Tryb wejścia, którego właścicielem jest Orchestrator (AD-3, AD-8).</summary>
public enum InputMode
{
    Shell,
    App,
    Osk,
}

/// <summary>Typ aplikacji z <c>apps.json</c> (AD-11); wyznacza wiersz tabeli routingu (AD-8).</summary>
public enum AppType
{
    Web,
    Moonlight,
    Native,
}

/// <summary>Polityka myszy i żyroskopu w bieżącym trybie (AD-8).</summary>
public enum PointerPolicy
{
    Blocked,
    Allowed,
}

/// <summary>
/// Komenda Orchestratora do InputHub: nowa lokalna kopia trybu routingu (AD-8).
/// Hook decyduje według ostatniej kopii, bez czekania na Orchestrator.
/// </summary>
/// <param name="AppType">Typ aktywnej aplikacji albo <c>null</c>, gdy żadna nie jest aktywna.</param>
/// <param name="Epoch">Monotoniczny numer decyzji routingu.</param>
/// <param name="ClipHwnd">
/// Uchwyt okna klawiatury ekranowej, do którego InputHub więzi kursor (<c>ClipCursor</c>) w trybie <c>osk</c>;
/// <c>null</c> albo <c>0</c> znaczy brak ograniczenia. Opcjonalny na końcu, żeby kod epiki 2 kompilował się
/// bez zmian; wysyła go dopiero epic-osk.
/// </param>
public sealed record SetInputRouting(
    long CmdId,
    InputMode Mode,
    AppType? AppType,
    PointerPolicy Pointer,
    long Epoch,
    long? ClipHwnd = null)
    : Command(CmdId);
