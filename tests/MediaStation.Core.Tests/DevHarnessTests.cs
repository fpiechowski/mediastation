using MediaStation.Core.Contracts;
using MediaStation.App.Hosts.DevHarness;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;

namespace MediaStation.Core.Tests;

/// <summary>Czyste decyzje dev harnessu: wybór okna z drzewa procesów, linia poleceń i wybór przeglądarki.</summary>
public class DevHarnessTests
{
    private static readonly HashSet<int> Tree = [10, 11];

    [Fact]
    public void Picker_skips_owned_hidden_and_foreign_windows_and_takes_visible_ownerless_browser_window()
    {
        WindowInfo[] windows =
        [
            new(1, 10, "Chrome_WidgetWin_1", Visible: true, HasOwner: true),
            new(2, 10, "Chrome_WidgetWin_1", Visible: false, HasOwner: false),
            new(3, 10, "Notepad", Visible: true, HasOwner: false),
            new(4, 99, "Chrome_WidgetWin_1", Visible: true, HasOwner: false),
            new(5, 11, "Chrome_WidgetWin_1", Visible: true, HasOwner: false),
        ];

        Assert.Equal(5L, WindowTreePicker.Pick(windows, Tree)?.Hwnd);
    }

    [Fact]
    public void Picker_returns_null_when_no_candidate_matches()
    {
        WindowInfo[] windows = [new(1, 10, "Chrome_WidgetWin_1", Visible: true, HasOwner: true)];

        Assert.Null(WindowTreePicker.Pick(windows, Tree));
    }

    [Fact]
    public void Descendants_follow_parent_links_and_survive_cycles()
    {
        KeyValuePair<int, int>[] parents = [new(11, 10), new(12, 11), new(13, 99), new(10, 12)];

        Assert.Equal(new HashSet<int> { 10, 11, 12 }, WindowTreePicker.Descendants(10, parents));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Harness_is_enabled_only_by_one(string? value, bool expected) =>
        Assert.Equal(expected, HarnessBrowser.IsEnabled(value));

    [Fact]
    public void Command_line_uses_ticket_flags_with_url_last()
    {
        var args = HarnessBrowser.BuildArguments(@"C:\data\profiles\netflix", "https://example.test/");

        Assert.Equal(
            [
                @"--user-data-dir=C:\data\profiles\netflix",
                "--no-first-run",
                "--no-default-browser-check",
                "--kiosk",
                "--autoplay-policy=no-user-gesture-required",
                "https://example.test/",
            ],
            args);
    }

    [Theory]
    [InlineData(null, "about:blank")]
    [InlineData("", "about:blank")]
    [InlineData("   ", "about:blank")]
    [InlineData(" https://example.test/x ", "https://example.test/x")]
    public void Url_comes_from_env_or_defaults_to_about_blank(string? value, string expected) =>
        Assert.Equal(expected, HarnessBrowser.ResolveUrl(value));

    [Theory]
    [InlineData(@"""C:\Edge\msedge.exe"" --user-data-dir=C:\d\profiles\netflix --no-first-run about:blank", true)]
    [InlineData(@"""C:\Edge\msedge.exe"" ""--user-data-dir=C:\d\profiles\netflix"" about:blank", true)]
    [InlineData(@"msedge.exe --user-data-dir=c:\D\PROFILES\netflix\ x", true)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix", true)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\netflix2 x", false)]
    [InlineData(@"msedge.exe --user-data-dir=C:\d\profiles\other x", false)]
    [InlineData(@"msedge.exe about:blank", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Command_line_profile_matcher(string? commandLine, bool expected) =>
        Assert.Equal(expected, HarnessBrowser.CommandLineMatchesProfile(commandLine, @"C:\d\profiles\netflix"));

    [Fact]
    public void Matching_roots_put_main_process_before_helpers_and_skip_other_profiles()
    {
        KeyValuePair<int, string>[] lines =
        [
            new(1, @"msedge.exe --type=renderer --user-data-dir=C:\p\a x"),
            new(2, @"msedge.exe --user-data-dir=C:\p\a"),
            new(3, @"msedge.exe --user-data-dir=C:\p\b"),
        ];

        Assert.Equal([2, 1], HarnessBrowser.MatchingRoots(lines, @"C:\p\a"));
    }

    [Fact]
    public void Edge_and_chrome_candidates_differ_and_skip_missing_roots()
    {
        var edge = HarnessBrowser.ExecutableCandidates(BrowserKind.Edge, @"C:\PF", @"C:\PF86", null);
        var chrome = HarnessBrowser.ExecutableCandidates(BrowserKind.Chrome, @"C:\PF", @"C:\PF86", @"C:\Local");

        Assert.Equal([@"C:\PF86\Microsoft\Edge\Application\msedge.exe", @"C:\PF\Microsoft\Edge\Application\msedge.exe"], edge);
        Assert.Equal(3, chrome.Count);
        Assert.All(chrome, path => Assert.EndsWith(@"Google\Chrome\Application\chrome.exe", path));
    }

    [Fact]
    public void Resolve_returns_first_existing_candidate_or_null()
    {
        string[] candidates = ["a.exe", "b.exe", "c.exe"];

        Assert.Equal("b.exe", HarnessBrowser.ResolveExecutable(candidates, path => path != "a.exe"));
        Assert.Null(HarnessBrowser.ResolveExecutable(candidates, _ => false));
    }

    [Fact]
    public void Picker_without_class_takes_any_visible_ownerless_window_from_tree()
    {
        WindowInfo[] windows =
        [
            new(1, 10, "SDL_app", Visible: true, HasOwner: true),
            new(2, 99, "SDL_app", Visible: true, HasOwner: false),
            new(3, 10, "SDL_app", Visible: false, HasOwner: false),
            new(4, 11, "SDL_app", Visible: true, HasOwner: false),
        ];

        Assert.Equal(4L, WindowTreePicker.Pick(windows, Tree, windowClass: null)?.Hwnd);
        Assert.Null(WindowTreePicker.Pick(windows, Tree));
    }

    [Fact]
    public void Moonlight_arguments_stream_host_app_borderless_and_mute()
    {
        Assert.Equal(
            ["stream", "desktop", "Desktop", "--display-mode", "borderless", "--mute-on-focus-loss"],
            HarnessMoonlight.BuildArguments("desktop", "Desktop"));
    }

    [Fact]
    public void Moonlight_executable_comes_from_env_when_file_exists()
    {
        Assert.Equal(@"C:\M\Moonlight.exe", HarnessMoonlight.ResolveExecutable(@" ""C:\M\Moonlight.exe"" ", _ => true));
        Assert.Null(HarnessMoonlight.ResolveExecutable(@"C:\M\Moonlight.exe", _ => false));
        Assert.Null(HarnessMoonlight.ResolveExecutable(null, _ => true));
        Assert.Null(HarnessMoonlight.ResolveExecutable("  ", _ => true));
    }

    [Theory]
    [InlineData(@"""C:\M\Moonlight.exe"" stream desktop Desktop --display-mode borderless", true)]
    [InlineData(@"C:\M\Moonlight.exe stream DESKTOP desktop", true)]
    [InlineData(@"C:\M\Moonlight.exe stream desktop Desktop", true)]
    [InlineData(@"C:\M\Moonlight.exe stream desktop ""Steam Big Picture"" --quit-after", true)]
    [InlineData(@"C:\M\Moonlight.exe stream desktop Desktop2", false)]
    [InlineData(@"C:\M\Moonlight.exe stream other Desktop", false)]
    [InlineData(@"C:\M\Moonlight.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Moonlight_command_line_matcher(string? commandLine, bool expected)
    {
        var app = commandLine?.Contains("Steam", StringComparison.Ordinal) == true ? "Steam Big Picture" : "Desktop";
        Assert.Equal(expected, HarnessMoonlight.CommandLineMatches(commandLine, "desktop", app));
    }

    [Fact]
    public void Moonlight_roots_keep_only_matching_streams()
    {
        KeyValuePair<int, string>[] lines =
        [
            new(1, @"Moonlight.exe stream desktop Desktop"),
            new(2, @"Moonlight.exe stream other Desktop"),
            new(3, @"msedge.exe --user-data-dir=C:\p"),
        ];

        Assert.Equal([1], HarnessMoonlight.MatchingRoots(lines, "desktop", "Desktop"));
    }

    [Fact]
    public void Launch_window_class_is_browser_class_for_web_and_stream_class_for_moonlight()
    {
        var web = new WebApp("w", AppCategory.Video, "W", "w.png", "svc", BrowserKind.Edge, 9222);
        var moon = new MoonlightApp("m", AppCategory.Game, "M", "m.png", "desktop", "Desktop");

        Assert.Equal("Chrome_WidgetWin_1", HarnessLaunch.WindowClassOf(web));
        Assert.Equal("SDL_app", HarnessLaunch.WindowClassOf(moon));
    }

    [Fact]
    public void Moonlight_picker_skips_the_qt_connecting_window_and_waits_for_the_sdl_stream_window()
    {
        var moon = new MoonlightApp("m", AppCategory.Game, "M", "m.png", "desktop", "Desktop");
        var moonClass = HarnessLaunch.WindowClassOf(moon);
        WindowInfo qt = new(1, 10, "Qt6120QWindowIcon", Visible: true, HasOwner: false);
        WindowInfo sdl = new(2, 10, "SDL_app", Visible: true, HasOwner: false);

        // Pierwsze ~5 s strumienia widać tylko okno Qt: nie jest oknem aplikacji, więc nic nie jest podpinane.
        Assert.Null(WindowTreePicker.Pick([qt], Tree, moonClass));

        // Po starcie strumienia okno Qt jest ukryte albo dalej widoczne: wybór zawsze pada na okno SDL.
        Assert.Equal(2L, WindowTreePicker.Pick([qt, sdl], Tree, moonClass)?.Hwnd);
        Assert.Equal(2L, WindowTreePicker.Pick([qt with { Visible = false }, sdl], Tree, moonClass)?.Hwnd);
    }

    [Fact]
    public void Launch_roots_use_profile_for_web_and_stream_for_moonlight()
    {
        var web = new WebApp("w", AppCategory.Video, "W", "w.png", "svc", BrowserKind.Edge, 9222);
        var moon = new MoonlightApp("m", AppCategory.Game, "M", "m.png", "desktop", "Desktop");
        KeyValuePair<int, string>[] lines =
        [
            new(1, @"msedge.exe --user-data-dir=C:\p\w"),
            new(2, @"Moonlight.exe stream desktop Desktop"),
        ];

        Assert.Equal([1], HarnessLaunch.MatchingRootsFor(web, lines, @"C:\p"));
        Assert.Equal([2], HarnessLaunch.MatchingRootsFor(moon, lines, @"C:\p"));
    }

    [Fact]
    public void Moonlight_launch_differs_from_manual_only_by_borderless_and_mute_args()
    {
        var exe = @"C:\M\Moonlight.exe";
        var manual = HarnessMoonlight.ManualLaunch(exe, "desktop", "Desktop");
        var actual = HarnessMoonlight.BuildLaunch(exe, "desktop", "Desktop", ["PATH", "MEDIASTATION_DEV_HARNESS"]);

        Assert.Equal(@"C:\M", actual.WorkingDirectory);
        Assert.Equal(
            ["arg+: --display-mode", "arg+: borderless", "arg+: --mute-on-focus-loss"],
            HarnessMoonlight.Differences(manual, actual));
    }

    [Fact]
    public void Moonlight_launch_without_working_directory_is_reported_as_a_difference()
    {
        var exe = @"C:\M\Moonlight.exe";
        var manual = HarnessMoonlight.ManualLaunch(exe, "desktop", "Desktop");
        var baseline = HarnessMoonlight.BuildLaunch(exe, "desktop", "Desktop", []) with { WorkingDirectory = null };

        Assert.Contains(HarnessMoonlight.Differences(manual, baseline), d => d.StartsWith("cwd:", StringComparison.Ordinal));
    }

    [Fact]
    public void Moonlight_environment_removal_lists_only_mediastation_variables()
    {
        Assert.Equal(
            ["MEDIASTATION_DEV_HARNESS", "mediastation_harness_moonlight"],
            HarnessMoonlight.EnvironmentToRemove(["MEDIASTATION_DEV_HARNESS", "PATH", "mediastation_harness_moonlight"]));
    }

    [Fact]
    public void Moonlight_environment_description_redacts_secrets()
    {
        var described = HarnessMoonlight.DescribeEnvironment(
        [
            new("FOO_TOKEN", "abc"),
            new("my_password", "x"),
            new("PATH", @"C:\bin"),
        ]);

        Assert.Equal(["FOO_TOKEN=<redacted>", "my_password=<redacted>", @"PATH=C:\bin"], described);
    }

    [Fact]
    public void Moonlight_apply_sets_working_directory_and_strips_mediastation_variables()
    {
        var exe = @"C:\M\Moonlight.exe";
        var info = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
        info.Environment["MEDIASTATION_X"] = "1";
        info.Environment["KEEP_ME"] = "2";
        var launch = HarnessMoonlight.BuildLaunch(exe, "desktop", "Desktop", info.Environment.Keys);

        HarnessMoonlight.Apply(info, launch);

        Assert.Equal(@"C:\M", info.WorkingDirectory);
        Assert.False(info.Environment.ContainsKey("mediastation_x"));
        Assert.True(info.Environment.ContainsKey("KEEP_ME"));
    }

    [Fact]
    public void Moonlight_differences_report_extra_environment_removals()
    {
        var exe = @"C:\M\Moonlight.exe";
        var manual = HarnessMoonlight.ManualLaunch(exe, "desktop", "Desktop");
        var actual = HarnessMoonlight.BuildLaunch(exe, "desktop", "Desktop", []) with { EnvironmentToRemove = ["FOO"] };

        Assert.Contains("env-: FOO", HarnessMoonlight.Differences(manual, actual));
    }

    [Fact]
    public void ClassifyStage_is_handshake_with_only_qt_window_and_windowAttach_with_unselectable_sdl_window()
    {
        WindowInfo[] qtOnly = [new(1, 10, "Qt6QWindowIcon", Visible: true, HasOwner: false), new(2, 99, "SDL_app", Visible: true, HasOwner: false)];
        WindowInfo[] hiddenSdl = [new(1, 10, "Qt6QWindowIcon", Visible: true, HasOwner: false), new(3, 11, "SDL_app", Visible: false, HasOwner: false)];

        Assert.Equal("handshake", HarnessMoonlight.ClassifyStage(qtOnly, Tree, "SDL_app"));
        Assert.Equal("windowAttach", HarnessMoonlight.ClassifyStage(hiddenSdl, Tree, "SDL_app"));
        Assert.Equal("handshake", HarnessMoonlight.ClassifyStage([], Tree, "SDL_app"));
    }

    [Fact]
    public void TimeoutDiagnostics_has_stage_and_hints_for_qt_only_and_unselectable_sdl_trees()
    {
        WindowInfo[] qtOnly = [new(1, 10, "Qt6QWindowIcon", Visible: true, HasOwner: false)];
        WindowInfo[] hiddenSdl = [new(3, 11, "SDL_app", Visible: false, HasOwner: false)];
        string[] log = ["noise", "serverinfo request failed: HostNotFoundError"];

        var qt = HarnessMoonlight.TimeoutDiagnostics(qtOnly, Tree, "SDL_app", log);
        var sdl = HarnessMoonlight.TimeoutDiagnostics(hiddenSdl, Tree, "SDL_app", log);

        Assert.Equal("handshake", qt["failedStage"]);
        Assert.Equal("windowAttach", sdl["failedStage"]);
        Assert.Equal(["serverinfo request failed: HostNotFoundError"], Assert.IsAssignableFrom<IReadOnlyList<string>>(qt["moonlightLogHints"]));
        Assert.Equal(qt["moonlightLogHints"], sdl["moonlightLogHints"]);
    }

    [Fact]
    public void LogHints_keeps_last_five_matching_lines_in_order_and_is_empty_without_matches()
    {
        string[] lines = ["noise", "Warning a", "x", "Error b", "serverinfo request failed c", "d FAILED", "Warning e", "Error f", "tail"];

        Assert.Equal(["Error b", "serverinfo request failed c", "d FAILED", "Warning e", "Error f"], HarnessMoonlight.LogHints(lines));
        Assert.Empty(HarnessMoonlight.LogHints(["a", "b"]));
    }

    [Fact]
    public void LatestLogFile_picks_newest_or_null()
    {
        var t = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal("b", HarnessMoonlight.LatestLogFile([new("a", t), new("b", t.AddMinutes(1)), new("c", t.AddMinutes(-1))]));
        Assert.Null(HarnessMoonlight.LatestLogFile([]));
    }

    private sealed class Clock
    {
        public TimeSpan Now;
        public int Sleeps;

        public void Sleep()
        {
            Sleeps++;
            Now += TimeSpan.FromMilliseconds(100);
        }
    }

    [Fact]
    public void Wait_returns_window_that_appears_after_some_polls()
    {
        var clock = new Clock();
        var polls = 0;

        var result = HarnessWait.FindWindow(() => ++polls == 4 ? "w" : null, () => false, () => false, clock.Sleep, () => clock.Now, TimeSpan.FromSeconds(20));

        Assert.Equal("w", result);
        Assert.Equal(3, clock.Sleeps);
    }

    [Fact]
    public void Wait_returns_null_at_timeout_when_window_never_appears()
    {
        var clock = new Clock();

        var result = HarnessWait.FindWindow<string>(() => null, () => false, () => false, clock.Sleep, () => clock.Now, TimeSpan.FromSeconds(1));

        Assert.Null(result);
        Assert.Equal(10, clock.Sleeps);
    }

    [Fact]
    public void Wait_returns_null_when_process_exited_or_stopping()
    {
        var clock = new Clock();

        Assert.Null(HarnessWait.FindWindow(() => "w", () => true, () => false, clock.Sleep, () => clock.Now, TimeSpan.FromSeconds(20)));
        Assert.Null(HarnessWait.FindWindow(() => "w", () => false, () => true, clock.Sleep, () => clock.Now, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void Wait_is_stateless_so_a_second_wait_after_a_timeout_finds_the_window()
    {
        var first = new Clock();
        var second = new Clock();
        var secondPolls = 0;

        Assert.Null(HarnessWait.FindWindow<string>(() => null, () => false, () => false, first.Sleep, () => first.Now, TimeSpan.FromSeconds(1)));
        Assert.Equal("w2", HarnessWait.FindWindow(() => ++secondPolls == 2 ? "w2" : null, () => false, () => false, second.Sleep, () => second.Now, TimeSpan.FromSeconds(1)));
    }
}
