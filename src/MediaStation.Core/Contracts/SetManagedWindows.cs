namespace MediaStation.Core.Contracts;

/// <summary>Okno aplikacji zarządzane przez Orchestratora: uchwyt okna i PID korzenia drzewa procesów (AD-8).</summary>
/// <param name="Hwnd">Uchwyt okna z <see cref="AppWindowAttached.Hwnd"/>.</param>
/// <param name="RootPid">PID korzenia z <see cref="AppWindowAttached.RootPid"/>; fallback dopasowania okna.</param>
public sealed record ManagedWindow(long Hwnd, int RootPid);

/// <summary>
/// Komenda Orchestratora do InputHub: pełny zbiór okien, nad którymi klawiatura i pad są obsługiwane (AD-8,
/// bramka foreground). Hook decyduje według ostatniej kopii, bez czekania na Orchestrator. Uchwyt shella nie idzie
/// w komendzie: Core go nie zna, więc zapisuje go App (<c>ManagedWindows.SetShell</c>).
/// </summary>
/// <param name="OskHwnd">Uchwyt okna klawiatury ekranowej albo <c>null</c>, gdy jej nie ma.</param>
/// <param name="Apps">Okna aplikacji z <c>AppWindowAttached</c>, w kolejności <c>apps.json</c>.</param>
/// <param name="Epoch">Monotoniczny numer zbioru; własny licznik Orchestratora, niezależny od <see cref="SetInputRouting.Epoch"/>.</param>
public sealed record SetManagedWindows(
    long CmdId,
    long? OskHwnd,
    ManagedWindow[] Apps,
    long Epoch)
    : Command(CmdId);
