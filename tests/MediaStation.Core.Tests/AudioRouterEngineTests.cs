using MediaStation.App.Platform.Audio;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>
/// Silnik AudioRoutera bez COM: reguła drzewa, przypisanie sesji po łańcuchu PID, wybór wyciszenia (mute albo
/// kanały), nowe sesje w wyciszonym i odciszonym drzewie oraz brak usługi audio.
/// </summary>
public class AudioRouterEngineTests
{
    // Drzewo: 100 (korzeń) → 200 → 300; obcy proces: 900 (rodzic 1).
    private static readonly KeyValuePair<int, int>[] Processes =
    [
        new(1, 0), new(100, 1), new(200, 100), new(300, 200), new(900, 1),
    ];

    [Fact]
    public void Mute_marks_session_in_a_grandchild_and_leaves_a_foreign_session_alone()
    {
        var (engine, backend, log) = Create();
        var inTree = backend.Add("a", pid: 300);
        var foreign = backend.Add("b", pid: 900);

        var error = engine.Execute(new MuteProcessTree(1, 100));

        Assert.Null(error);
        Assert.True(inTree.Muted);
        Assert.False(foreign.Muted);
        var applied = Assert.Single(log.Entries, entry => entry.Msg == "audioRuleApplied");
        Assert.Equal(LogLevel.Info, applied.Level);
        Assert.Equal(100, applied.Data!["rootPid"]);
        Assert.Equal(true, applied.Data["muted"]);
        Assert.Equal(1, applied.Data["sessions"]);
        var muted = Assert.Single(log.Entries, entry => entry.Msg == "audioSessionMuted");
        Assert.Equal(100, muted.Data!["rootPid"]);
        Assert.Equal(300, muted.Data["pid"]);
    }

    [Fact]
    public void Mute_covers_the_root_process_itself()
    {
        var (engine, backend, _) = Create();
        var root = backend.Add("a", pid: 100);

        engine.Execute(new MuteProcessTree(1, 100));

        Assert.True(root.Muted);
    }

    [Fact]
    public void Unmute_restores_only_sessions_the_engine_muted()
    {
        var (engine, backend, log) = Create();
        var ours = backend.Add("a", pid: 300);
        var foreign = backend.Add("b", pid: 900);
        foreign.Muted = true;
        engine.Execute(new MuteProcessTree(1, 100));

        var error = engine.Execute(new UnmuteProcessTree(2, 100));

        Assert.Null(error);
        Assert.False(ours.Muted);
        Assert.True(foreign.Muted);
        var applied = log.Entries.Last(entry => entry.Msg == "audioRuleApplied");
        Assert.Equal(false, applied.Data!["muted"]);
        Assert.Equal(1, applied.Data["sessions"]);
    }

    [Fact]
    public void New_session_in_a_muted_tree_is_muted_on_the_next_sweep()
    {
        var (engine, backend, log) = Create();
        engine.Execute(new MuteProcessTree(1, 100));
        var created = backend.Add("late", pid: 300);

        var sweep = engine.Sweep();

        Assert.False(sweep.Unavailable);
        Assert.True(created.Muted);
        Assert.Contains(log.Entries, entry => entry.Msg == "audioSessionMuted" && Equals(entry.Data!["pid"], 300));
    }

    [Fact]
    public void New_session_in_an_unmuted_tree_is_left_alone()
    {
        var (engine, backend, log) = Create();
        engine.Execute(new MuteProcessTree(1, 100));
        engine.Execute(new UnmuteProcessTree(2, 100));
        var created = backend.Add("late", pid: 300);

        engine.Sweep();

        Assert.False(created.Muted);
        Assert.Equal(0, created.SetCalls);
        Assert.DoesNotContain(log.Entries, entry => entry.Msg == "audioSessionMuted");
    }

    [Fact]
    public void New_session_in_a_tree_without_a_rule_is_left_alone()
    {
        var (engine, backend, _) = Create();
        var created = backend.Add("late", pid: 300);

        engine.Sweep();

        Assert.Equal(0, created.SetCalls);
    }

    [Fact]
    public void Missing_simple_volume_falls_back_to_channels_and_unmute_restores_remembered_levels()
    {
        var (engine, backend, _) = Create();
        var session = backend.Add("a", pid: 300);
        session.SimpleVolume = false;
        session.Channels = [0.8f, 0.5f];

        engine.Execute(new MuteProcessTree(1, 100));

        Assert.Equal([0f, 0f], session.Channels);
        Assert.False(session.Muted);

        engine.Execute(new UnmuteProcessTree(2, 100));

        Assert.Equal([0.8f, 0.5f], session.Channels);
    }

    [Fact]
    public void Channel_levels_are_remembered_once_across_repeated_sweeps()
    {
        var (engine, backend, _) = Create();
        var session = backend.Add("a", pid: 300);
        session.SimpleVolume = false;
        session.Channels = [0.3f, 0.3f];
        engine.Execute(new MuteProcessTree(1, 100));

        engine.Sweep();
        engine.Sweep();
        engine.Execute(new UnmuteProcessTree(2, 100));

        Assert.Equal([0.3f, 0.3f], session.Channels);
    }

    [Fact]
    public void Session_without_either_interface_is_skipped_and_logged_once()
    {
        var (engine, backend, log) = Create();
        var session = backend.Add("a", pid: 300);
        session.SimpleVolume = false;
        session.Channels = null;

        var error = engine.Execute(new MuteProcessTree(1, 100));
        engine.Sweep();
        engine.Sweep();

        Assert.Null(error);
        Assert.False(session.Muted);
        var skipped = Assert.Single(log.Entries, entry => entry.Msg == "audioSessionSkipped");
        Assert.Equal(LogLevel.Warn, skipped.Level);
        Assert.Equal(300, skipped.Data!["pid"]);
        Assert.DoesNotContain(log.Entries, entry => entry.Msg == "audioSessionMuted");
    }

    [Fact]
    public void Failing_session_does_not_stop_the_sweep_for_other_sessions()
    {
        var (engine, backend, log) = Create();
        var broken = backend.Add("a", pid: 300);
        broken.Throws = true;
        var healthy = backend.Add("b", pid: 200);

        var error = engine.Execute(new MuteProcessTree(1, 100));

        Assert.Null(error);
        Assert.True(healthy.Muted);
        Assert.Single(log.Entries, entry => entry.Msg == "audioSessionFailed");
    }

    [Fact]
    public void Unavailable_audio_service_fails_the_command_but_keeps_the_rule()
    {
        var (engine, backend, log) = Create();
        var session = backend.Add("a", pid: 300);
        backend.EnumerateThrows = true;

        var error = engine.Execute(new MuteProcessTree(1, 100));

        Assert.Equal(ErrorCode.AudioUnavailable, error);
        Assert.True(engine.AnyRule);
        Assert.Single(log.Entries, entry => entry.Msg == "audioUnavailable");
        Assert.DoesNotContain(log.Entries, entry => entry.Msg == "audioRuleApplied");

        backend.EnumerateThrows = false;
        var sweep = engine.Sweep();

        Assert.False(sweep.Unavailable);
        Assert.True(session.Muted);
    }

    [Fact]
    public void Repeated_unavailable_sweeps_log_once()
    {
        var (engine, backend, log) = Create();
        backend.EnumerateThrows = true;

        engine.Sweep();
        engine.Sweep();

        Assert.Single(log.Entries, entry => entry.Msg == "audioUnavailable");
    }

    [Fact]
    public void Session_with_unknown_pid_is_reported_unresolved_only_while_a_tree_is_muted()
    {
        var (engine, backend, _) = Create();
        backend.Add("fresh", pid: 4000);

        Assert.False(engine.Sweep().Unresolved);

        engine.Execute(new MuteProcessTree(1, 100));

        Assert.True(engine.LastSweepUnresolved);
    }

    [Fact]
    public void Unresolved_session_is_muted_once_its_pid_appears_in_the_process_snapshot()
    {
        var (engine, backend, _) = Create();
        var fresh = backend.Add("fresh", pid: 4000);
        engine.Execute(new MuteProcessTree(1, 100));
        Assert.False(fresh.Muted);

        backend.Processes = [.. Processes, new KeyValuePair<int, int>(4000, 300)];
        var sweep = engine.Sweep();

        Assert.False(sweep.Unresolved);
        Assert.True(fresh.Muted);
    }

    [Fact]
    public void System_sounds_session_is_never_unresolved_or_muted()
    {
        var (engine, backend, _) = Create();
        var system = backend.Add("sys", pid: 0);

        engine.Execute(new MuteProcessTree(1, 100));

        Assert.False(engine.LastSweepUnresolved);
        Assert.False(system.Muted);
    }

    [Fact]
    public void Rule_of_a_dead_root_is_dropped_so_a_reused_pid_is_not_muted()
    {
        var (engine, backend, _) = Create();
        engine.Execute(new MuteProcessTree(1, 100));
        backend.Processes = [new KeyValuePair<int, int>(1, 0), new KeyValuePair<int, int>(900, 1)];
        var reused = backend.Add("reused", pid: 900);

        engine.Sweep();

        Assert.False(engine.AnyRule);
        Assert.False(reused.Muted);
    }

    [Fact]
    public void Sessions_muted_under_a_dead_root_are_restored_instead_of_staying_muted()
    {
        var (engine, backend, _) = Create();
        var survivor = backend.Add("child", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));
        Assert.True(survivor.Muted);
        backend.Processes = [new KeyValuePair<int, int>(1, 0), new KeyValuePair<int, int>(200, 1), new KeyValuePair<int, int>(300, 200)];

        engine.Sweep();

        Assert.False(survivor.Muted);
        Assert.False(engine.HasAssignedSessions);
    }

    [Fact]
    public void Failed_unmute_keeps_pending_sessions_so_the_port_keeps_scanning()
    {
        var (engine, backend, _) = Create();
        var session = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));
        backend.EnumerateThrows = true;

        var error = engine.Execute(new UnmuteProcessTree(2, 100));

        Assert.Equal(ErrorCode.AudioUnavailable, error);
        Assert.True(engine.AnyRule);
        Assert.True(engine.HasAssignedSessions);
        Assert.True(session.Muted);

        backend.EnumerateThrows = false;
        engine.Sweep();

        Assert.False(session.Muted);
    }

    [Fact]
    public void Dispose_recreates_a_dropped_backend_to_restore_muted_sessions()
    {
        var (engine, backend, _) = Create();
        var session = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));
        backend.EnumerateThrows = true;
        engine.Sweep();
        backend.EnumerateThrows = false;

        engine.Dispose();

        Assert.False(session.Muted);
    }

    [Fact]
    public void Vanished_session_is_forgotten_so_a_new_one_with_the_same_key_is_muted_again()
    {
        var (engine, backend, _) = Create();
        var first = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));
        backend.Sessions.Clear();
        engine.Sweep();
        var second = backend.Add("a", pid: 300);

        engine.Sweep();

        Assert.True(first.Muted);
        Assert.True(second.Muted);
    }

    [Fact]
    public void Every_enumerated_session_is_released()
    {
        var (engine, backend, _) = Create();
        var a = backend.Add("a", pid: 300);
        var b = backend.Add("b", pid: 900);

        engine.Execute(new MuteProcessTree(1, 100));

        Assert.True(a.Disposed);
        Assert.True(b.Disposed);
    }

    [Fact]
    public void Dispose_restores_what_the_engine_muted_and_releases_the_backend()
    {
        var (engine, backend, _) = Create();
        var session = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));

        engine.Dispose();

        Assert.False(session.Muted);
        Assert.True(backend.Disposed);
    }

    [Fact]
    public void Root_pid_zero_is_ignored_without_failing()
    {
        var (engine, backend, log) = Create();
        var session = backend.Add("a", pid: 300);

        var error = engine.Execute(new MuteProcessTree(1, 0));

        Assert.Null(error);
        Assert.False(session.Muted);
        Assert.Single(log.Entries, entry => entry.Msg == "audioRuleIgnored");
    }

    [Fact]
    public void Forget_removes_the_rule_and_restores_what_the_engine_muted()
    {
        var (engine, backend, log) = Create();
        var ours = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));

        var error = engine.Execute(new ForgetProcessTree(2, 100));

        Assert.Null(error);
        Assert.False(ours.Muted);
        Assert.False(engine.AnyRule);
        Assert.False(engine.HasAssignedSessions);
        var forgotten = Assert.Single(log.Entries, entry => entry.Msg == "audioRuleForgotten");
        Assert.Equal(100, forgotten.Data!["rootPid"]);
    }

    [Fact]
    public void Session_of_a_new_process_with_the_forgotten_pid_is_not_muted()
    {
        var (engine, backend, _) = Create();
        engine.Execute(new MuteProcessTree(1, 100));
        engine.Execute(new ForgetProcessTree(2, 100));
        var created = backend.Add("reborn", pid: 300);

        engine.Sweep();

        Assert.False(created.Muted);
        Assert.Equal(0, created.SetCalls);
    }

    [Fact]
    public void Forget_of_an_unknown_root_succeeds_and_leaves_other_rules()
    {
        var (engine, backend, _) = Create();
        var foreign = backend.Add("b", pid: 900);
        engine.Execute(new MuteProcessTree(1, 900));

        var error = engine.Execute(new ForgetProcessTree(2, 100));

        Assert.Null(error);
        Assert.True(foreign.Muted);
    }

    [Fact]
    public void Forget_with_unavailable_backend_fails_but_drops_the_rule_for_the_next_sweep()
    {
        var (engine, backend, log) = Create();
        var ours = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));
        Assert.True(ours.Muted);
        backend.EnumerateThrows = true;

        var error = engine.Execute(new ForgetProcessTree(2, 100));

        Assert.Equal(ErrorCode.AudioUnavailable, error);
        Assert.DoesNotContain(log.Entries, entry => entry.Msg == "audioRuleForgotten");
        Assert.False(engine.AnyRule);

        backend.EnumerateThrows = false;
        var reborn = backend.Add("reborn", pid: 300 + 0);
        var sweep = engine.Sweep();

        Assert.False(sweep.Unavailable);
        Assert.False(ours.Muted);
        Assert.False(reborn.Muted);
        Assert.False(engine.HasAssignedSessions);
    }

    [Fact]
    public void Forget_with_root_pid_zero_is_ignored_without_failing()
    {
        var (engine, _, log) = Create();

        var error = engine.Execute(new ForgetProcessTree(1, 0));

        Assert.Null(error);
        Assert.Single(log.Entries, entry => entry.Msg == "audioRuleIgnored");
    }

    [Fact]
    public void Backend_is_created_lazily_and_again_after_a_failure()
    {
        var log = new RecordingLog();
        var created = 0;
        var backend = new FakeBackend();
        var failFirst = true;
        using var engine = new AudioRouterEngine(
            () =>
            {
                created++;
                if (failFirst)
                {
                    failFirst = false;
                    throw new InvalidOperationException("brak usługi");
                }

                return backend;
            },
            () => backend.Processes,
            log);
        Assert.Equal(0, created);

        Assert.Equal(ErrorCode.AudioUnavailable, engine.Execute(new MuteProcessTree(1, 100)));
        Assert.Null(engine.Execute(new UnmuteProcessTree(2, 100)));

        Assert.Equal(2, created);
    }

    [Fact]
    public void Mixer_unmute_under_a_mute_rule_is_corrected_with_one_overridden_entry()
    {
        var (engine, backend, log) = Create();
        var session = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));

        session.Muted = false;
        engine.Sweep();
        engine.Sweep();
        session.Muted = false;
        engine.Sweep();

        Assert.True(session.Muted);
        var overridden = Assert.Single(log.Entries, entry => entry.Msg == "audioSessionOverridden");
        Assert.Equal(100, overridden.Data!["rootPid"]);
        Assert.Equal(300, overridden.Data["pid"]);
        Assert.Equal(true, overridden.Data["muted"]);
    }

    [Fact]
    public void Mixer_mute_under_an_unmute_rule_is_corrected_and_reported_per_rule_value()
    {
        var (engine, backend, log) = Create();
        var session = backend.Add("a", pid: 300);
        engine.Execute(new UnmuteProcessTree(1, 100));

        session.Muted = true;
        engine.Sweep();

        Assert.False(session.Muted);
        var overridden = Assert.Single(log.Entries, entry => entry.Msg == "audioSessionOverridden");
        Assert.Equal(false, overridden.Data!["muted"]);

        // Zmiana wartości reguły to zwykłe zastosowanie, a nowe nadpisanie znów dostaje wpis.
        engine.Execute(new MuteProcessTree(2, 100));
        Assert.Single(log.Entries, entry => entry.Msg == "audioSessionOverridden");
        session.Muted = false;
        engine.Sweep();

        Assert.True(session.Muted);
        Assert.Equal(2, log.Entries.Count(entry => entry.Msg == "audioSessionOverridden"));
    }

    [Fact]
    public void Rule_change_by_command_is_not_reported_as_an_override()
    {
        var (engine, backend, log) = Create();
        var session = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));

        engine.Execute(new UnmuteProcessTree(2, 100));

        Assert.False(session.Muted);
        Assert.DoesNotContain(log.Entries, entry => entry.Msg == "audioSessionOverridden");
    }

    [Fact]
    public void Session_muted_outside_the_engine_is_unmuted_by_an_unmute_rule_without_an_override_entry()
    {
        var (engine, backend, log) = Create();
        var inherited = backend.Add("a", pid: 300);
        inherited.Muted = true;
        var foreign = backend.Add("b", pid: 900);
        foreign.Muted = true;
        var system = backend.Add("sys", pid: 0);
        system.Muted = true;

        engine.Execute(new UnmuteProcessTree(1, 100));

        Assert.False(inherited.Muted);
        Assert.True(foreign.Muted);
        Assert.True(system.Muted);
        Assert.DoesNotContain(log.Entries, entry => entry.Msg == "audioSessionOverridden");
    }

    [Fact]
    public void Channel_fallback_without_remembered_levels_unmutes_to_full_volume()
    {
        var (engine, backend, _) = Create();
        var session = backend.Add("a", pid: 300);
        session.SimpleVolume = false;
        session.Channels = [0f, 0f];

        engine.Execute(new UnmuteProcessTree(1, 100));

        Assert.Equal([1f, 1f], session.Channels);
    }

    [Fact]
    public void Channel_fallback_raised_from_outside_is_muted_again_and_levels_are_remembered_anew()
    {
        var (engine, backend, log) = Create();
        var session = backend.Add("a", pid: 300);
        session.SimpleVolume = false;
        session.Channels = [0.8f, 0.8f];
        engine.Execute(new MuteProcessTree(1, 100));

        session.Channels = [0.4f, 0.6f];
        engine.Sweep();

        Assert.Equal([0f, 0f], session.Channels);
        var overridden = Assert.Single(log.Entries, entry => entry.Msg == "audioSessionOverridden");
        Assert.Equal(true, overridden.Data!["muted"]);

        engine.Execute(new UnmuteProcessTree(2, 100));

        Assert.Equal([0.4f, 0.6f], session.Channels);
    }

    [Fact]
    public void Forget_unmutes_a_session_muted_from_outside_and_empties_the_registry()
    {
        var (engine, backend, _) = Create();
        var session = backend.Add("a", pid: 300);
        var foreign = backend.Add("b", pid: 900);
        engine.Execute(new UnmuteProcessTree(1, 100));
        session.Muted = true;
        foreign.Muted = true;

        engine.Execute(new ForgetProcessTree(2, 100));

        Assert.False(session.Muted);
        Assert.True(foreign.Muted);
        Assert.False(engine.HasAssignedSessions);
        var calls = session.SetCalls;
        engine.Sweep();
        Assert.Equal(calls, session.SetCalls);
    }

    [Fact]
    public void Dispose_unmutes_assigned_sessions_but_not_foreign_ones()
    {
        var (engine, backend, _) = Create();
        var session = backend.Add("a", pid: 300);
        var foreign = backend.Add("b", pid: 900);
        foreign.Muted = true;
        engine.Execute(new UnmuteProcessTree(1, 100));
        session.Muted = true;

        engine.Dispose();

        Assert.False(session.Muted);
        Assert.True(foreign.Muted);
    }

    [Fact]
    public void Compliant_sessions_are_not_touched_by_repeated_sweeps()
    {
        var (engine, backend, _) = Create();
        var session = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));
        var calls = session.SetCalls;

        engine.Sweep();
        engine.Sweep();

        Assert.Equal(calls, session.SetCalls);
        Assert.True(engine.AnyRule);
        Assert.True(engine.HasAssignedSessions);
    }

    [Fact]
    public void Needs_sweeping_while_a_rule_or_assigned_session_exists()
    {
        var (engine, backend, _) = Create();
        backend.Add("a", pid: 300);
        Assert.False(engine.NeedsSweeping);

        engine.Execute(new UnmuteProcessTree(1, 100));
        Assert.True(engine.NeedsSweeping);

        engine.Execute(new ForgetProcessTree(2, 100));
        Assert.False(engine.NeedsSweeping);
    }

    [Fact]
    public void Empty_or_partial_process_snapshot_keeps_a_muted_child_muted()
    {
        var (engine, backend, _) = Create();
        var child = backend.Add("a", pid: 300);
        engine.Execute(new MuteProcessTree(1, 100));
        Assert.True(child.Muted);

        backend.Processes = [];
        engine.Sweep();
        Assert.True(child.Muted);
        Assert.True(engine.HasAssignedSessions);

        // Korzeń żyje, ale potomka chwilowo brak w migawce.
        backend.Processes = [new KeyValuePair<int, int>(1, 0), new KeyValuePair<int, int>(100, 1)];
        engine.Sweep();
        Assert.True(child.Muted);

        backend.Processes = Processes;
        engine.Sweep();
        Assert.True(child.Muted);
    }

    private static (AudioRouterEngine Engine, FakeBackend Backend, RecordingLog Log) Create()
    {
        var log = new RecordingLog();
        var backend = new FakeBackend { Processes = Processes };
        var engine = new AudioRouterEngine(() => backend, () => backend.Processes, log);
        return (engine, backend, log);
    }

    private sealed class FakeBackend : IAudioSessionBackend
    {
        public IReadOnlyList<KeyValuePair<int, int>> Processes { get; set; } = [];

        public List<FakeSession> Sessions { get; } = [];

        public bool EnumerateThrows { get; set; }

        public bool Disposed { get; private set; }

        public FakeSession Add(string key, int pid)
        {
            var session = new FakeSession(key, pid);
            Sessions.Add(session);
            return session;
        }

        public IReadOnlyList<IAudioSession> Enumerate()
        {
            if (EnumerateThrows)
            {
                throw new InvalidOperationException("usługa audio nie odpowiada");
            }

            return [.. Sessions];
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSession(string key, int pid) : IAudioSession
    {
        public string Key { get; } = key;

        public int Pid { get; } = pid;

        public bool SimpleVolume { get; set; } = true;

        public float[]? Channels { get; set; } = [1f, 1f];

        public bool Muted { get; set; }

        public bool Throws { get; set; }

        public int SetCalls { get; private set; }

        public bool Disposed { get; private set; }

        public bool? TryGetMute()
        {
            if (Throws)
            {
                throw new InvalidOperationException("sesja zniknęła");
            }

            return SimpleVolume ? Muted : null;
        }

        public bool TrySetMute(bool mute)
        {
            if (Throws)
            {
                throw new InvalidOperationException("sesja zniknęła");
            }

            if (!SimpleVolume)
            {
                return false;
            }

            SetCalls++;
            Muted = mute;
            return true;
        }

        public float[]? TryGetChannelVolumes() => Channels is null ? null : [.. Channels];

        public bool TrySetChannelVolumes(float[] levels)
        {
            if (Channels is null)
            {
                return false;
            }

            SetCalls++;
            Channels = [.. levels];
            return true;
        }

        public void Dispose() => Disposed = true;
    }
}
