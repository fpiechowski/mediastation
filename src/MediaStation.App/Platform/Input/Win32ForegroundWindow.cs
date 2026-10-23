using MediaStation.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Okno pierwszego planu z Win32 (AD-8): <c>GetForegroundWindow</c>, korzeń <c>GA_ROOT</c> i PID jego procesu.
/// Brak okna (pulpit bezpieczny, przełącznik Alt+Tab, okno w trakcie niszczenia) daje <c>null</c>, czyli obce.
/// Wołany z callbacku hooka LL, więc robi trzy tanie wywołania i nic nie blokuje.
/// </summary>
internal sealed class Win32ForegroundWindow : IForegroundWindow
{
    public unsafe ForegroundWindowInfo? Current()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return null;
        }

        var root = PInvoke.GetAncestor(foreground, GET_ANCESTOR_FLAGS.GA_ROOT);
        if (root.IsNull)
        {
            root = foreground;
        }

        uint pid;
        if (PInvoke.GetWindowThreadProcessId(root, &pid) == 0)
        {
            return null;
        }

        return new ForegroundWindowInfo((long)(nint)root.Value, (int)pid);
    }
}
