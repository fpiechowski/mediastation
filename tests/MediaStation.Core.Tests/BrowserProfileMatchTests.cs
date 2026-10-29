using MediaStation.App.Hosts.Browser;
using MediaStation.App.Hosts.DevHarness;

namespace MediaStation.Core.Tests;

/// <summary>
/// Czyste dopasowanie procesów przeglądarki do profilu i portu CDP (story 6.5, <see cref="BrowserLaunch"/>): wspólne dla
/// adopcji BrowserHosta i dev harnessu, którego wrappery dają ten sam wynik.
/// </summary>
public class BrowserProfileMatchTests
{
    private const string Profile = @"C:\d\profiles\netflix";

    [Theory]
    [InlineData(@"""C:\Edge\msedge.exe"" --user-data-dir=C:\d\profiles\netflix --no-first-run about:blank", true)]
    [InlineData(@"""C:\Edge\msedge.exe"" ""--user-data-dir=C:\d\profiles\netflix"" about:blank", true)]
    [InlineData(@"""C:\Edge\msedge.exe"" --user-data-dir=""C:\d\profiles\netflix"" about:blank", true)]
    [InlineData(@"msedge.exe --user-data-dir=c:\D\PROFILES\NETFLIX x", true)]
    [InlineData(@"msedge.exe --USER-DATA-DIR=C:\d\profiles\netflix x", true)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix\ x", true)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix/ x", true)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix", true)]
    [InlineData("msedge.exe --user-data-dir=C:\\d\\profiles\\netflix\t--kiosk", true)]
    [InlineData("msedge.exe --user-data-dir=C:\\d\\profiles\\netflix\\\t--kiosk", true)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix\child x", false)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix\child", false)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix/child x", false)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix\..\other x", false)]
    [InlineData(@"msedge.exe --type=renderer --user-data-dir=C:\d\profiles\netflix x", true)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix2 x", false)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix-old x", false)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\other x", false)]
    [InlineData(@"msedge.exe about:blank", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Profile_matcher_ignores_quotes_case_and_a_trailing_slash_but_not_a_sibling_prefix(string? commandLine, bool expected)
    {
        Assert.Equal(expected, BrowserLaunch.CommandLineMatchesProfile(commandLine, Profile));
        Assert.Equal(expected, BrowserLaunch.CommandLineMatchesProfile(commandLine, Profile + @"\"));
        Assert.Equal(expected, HarnessBrowser.CommandLineMatchesProfile(commandLine, Profile));
    }

    [Theory]
    [InlineData(@"msedge.exe --user-data-dir=C:\p\a", true)]
    [InlineData(@"""C:\Edge\msedge.exe"" --user-data-dir=C:\p\a --remote-debugging-port=9222", true)]
    [InlineData(@"msedge.exe --type=renderer --user-data-dir=C:\p\a", false)]
    [InlineData(@"msedge.exe --type=gpu-process --user-data-dir=C:\p\a", false)]
    [InlineData(@"msedge.exe --user-data-dir=C:\p\a --type=utility", false)]
    public void Browser_root_is_a_process_without_a_type_switch(string commandLine, bool expected) =>
        Assert.Equal(expected, BrowserLaunch.IsBrowserRoot(commandLine));

    [Fact]
    public void Matching_roots_put_the_main_process_before_helpers_and_skip_other_profiles()
    {
        KeyValuePair<int, string>[] lines =
        [
            new(1, @"msedge.exe --type=renderer --user-data-dir=C:\p\a x"),
            new(2, @"msedge.exe --user-data-dir=C:\p\a"),
            new(3, @"msedge.exe --user-data-dir=C:\p\b"),
            new(4, @"msedge.exe --user-data-dir=C:\p\a2"),
            new(5, @"""msedge.exe"" ""--user-data-dir=C:\p\a"" --remote-debugging-port=9237"),
        ];

        Assert.Equal([2, 5, 1], BrowserLaunch.MatchingRoots(lines, @"C:\p\a"));
        Assert.Equal([2, 5, 1], HarnessBrowser.MatchingRoots(lines, @"C:\p\a"));
    }

    [Theory]
    [InlineData("msedge.exe --remote-debugging-port=9222 --kiosk about:blank", true)]
    [InlineData("msedge.exe --kiosk --remote-debugging-port=9222", true)]
    [InlineData(@"""msedge.exe"" ""--remote-debugging-port=9222"" about:blank", true)]
    [InlineData("--remote-debugging-port=9222", true)]
    [InlineData("msedge.exe --REMOTE-DEBUGGING-PORT=9222", true)]
    [InlineData("msedge.exe --remote-debugging-port=92220 --kiosk", false)]
    [InlineData("msedge.exe --remote-debugging-port=19222 --kiosk", false)]
    [InlineData("msedge.exe --remote-debugging-port=922 --kiosk", false)]
    [InlineData("msedge.exe --remote-debugging-port=9223", false)]
    [InlineData("msedge.exe --x--remote-debugging-port=9222", false)]
    [InlineData("msedge.exe --remote-debugging-pipe --kiosk", false)]
    [InlineData("msedge.exe --kiosk about:blank", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Debugging_port_matches_the_exact_token_only(string? commandLine, bool expected) =>
        Assert.Equal(expected, BrowserLaunch.HasDebuggingPort(commandLine, 9222));

    [Fact]
    public void Launch_arguments_carry_the_port_token_the_probe_looks_for()
    {
        var line = string.Join(' ', BrowserLaunch.BuildArguments(Profile, 9237));

        Assert.True(BrowserLaunch.HasDebuggingPort(line, 9237));
        Assert.False(BrowserLaunch.HasDebuggingPort(line, 9222));
        Assert.True(BrowserLaunch.CommandLineMatchesProfile(line, Profile));
        Assert.True(BrowserLaunch.IsBrowserRoot(line));
    }
}
