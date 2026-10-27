using System.Collections.Generic;

namespace MediaStation.App.Platform.Window;

/// <summary>Sposób pokazania okna w <see cref="IWindowApi.ShowAsync"/>; mapowanie na <c>SW_*</c> robi adapter Win32.</summary>
internal enum WindowShow
{
    /// <summary>Przywrócenie zminimalizowanego okna (<c>SW_RESTORE</c>).</summary>
    Restore,

    /// <summary>Pokazanie okna w obecnym rozmiarze (<c>SW_SHOW</c>).</summary>
    Show,

    /// <summary>Minimalizacja bez aktywacji (<c>SW_SHOWMINNOACTIVE</c>); nigdy <c>SW_HIDE</c> (AD-5).</summary>
    MinimizeNoActivate,
}

/// <summary>Okno najwyższego poziomu i PID jego procesu; <c>(0, 0)</c>, gdy brak okna.</summary>
internal readonly record struct ForegroundWindow(long Hwnd, int Pid);

/// <summary>
/// Jedyna granica logiki WindowManagera do Win32 (AD-15). Operacje na cudzych oknach są asynchroniczne
/// (<c>ShowWindowAsync</c>, <c>SWP_ASYNCWINDOWPOS</c>), żeby zawieszone okno nie blokowało wątku WindowManagera.
/// </summary>
internal interface IWindowApi
{
    bool IsWindow(long hwnd);

    bool IsIconic(long hwnd);

    bool IsVisible(long hwnd);

    /// <summary>Zleca zmianę stanu okna (<c>ShowWindowAsync</c>); <c>false</c>, gdy system nie przyjął polecenia.</summary>
    bool ShowAsync(long hwnd, WindowShow show);

    /// <summary>Zleca postawienie okna na szczycie Z-order (asynchroniczne <c>SetWindowPos</c>, bez ruchu i zmiany rozmiaru).</summary>
    bool RaiseAsync(long hwnd);

    /// <summary>Bezpośrednie <c>SetForegroundWindow</c>.</summary>
    bool SetForeground(long hwnd);

    /// <summary>Symulowany Alt ze znacznikiem MediaStation (odblokowanie foregroundu, helper <c>KeyInjector</c>).</summary>
    bool SimulateAlt();

    /// <summary>Fallback: <c>AttachThreadInput</c> do wątku okna pierwszego planu, <c>SetForegroundWindow</c> i odpięcie.</summary>
    bool SetForegroundAttached(long hwnd);

    /// <summary>Okno na wierzchu (<c>GA_ROOT</c>) i jego PID; <c>(0, 0)</c>, gdy brak okna.</summary>
    ForegroundWindow GetForeground();

    /// <summary>Okna najwyższego poziomu (<c>EnumWindows</c>).</summary>
    IReadOnlyList<WindowInfo> EnumerateTopLevel();
}
