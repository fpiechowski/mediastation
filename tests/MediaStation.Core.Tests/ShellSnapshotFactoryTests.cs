using MediaStation.Core.Config;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class ShellSnapshotFactoryTests
{
    [Fact]
    public void Config_without_errors_becomes_placeholder()
    {
        var result = new ConfigResult(null, []);

        var snapshot = ShellSnapshotFactory.FromConfig(result, rev: 7);

        Assert.Equal(7, snapshot.Rev);
        Assert.Equal(ShellScreen.Placeholder, snapshot.Screen);
        Assert.Empty(snapshot.ConfigErrors);
    }

    [Fact]
    public void Config_with_errors_becomes_config_error_with_the_same_list()
    {
        ConfigError[] errors = [new("apps.json", "$.apps[0].cdpPort", "port poza zakresem")];
        var result = new ConfigResult(null, errors);

        var snapshot = ShellSnapshotFactory.FromConfig(result, rev: 3);

        Assert.Equal(3, snapshot.Rev);
        Assert.Equal(ShellScreen.ConfigError, snapshot.Screen);
        Assert.Same(errors, snapshot.ConfigErrors);
    }
}
