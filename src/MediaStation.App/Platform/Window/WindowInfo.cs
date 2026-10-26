namespace MediaStation.App.Platform.Window;

/// <summary>
/// Opis okna najwyższego poziomu bez typów Win32: dane, na których decyzje WindowManagera i harnessu są czystymi funkcjami.
/// </summary>
/// <param name="Hwnd">Uchwyt okna jako <c>long</c> (kontrakty JSON nie niosą <c>IntPtr</c>).</param>
/// <param name="Pid">PID procesu właściciela okna.</param>
/// <param name="ClassName">Nazwa klasy okna (<c>GetClassName</c>).</param>
/// <param name="Visible">Wynik <c>IsWindowVisible</c>.</param>
/// <param name="HasOwner">Okno ma właściciela (<c>GW_OWNER</c>), czyli to okno pomocnicze, nie główne.</param>
internal sealed record WindowInfo(long Hwnd, int Pid, string ClassName, bool Visible, bool HasOwner);
