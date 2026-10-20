using System.IO;
using System.Windows;
using MediaStation.Core.Logging;
using Microsoft.Web.WebView2.Core;

namespace MediaStation.App.Shell;

/// <summary>
/// Pusty host WPF dla WebView2: okno bez ramki, pełnoekranowe, tło ground. UI powstaje w web/shell (AD-2).
/// </summary>
public partial class ShellWindow : Window
{
#if SHELL_DEV_SERVER
    // Debug: shell z Vite dev servera (AD-13), z HMR.
    private static readonly Uri ShellUri = new("http://localhost:5173/");
#else
    // Release: shell z plików lokalnych <exe>/shell przez wirtualny host.
    private const string ShellHost = "shell.mediastation.example";
    private static readonly Uri ShellUri = new($"https://{ShellHost}/index.html");
#endif

    private readonly ShellBridge _bridge;
    private readonly string _userDataDir;
    private readonly ILog _log;

    public ShellWindow(ShellBridge bridge, string userDataDir, ILog log)
    {
        _bridge = bridge;
        _userDataDir = userDataDir;
        _log = log;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_userDataDir);
            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: _userDataDir);
            await WebView.EnsureCoreWebView2Async(environment);

            var core = WebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
#if !SHELL_DEV_SERVER
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                ShellHost,
                Path.Combine(AppContext.BaseDirectory, "shell"),
                CoreWebView2HostResourceAccessKind.DenyCors);
#endif

            _bridge.Attach(core);
            core.Navigate(ShellUri.ToString());
        }
        catch (Exception ex)
        {
            // Bez WebView2 albo plików shella nie ma UI; logujemy i pokazujemy powód zamiast crasha procesu.
            _log.Error("shell", "webView2StartFailed", ex);
            MessageBox.Show(this, $"Nie udało się uruchomić shella:\n{ex.Message}", "MediaStation", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }
}
