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
        /// Czeka, aż system doręczy echo rejestracji <c>GUID_CONSOLE_DISPLAY_STATE</c> (router je zużywa, także
        /// po powrocie ze <c>Start</c>), po czym przesuwa zegar za oczekiwanie na echo i okno bramki oraz zeruje
        /// licznik wpisów, żeby kolejne asercje dotyczyły tylko wstrzykniętych komunikatów, nawet gdyby echo
        /// nie przyszło.
        /// </summary>
        public void Settle()
        {
            MessageOnlyWindow.Pump(TimeSpan.FromMilliseconds(300));
            Time.Advance(PowerSignalRouter.EchoWait + WakeSignalGate.Window);
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
        using var power = PowerNotifications.Start(window.Handle, f.Reporter, f.Time);

        var line = Assert.Single(f.Log.Entries, entry => entry.Msg == "powerSignalsRegistered");
        Assert.Equal(LogLevel.Info, line.Level);
        Assert.Equal(true, line.Data!["displayState"]);
        Assert.Equal(true, line.Data["windowHook"]);
        Assert.Equal(true, line.Data["powerModeChanged"]);
    });

    [Fact]
    public void Registration_echo_is_not_reported_as_a_wake() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = StartAndAwaitLateEcho(window, f);

        // Druga notyfikacja też ma tu zostać złapana, więc pompujemy dalej już po echu.
        MessageOnlyWindow.Pump(TimeSpan.FromMilliseconds(500));

        Assert.Empty(f.Sources("powerSignal"));
        Assert.Empty(f.Posted);
    });

    [Fact]
    public void Real_display_wake_right_after_the_echo_wins() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = StartAndAwaitLateEcho(window, f);

        // Bez przesuwania zegara: po zużytym echu wybudzenie nie może czekać na limit.
        SendSetting(window, ConsoleDisplayState, dataLength: 4, data0: 1);

        Assert.Equal([WakeSignalGate.DisplayStateSource], f.Sources("systemResumed"));
        Assert.Equal(new SystemResumed(WakeSignalGate.DisplayStateSource), Assert.Single(f.Posted));
    });

    [Fact]
    public void Resume_automatic_broadcast_wins_and_reaches_later_hooks() => StaThread.Run(() =>
    {
        var f = new Fixture();
        using var window = new MessageOnlyWindow();
        using var power = PowerNotifications.Start(window.Handle, f.Reporter, f.Time);

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
        using var power = PowerNotifications.Start(window.Handle, f.Reporter, f.Time);
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
        using var power = PowerNotifications.Start(window.Handle, f.Reporter, f.Time);
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
        using var power = PowerNotifications.Start(window.Handle, f.Reporter, f.Time);
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
        using var power = PowerNotifications.Start(window.Handle, f.Reporter, f.Time);
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
        using var power = PowerNotifications.Start(window.Handle, f.Reporter, f.Time);

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
        var power = PowerNotifications.Start(window.Handle, f.Reporter, f.Time);

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

        Assert.Throws<ArgumentException>(() => PowerNotifications.Start(IntPtr.Zero, f.Reporter, f.Time));

        var line = Assert.Single(f.Log.Entries, entry => entry.Msg == "powerSignalsRegistered");
        Assert.Equal(LogLevel.Warn, line.Level);
        Assert.Equal(false, line.Data!["windowHook"]);
        Assert.Equal(false, line.Data["displayState"]);
    });

    /// <summary>
    /// Start ścieżką późnego echa: po powrocie ze <c>Start</c> echa jeszcze nie ma (obserwator stoi przed
    /// <c>Start</c>, więc policzyłby je także w oknie rejestracji), a potem pompuje do jego doręczenia. Bez tego
    /// testy przechodziłyby też wtedy, gdy echo wpadło do okna albo w ogóle nie przyszło.
    /// </summary>
    private static PowerNotifications StartAndAwaitLateEcho(MessageOnlyWindow window, Fixture f)
    {
        EnsureSystemEventsInitialized();

        var settingChanges = 0;
        window.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == (int)PInvoke.WM_POWERBROADCAST && (uint)wParam.ToInt64() == PInvoke.PBT_POWERSETTINGCHANGE)
            {
                settingChanges++;
            }

            return IntPtr.Zero;
        });

        var power = PowerNotifications.Start(window.Handle, f.Reporter, f.Time);
        try
        {
            Assert.Equal(0, settingChanges);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (settingChanges < 1 && DateTime.UtcNow < deadline)
            {
                MessageOnlyWindow.Pump(TimeSpan.FromMilliseconds(20));
            }

            Assert.True(settingChanges >= 1, "system nie doręczył echa rejestracji");
            return power;
        }
        catch
        {
            power.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Inicjuje <c>SystemEvents</c> przed <c>Start</c>. Pierwsza subskrypcja czeka na wątek <c>SystemEvents</c>,
    /// a czekanie na wątku STA pompuje komunikaty, więc echo trafiałoby wtedy do okna rejestracji. Po inicjalizacji
    /// nic nie pompuje i echo przychodzi po powrocie ze <c>Start</c>, niezależnie od kolejności testów.
    /// </summary>
    private static void EnsureSystemEventsInitialized()
    {
        PowerModeChangedEventHandler noop = static (_, _) => { };
        SystemEvents.PowerModeChanged += noop;
        SystemEvents.PowerModeChanged -= noop;
    }

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
