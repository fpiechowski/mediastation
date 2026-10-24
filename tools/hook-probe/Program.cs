using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

// Spike 3.11, eksperyment 4: puste okno na wierzchu z hookiem WH_KEYBOARD_LL na wątku UI.
// Użycie: hook-probe.exe --log <plik.jsonl> [--seconds 20] [--rawinput | --raw-kbd | --raw-hid]  (RIDEV_INPUTSINK: oba | klawiatura | HID Consumer)
// Zapis: jedna linia JSON na zdarzenie ("start", "down", "up", "stop"). Kod wyjścia: 0 = hook zainstalowany,
// 2 = SetWindowsHookEx nie zadziałał, 3 = zły argument.
internal static class Program
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const uint LlkhfInjected = 0x10;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    private const uint RidevInputSink = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private static StreamWriter? _log;
    private static nint _hook;
    private static HookProc? _proc;

    [STAThread]
    private static int Main(string[] args)
    {
        string? logPath = null;
        var seconds = 20;
        var rawKeyboard = false;
        var rawHid = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--log" && i + 1 < args.Length)
            {
                logPath = args[++i];
            }
            else if (args[i] == "--rawinput" || args[i] == "--raw-kbd" || args[i] == "--raw-hid")
            {
                rawKeyboard |= args[i] != "--raw-hid";
                rawHid |= args[i] != "--raw-kbd";
            }
            else if (args[i] == "--seconds" && i + 1 < args.Length && int.TryParse(args[++i], out var s))
            {
                seconds = s;
            }
            else
            {
                return 3;
            }
        }

        if (logPath is null)
        {
            return 3;
        }

        _log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        System.Windows.Forms.Application.EnableVisualStyles();
        using var form = new System.Windows.Forms.Form
        {
            Text = "hook-probe",
            TopMost = true,
            Width = 360,
            Height = 160,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
        };

        _proc = Callback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == 0)
        {
            Write("error", ("op", "hook"), ("win32", Marshal.GetLastWin32Error()));
            return 2;
        }

        if (rawKeyboard || rawHid)
        {
            var list = new List<RawInputDevice>();
            if (rawKeyboard)
            {
                list.Add(new() { UsagePage = 0x01, Usage = 0x06, Flags = RidevInputSink, Target = form.Handle });
            }

            if (rawHid)
            {
                list.Add(new() { UsagePage = 0x0C, Usage = 0x01, Flags = RidevInputSink, Target = form.Handle });
            }

            var devices = list.ToArray();
            var registered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
            Write("rawInput", ("registered", registered), ("win32", registered ? 0 : Marshal.GetLastWin32Error()));
        }

        using var timer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        timer.Tick += (_, _) => form.Close();
        form.Shown += (_, _) =>
        {
            form.Activate();
            Write("start", ("pid", Environment.ProcessId), ("hwnd", (long)form.Handle), ("foreground", GetForegroundWindow() == form.Handle));
            timer.Start();
        };

        System.Windows.Forms.Application.Run(form);
        UnhookWindowsHookEx(_hook);
        Write("stop");
        _log.Dispose();
        return 0;
    }

    private static nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var down = wParam == WmKeyDown || wParam == WmSysKeyDown;
            Write(down ? "down" : "up", ("vk", data.VkCode), ("injected", (data.Flags & LlkhfInjected) != 0));
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static void Write(string kind, params (string Key, object Value)[] fields)
    {
        var line = new Dictionary<string, object> { ["ts"] = DateTime.UtcNow.ToString("O"), ["event"] = kind };
        foreach (var (key, value) in fields)
        {
            line[key] = value;
        }

        _log?.WriteLine(JsonSerializer.Serialize(line));
    }
}
