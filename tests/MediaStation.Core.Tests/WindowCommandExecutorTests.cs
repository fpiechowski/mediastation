using MediaStation.App.Platform.Window;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>Wykonawca komend okienkowych: mapowanie na wywołania, kolejność odblokowania foregroundu, wyniki i log.</summary>
public class WindowCommandExecutorTests
{
    private sealed class FakeWindowApi : IWindowApi
    {
        public HashSet<long> Alive { get; } = [100, 200];

        public HashSet<long> Iconic { get; } = [];

        public bool ShowResult { get; set; } = true;

        public bool DirectForeground { get; set; } = true;

        public bool ForegroundAfterAlt { get; set; }

        public bool ForegroundAttached { get; set; }

        public List<string> Calls { get; } = [];

        public List<WindowShow> Shows { get; } = [];

        public bool IsWindow(long hwnd) => Alive.Contains(hwnd);

        /// <summary>Po Restore okno przestaje być iconic dopiero po tylu odczytach <see cref="IsIconic"/>; <c>null</c> = od razu.</summary>
        public int? PollsUntilRestored { get; set; }

        private bool _restoring;

        public bool IsIconic(long hwnd)
        {
            if (_restoring && Iconic.Contains(hwnd) && --PollsUntilRestored <= 0)
            {
                Iconic.Remove(hwnd);
            }

            return Iconic.Contains(hwnd);
        }

        public bool IsVisible(long hwnd) => !Iconic.Contains(hwnd);

        public bool ShowAsync(long hwnd, WindowShow show)
        {
            Calls.Add($"show:{show}");
            Shows.Add(show);
            if (show == WindowShow.Restore)
            {
                if (PollsUntilRestored is null)
                {
                    Iconic.Remove(hwnd);
                }
                else
                {
                    _restoring = true;
                }
            }

            return ShowResult;
        }

        public bool RaiseAsync(long hwnd)
        {
            Calls.Add("raise");
            return true;
        }

        public bool SetForeground(long hwnd)
        {
            Calls.Add("foreground");
            return Calls.Contains("alt") ? ForegroundAfterAlt : DirectForeground;
        }

        public bool SimulateAlt()
        {
            Calls.Add("alt");
            return true;
        }

        public bool SetForegroundAttached(long hwnd)
        {
            Calls.Add("attach");
            return ForegroundAttached;
        }

        public IReadOnlyList<WindowInfo> EnumerateTopLevel() => [];
    }

    private sealed class Rig
    {
        public FakeWindowApi Api { get; } = new();

        public RecordingLog Log { get; } = new();

        public List<IOrchestratorMessage> Posted { get; } = [];

        public long? Shell { get; set; } = 200;

        public WindowCommandExecutor Executor => _executor ??= new(Api, Log, Posted.Add, () => Shell, static _ => { });

        private WindowCommandExecutor? _executor;
    }

    [Fact]
    public void Minimize_uses_show_min_no_active_and_never_hides()
    {
        var rig = new Rig();

        rig.Executor.Execute(new MinimizeWindow(1, 100));

        Assert.Equal([WindowShow.MinimizeNoActivate], rig.Api.Shows);
        Assert.Equal(new CommandCompleted(1), Assert.Single(rig.Posted));
        var entry = Assert.Single(rig.Log.Entries);
        Assert.Equal("windowCommand", entry.Msg);
        Assert.Equal("MinimizeWindow", entry.Data!["type"]);
        Assert.Equal(1L, entry.Data["cmdId"]);
        Assert.Equal(100L, entry.Data["hwnd"]);
        Assert.Equal("completed", entry.Data["result"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dead_hwnd_fails_with_window_not_found_and_logs_result(bool activate)
    {
        var rig = new Rig();
        WindowCommand command = activate ? new ActivateWindow(2, 999) : new MinimizeWindow(2, 999);

        rig.Executor.Execute(command);

        Assert.Equal(new CommandFailed(2, ErrorCode.WindowNotFound), Assert.Single(rig.Posted));
        Assert.Empty(rig.Api.Calls);
        var entry = Assert.Single(rig.Log.Entries);
        Assert.Equal(LogLevel.Warn, entry.Level);
        Assert.Equal("WindowNotFound", entry.Data!["result"]);
    }

    [Fact]
    public void Activate_iconic_window_restores_then_takes_foreground()
    {
        var rig = new Rig();
        rig.Api.Iconic.Add(100);

        rig.Executor.Execute(new ActivateWindow(3, 100));

        Assert.Equal(["show:Restore", "foreground"], rig.Api.Calls);
        Assert.Equal(new CommandCompleted(3), Assert.Single(rig.Posted));
    }

    [Fact]
    public void Foreground_unlock_order_is_direct_then_alt_then_attach()
    {
        var rig = new Rig();
        rig.Api.DirectForeground = false;
        rig.Api.ForegroundAttached = true;

        rig.Executor.Execute(new ActivateWindow(4, 100));

        Assert.Equal(["foreground", "alt", "foreground", "attach"], rig.Api.Calls);
        Assert.Equal(new CommandCompleted(4), Assert.Single(rig.Posted));
    }

    [Fact]
    public void Alt_retry_success_skips_attach()
    {
        var rig = new Rig();
        rig.Api.DirectForeground = false;
        rig.Api.ForegroundAfterAlt = true;

        rig.Executor.Execute(new ActivateWindow(5, 100));

        Assert.DoesNotContain("attach", rig.Api.Calls);
        Assert.Equal(new CommandCompleted(5), Assert.Single(rig.Posted));
    }

    [Fact]
    public void Refusal_after_alt_and_attach_is_foreground_denied()
    {
        var rig = new Rig();
        rig.Api.DirectForeground = false;

        rig.Executor.Execute(new ActivateWindow(6, 100));

        Assert.Equal(new CommandFailed(6, ErrorCode.ForegroundDenied), Assert.Single(rig.Posted));
        Assert.Equal("ForegroundDenied", Assert.Single(rig.Log.Entries).Data!["result"]);
    }

    [Fact]
    public void Show_shell_shows_raises_async_and_takes_foreground()
    {
        var rig = new Rig();

        rig.Executor.Execute(new ShowShell(7));

        Assert.Equal(["show:Show", "raise", "foreground"], rig.Api.Calls);
        Assert.Equal(new CommandCompleted(7), Assert.Single(rig.Posted));
        Assert.Equal(200L, Assert.Single(rig.Log.Entries).Data!["hwnd"]);
    }

    [Fact]
    public void Show_shell_restores_iconic_shell()
    {
        var rig = new Rig();
        rig.Api.Iconic.Add(200);

        rig.Executor.Execute(new ShowShell(8));

        Assert.Equal(WindowShow.Restore, rig.Api.Shows[0]);
    }

    [Fact]
    public void Show_shell_without_handle_is_window_not_found()
    {
        var rig = new Rig { Shell = null };

        rig.Executor.Execute(new ShowShell(9));

        Assert.Equal(new CommandFailed(9, ErrorCode.WindowNotFound), Assert.Single(rig.Posted));
        Assert.False(Assert.Single(rig.Log.Entries).Data!.ContainsKey("hwnd"));
    }

    [Fact]
    public void Api_exception_becomes_unexpected_failure_and_does_not_throw()
    {
        var rig = new Rig();
        var throwing = new ThrowingApi();
        var executor = new WindowCommandExecutor(throwing, rig.Log, rig.Posted.Add, () => null, static _ => { });

        executor.Execute(new MinimizeWindow(10, 100));

        Assert.Equal(new CommandFailed(10, ErrorCode.Unexpected), Assert.Single(rig.Posted));
        var entry = Assert.Single(rig.Log.Entries, e => e.Msg == "windowCommand");
        Assert.Equal(100L, entry.Data!["hwnd"]);
        Assert.Equal("Unexpected", entry.Data["result"]);
    }

    [Fact]
    public void Restore_that_clears_after_a_few_polls_takes_foreground()
    {
        var rig = new Rig();
        rig.Api.Iconic.Add(100);
        rig.Api.PollsUntilRestored = 3;

        rig.Executor.Execute(new ActivateWindow(11, 100));

        Assert.Contains("foreground", rig.Api.Calls);
        Assert.Equal(new CommandCompleted(11), Assert.Single(rig.Posted));
    }

    [Fact]
    public void Restore_that_never_clears_is_unexpected_and_skips_foreground()
    {
        var rig = new Rig();
        rig.Api.Iconic.Add(100);
        rig.Api.PollsUntilRestored = 1000;

        rig.Executor.Execute(new ActivateWindow(12, 100));

        Assert.DoesNotContain("foreground", rig.Api.Calls);
        Assert.Equal(new CommandFailed(12, ErrorCode.Unexpected), Assert.Single(rig.Posted));
        Assert.Equal("Unexpected", Assert.Single(rig.Log.Entries).Data!["result"]);
    }

    [Theory]
    [InlineData("minimize")]
    [InlineData("activate")]
    [InlineData("shell")]
    public void Show_refused_by_the_system_is_unexpected_and_logged(string kind)
    {
        var rig = new Rig();
        rig.Api.ShowResult = false;
        if (kind == "activate")
        {
            rig.Api.Iconic.Add(100);
        }

        WindowCommand command = kind switch
        {
            "minimize" => new MinimizeWindow(13, 100),
            "activate" => new ActivateWindow(13, 100),
            _ => new ShowShell(13),
        };

        rig.Executor.Execute(command);

        Assert.Equal(new CommandFailed(13, ErrorCode.Unexpected), Assert.Single(rig.Posted));
        Assert.Equal("Unexpected", Assert.Single(rig.Log.Entries).Data!["result"]);
    }

    [Fact]
    public void Shell_handle_of_a_dead_window_is_window_not_found_and_logs_hwnd()
    {
        var rig = new Rig { Shell = 999 };

        rig.Executor.Execute(new ShowShell(14));

        Assert.Equal(new CommandFailed(14, ErrorCode.WindowNotFound), Assert.Single(rig.Posted));
        var entry = Assert.Single(rig.Log.Entries);
        Assert.Equal(999L, entry.Data!["hwnd"]);
        Assert.Equal("WindowNotFound", entry.Data["result"]);
    }

    private sealed class ThrowingApi : IWindowApi
    {
        public bool IsWindow(long hwnd) => throw new InvalidOperationException("boom");

        public bool IsIconic(long hwnd) => false;

        public bool IsVisible(long hwnd) => false;

        public bool ShowAsync(long hwnd, WindowShow show) => false;

        public bool RaiseAsync(long hwnd) => false;

        public bool SetForeground(long hwnd) => false;

        public bool SimulateAlt() => false;

        public bool SetForegroundAttached(long hwnd) => false;

        public IReadOnlyList<WindowInfo> EnumerateTopLevel() => [];
    }
}
