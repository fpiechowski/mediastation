using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MediaStation.App.Tests;

/// <summary>
/// Prawdziwe okno message-only (<c>HWND_MESSAGE</c>) założone przez <c>HwndSource</c>, tak jak shell, żeby
/// <c>HwndSource.FromHwnd</c> w <c>PowerNotifications</c> znalazło źródło. Komunikaty wysyłamy
/// <c>SendMessage</c> z wątku okna, więc przechodzą przez prawdziwy WndProc i łańcuch hooków WPF.
/// Okno należy do wątku STA, który je założył; wszystkie wywołania muszą iść z tego wątku.
/// </summary>
internal sealed class MessageOnlyWindow : IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;

    public MessageOnlyWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("PowerNotificationsHarness")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>
    /// Dodaje obserwatora hooka. Hook dodany po shimie widzi komunikat tylko wtedy, gdy shim nie ustawił
    /// <c>handled</c>, co sprawdza kontrakt „broadcast dociera do innych odbiorców”.
    /// </summary>
    public void AddHook(HwndSourceHook hook) => _source.AddHook(hook);

    /// <summary>Synchroniczny <c>SendMessage</c> do tego okna z wątku okna.</summary>
    public void Send(uint message, nuint wParam, nint lParam) =>
        PInvoke.SendMessage((HWND)Handle, message, (WPARAM)wParam, (LPARAM)lParam);

    /// <summary>
    /// Pompuje kolejkę komunikatów wątku przez <paramref name="duration"/>: tak dochodzą komunikaty
    /// <c>PostMessage</c> od systemu, np. echo <c>RegisterPowerSettingNotification</c>.
    /// </summary>
    public static void Pump(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            while (PInvoke.PeekMessage(out var msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                PInvoke.TranslateMessage(in msg);
                PInvoke.DispatchMessage(in msg);
            }

            Thread.Sleep(10);
        }
    }

    public void Dispose() => _source.Dispose();
}
