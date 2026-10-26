using System.Collections.Generic;
using System.IO;
using System.Windows;
using MediaStation.Core.Cli;
using MediaStation.Core.Logging;
using Microsoft.Web.WebView2.Core;

namespace MediaStation.App.Shell;

/// <summary>
/// Pusty host WPF dla WebView2: okno bez ramki, pełnoekranowe, tło ground. UI powstaje w web/shell (AD-2).
/// </summary>
public partial class ShellWindow : Window
{
    private static readonly Uri ShellUri = ShellOrigin.Uri;

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
                ShellOrigin.Host,
                Path.Combine(AppContext.BaseDirectory, "shell"),
                CoreWebView2HostResourceAccessKind.DenyCors);
#endif

            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed += OnProcessFailed;
            core.NewWindowRequested += OnNewWindowRequested;
            _bridge.Attach(core);
            core.Navigate(ShellUri.ToString());
        }
        catch (Exception ex)
        {
            // Bez WebView2 albo plików shella nie ma UI; logujemy, pokazujemy powód i kończymy proces kodem awarii hosta.
            _log.Error("shell", "webView2StartFailed", ex);
            MessageBox.Show(this, $"Nie udało się uruchomić shella:\n{ex.Message}", "MediaStation", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown((int)ExitCode.ShellStartFailed);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            return;
        }

        _log.Error("shell", "navigationFailed", data: new Dictionary<string, object?>
        {
            ["uri"] = ShellUri.ToString(),
            ["webErrorStatus"] = e.WebErrorStatus,
            ["httpStatusCode"] = e.HttpStatusCode,
        });
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _log.Error("shell", "webViewProcessFailed", data: new Dictionary<string, object?>
        {
            ["kind"] = e.ProcessFailedKind,
            ["reason"] = e.Reason,
            ["exitCode"] = e.ExitCode,
            ["processDescription"] = e.ProcessDescription,
        });
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _log.Warn("shell", "newWindowBlocked", new Dictionary<string, object?>
        {
            ["uri"] = e.Uri,
        });
    }
}
