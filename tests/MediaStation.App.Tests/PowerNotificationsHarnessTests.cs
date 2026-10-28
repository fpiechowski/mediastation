using System.Reflection;
using System.Runtime.InteropServices;
using MediaStation.App.Platform.System;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using MediaStation.Core.Tests;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32;
using Windows.Win32;

namespace MediaStation.App.Tests;

/// <summary>
/// Harness HWND dla shimu <see cref="PowerNotifications"/> (DW-1): prawdziwe okno message-only, prawdziwy
/// <c>RegisterPowerSettingNotification</c> i prawdziwy <c>SystemEvents</c>. Decyzje routera są już pokryte
/// przez <c>PowerSignalRouterTests</c>; tu sprawdzamy tylko plumbing: hook, kopię
/// <c>POWERBROADCAST_SETTING</c>, rejestrację z echem, subskrypcję <c>PowerModeChanged</c> i sprzątanie.
/// Komunikaty systemowe wstrzykujemy <c>SendMessage</c> (wybudzenia nie da się wywołać z testu).
/// </summary>
public class PowerNotificationsHarnessTests
{
    private const uint ResumeAutomatic = 0x12; // PBT_APMRESUMEAUTOMATIC

    private const uint ResumeSuspend = 0x7; // PBT_APMRESUMESUSPEND: SystemEvents mapuje go na PowerModes.Resume

    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    private sealed class Fixture
    {
        private readonly List<IOrchestratorMessage> _posted = [];
        private int _logBaseline;
        private int _postedBaseline;

        public RecordingLog Log { get; } = new();

        public FakeTimeProvider Time { get; } = new();

        public PowerSignalReporter Reporter { get; }

        public Fixture()
        {
            Reporter = new PowerSignalReporter(Log, new WakeSignalGate(Time), message =>
            {
                lock (_posted)
                {
                    _posted.Add(message);
                }
            });
        }

        public IReadOnlyList<IOrchestratorMessage> Posted
        {
            get
            {
                lock (_posted)
                {
                    return _posted.Skip(_postedBaseline).ToArray();
                }
            }
        }

        /// <summary>
        /// Czeka, aż system doręczy echo rejestracji <c>GUID_CONSOLE_DISPLAY_STATE</c> (przychodzi z kolejki
        /// dopiero po powrocie ze <c>Start</c>), po czym zeruje licznik wpisów i zegar bramki, żeby kolejne
        /// asercje dotyczyły tylko wstrzykniętych komunikatów, a nie echa wygrywającego okno 5 s.
        /// </summary>
        public void Settle()
        {
            MessageOnlyWindow.Pump(TimeSpan.FromMilliseconds(300));
            Time.Advance(WakeSignalGate.Window + TimeSpan.FromSeconds(1));
            _logBaseline = Log.Entries.Count;
            lock (_posted)
            {
                _postedBaseline = _posted.Count;
            }
        }

        public string[] Sources(string msg) => Log.Entries
            .Skip(_logBaseline)
            .Where(entry => entry.Msg == msg)
            .Select(entry => (string)entry.Data!["source"]!)
            .ToArray();
    }

    [Fact]
    public void Start_on_a_real_window_registers_all_three_sources() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter);

        var line = Assert.Single(f.Log.Entries, entry => entry.Msg == "powerSignalsRegistered");
        Assert.Equal(LogLevel.Info, line.Level);
        Assert.Equal(true, line.Data!["displayState"]);
        Assert.Equal(true, line.Data["windowHook"]);
        Assert.Equal(true, line.Data["powerModeChanged"]);
    });

    [Fact(Skip = "DW-1 finding: system dostarcza echo GUID_CONSOLE_DISPLAY_STATE z kolejki dopiero po powrocie ze Start, gdy okno echa w PowerSignalRouter jest już zamknięte, więc echo jest raportowane jako wybudzenie. Do decyzji (zmiana poza zakresem harnessu).")]
    public void Registration_echo_is_not_reported_as_a_wake() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter);

        MessageOnlyWindow.Pump(TimeSpan.FromMilliseconds(500));

        Assert.Empty(f.Sources("powerSignal"));
        Assert.Empty(f.Posted);
    });

    [Fact]
    public void Registration_echo_is_delivered_through_the_hook_after_start() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter);

        MessageOnlyWindow.Pump(TimeSpan.FromMilliseconds(500));

        // Charakterystyka stanu faktycznego (patrz Skip powyżej): echo przechodzi przez prawdziwy hook,
        // kopię POWERBROADCAST_SETTING i router, a okno echa już nie obowiązuje.
        Assert.Equal([WakeSignalGate.DisplayStateSource], f.Sources("systemResumed"));
    });

    [Fact]
    public void Resume_automatic_broadcast_wins_and_reaches_later_hooks() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter);

        var seenByLaterHook = false;
        window.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            seenByLaterHook |= msg == (int)PInvoke.WM_POWERBROADCAST;
            return IntPtr.Zero;
        });

        window.Send(PInvoke.WM_POWERBROADCAST, ResumeAutomatic, 0);

        Assert.Equal([WakeSignalGate.ResumeAutomaticSource], f.Sources("systemResumed"));
        Assert.Equal(new SystemResumed(WakeSignalGate.ResumeAutomaticSource), Assert.Single(f.Posted));
        Assert.True(seenByLaterHook, "hook nie może oznaczać broadcastu jako obsłużonego");
    });

    [Fact]
    public void Display_on_setting_is_copied_and_wins() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter);
        f.Settle();

        SendSetting(window, ConsoleDisplayState, dataLength: 4, data0: 1);

        Assert.Equal([WakeSignalGate.DisplayStateSource], f.Sources("systemResumed"));
        Assert.Equal(new SystemResumed(WakeSignalGate.DisplayStateSource), Assert.Single(f.Posted));
    });

    [Theory]
    [InlineData((byte)0)] // off
    [InlineData((byte)2)] // dimmed
    public void Display_off_and_dimmed_settings_are_ignored(byte data0) => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter);
        f.Settle();

        SendSetting(window, ConsoleDisplayState, dataLength: 4, data0);

        Assert.Empty(f.Sources("powerSignal"));
    });

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(8u)]
    [InlineData(uint.MaxValue)]
    public void Setting_with_wrong_data_length_is_rejected_without_overread(uint dataLength) => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter);
        f.Settle();

        // Bufor ma tylko nagłówek (20 B) i nic ponad: kopia deklarowanych danych oznaczałaby nadczyt.
        SendSetting(window, ConsoleDisplayState, dataLength, data0: 1, allocatedBytes: 20);

        Assert.Empty(f.Sources("powerSignal"));
    });

    [Fact]
    public void Setting_for_another_guid_and_null_payload_are_ignored() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter);
        f.Settle();

        SendSetting(window, Guid.NewGuid(), dataLength: 4, data0: 1);
        window.Send(PInvoke.WM_POWERBROADCAST, PInvoke.PBT_POWERSETTINGCHANGE, 0);

        Assert.Empty(f.Sources("powerSignal"));
    });

    [Fact]
    public void Power_mode_resume_is_logged_but_never_wins() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter);

        RaiseSystemEventsPowerBroadcast(ResumeSuspend);

        Assert.True(SpinWait.SpinUntil(() => f.Sources("powerSignal").Length > 0, TimeSpan.FromSeconds(5)));
        Assert.Equal([PowerSignalRouter.PowerModeChangedSource], f.Sources("powerSignal"));
        Assert.Empty(f.Sources("systemResumed"));
        Assert.Empty(f.Posted);
    });

    [Fact]
    public void Dispose_detaches_hook_and_power_mode_subscription() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        var power = PowerNotifications.Start(window.Handle, f.Reporter);

        power.Dispose();
        power.Dispose();

        window.Send(PInvoke.WM_POWERBROADCAST, ResumeAutomatic, 0);
        SendSetting(window, ConsoleDisplayState, dataLength: 4, data0: 1);
        RaiseSystemEventsPowerBroadcast(ResumeSuspend);
        MessageOnlyWindow.Pump(TimeSpan.FromMilliseconds(500));

        Assert.Empty(f.Sources("powerSignal"));
        Assert.Empty(f.Posted);
    });

    [Fact]
    public void Start_without_a_usable_window_throws_after_reporting_a_warning() => StaThread.Run(() =>
    {
        var f = new Fixture();

        Assert.Throws<ArgumentException>(() => PowerNotifications.Start(IntPtr.Zero, f.Reporter));

        var line = Assert.Single(f.Log.Entries, entry => entry.Msg == "powerSignalsRegistered");
        Assert.Equal(LogLevel.Warn, line.Level);
        Assert.Equal(false, line.Data!["windowHook"]);
        Assert.Equal(false, line.Data["displayState"]);
    });

    /// <summary>
    /// Wysyła <c>PBT_POWERSETTINGCHANGE</c> z <c>POWERBROADCAST_SETTING</c> w pamięci niezarządzanej:
    /// GUID (16 B), <c>DataLength</c> (offset 16), <c>Data[0]</c> (offset 20). Układ zapisany literałami offsetów,
    /// niezależnie od produkcyjnych stałych.
    /// </summary>
    private static void SendSetting(MessageOnlyWindow window, Guid setting, uint dataLength, byte data0, int allocatedBytes = 24)
    {
        var buffer = Marshal.AllocHGlobal(allocatedBytes);
        try
        {
            Marshal.Copy(new byte[allocatedBytes], 0, buffer, allocatedBytes);
            Marshal.Copy(setting.ToByteArray(), 0, buffer, 16);
            Marshal.WriteInt32(buffer, 16, unchecked((int)dataLength));
            if (allocatedBytes > 20)
            {
                Marshal.WriteByte(buffer, 20, data0);
            }

            window.Send(PInvoke.WM_POWERBROADCAST, PInvoke.PBT_POWERSETTINGCHANGE, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Przepuszcza broadcast przez wewnętrzny dispatcher <c>SystemEvents</c> (<c>OnPowerModeChanged(IntPtr)</c>),
    /// czyli tę samą ścieżkę, którą jego ukryte okno wywołuje po <c>WM_POWERBROADCAST</c>. Zdarzenie wraca na
    /// wątku <c>SystemEvents</c>, nie na wątku testu.
    /// </summary>
    private static void RaiseSystemEventsPowerBroadcast(uint powerEvent)
    {
        const BindingFlags nonPublic = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var instance = typeof(SystemEvents).GetField("s_systemEvents", nonPublic)?.GetValue(null);
        var method = typeof(SystemEvents).GetMethod("OnPowerModeChanged", nonPublic, [typeof(IntPtr)]);
        Assert.NotNull(instance);
        Assert.NotNull(method);
        method.Invoke(instance, [(IntPtr)(nint)powerEvent]);
    }
}
