using System.IO;
using System.Windows;
using MediaStation.App.Config;
using MediaStation.App.Shell;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;

namespace MediaStation.App;

/// <summary>
/// Składa aplikację: odczyt configu (I/O tylko w App), preflight w Core, snapshot dla shella, okno shella.
/// </summary>
public static class CompositionRoot
{
    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaStation");

    public static string ConfigDir { get; } = Path.Combine(DataRoot, "config");

    public static string WebView2UserDataDir { get; } = Path.Combine(DataRoot, "webview2");

    public static void Start(Application app)
    {
        var files = ConfigFileReader.ReadAll(ConfigDir, ConfigPreflight.RequiredFiles);
        var errors = ConfigPreflight.Check(files);
        var snapshot = new ShellSnapshot(
            Rev: 1,
            Screen: errors.Count == 0 ? ShellScreen.Placeholder : ShellScreen.ConfigError,
            ConfigErrors: errors);

        var bridge = new ShellBridge(snapshot);
        var window = new ShellWindow(bridge, WebView2UserDataDir);
        app.MainWindow = window;
        window.Show();
    }
}
