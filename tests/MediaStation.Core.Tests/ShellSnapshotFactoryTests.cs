using MediaStation.Core.Config;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class ShellSnapshotFactoryTests
{
    private static LoadedConfig TwoApps() => new(
        new AppsConfig([
            new WebApp("netflix", AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222),
            new NativeApp("plex", AppCategory.Game, "Plex", "plex.png", "C:\\Plex\\Plex.exe"),
        ]),
        new InputConfig([], []),
        new Settings());

    [Fact]
    public void Config_without_errors_becomes_placeholder_with_not_running_apps()
    {
        var result = new ConfigResult(TwoApps(), []);

        var snapshot = ShellSnapshotFactory.FromConfig(result, rev: 7);

        Assert.Equal(7, snapshot.Rev);
        Assert.Equal(ShellScreen.Placeholder, snapshot.Screen);
        Assert.Empty(snapshot.ConfigErrors);
        Assert.Equal(
            [
                new AppSnapshot("netflix", AppCategory.Video, AppLifecycleState.NotRunning),
                new AppSnapshot("plex", AppCategory.Game, AppLifecycleState.NotRunning),
            ],
            snapshot.Apps);
        Assert.Null(snapshot.FocusRequest);
    }

    [Fact]
    public void Bootstrap_config_error_has_no_apps_and_no_focus()
    {
        ConfigError[] errors = [new("apps.json", null, "brak pliku")];
        var result = new ConfigResult(null, errors);

        var snapshot = ShellSnapshotFactory.FromConfig(result, rev: 0);

        Assert.Equal(0, snapshot.Rev);
        Assert.Equal(ShellScreen.ConfigError, snapshot.Screen);
        Assert.Same(errors, snapshot.ConfigErrors);
        Assert.Empty(snapshot.Apps);
        Assert.Null(snapshot.FocusRequest);
    }

    [Fact]
    public void Config_with_errors_becomes_config_error_with_the_same_list_and_no_apps()
    {
        ConfigError[] errors = [new("apps.json", "$.apps[0].cdpPort", "port poza zakresem")];
        var result = new ConfigResult(null, errors);

        var snapshot = ShellSnapshotFactory.FromConfig(result, rev: 3);

        Assert.Equal(3, snapshot.Rev);
        Assert.Equal(ShellScreen.ConfigError, snapshot.Screen);
        Assert.Same(errors, snapshot.ConfigErrors);
        Assert.Empty(snapshot.Apps);
        Assert.Null(snapshot.FocusRequest);
    }
}
