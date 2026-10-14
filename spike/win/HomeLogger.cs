// S10: logs everything the G20S Pro remote sends, from every source at once and unfiltered (WH_KEYBOARD_LL,
// Raw Input keyboard/consumer/system with RIDEV_INPUTSINK, WM_APPCOMMAND, shell-hook HSHELL_APPCOMMAND), each
// with the foreground process, and on Home tries to bring this window to the foreground.
// C# 5 only: compiled by Add-Type in Windows PowerShell 5.1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public class MsHomeLogger : Form
{
    const int WH_KEYBOARD_LL = 13, WM_INPUT = 0x00FF, WM_APPCOMMAND = 0x0319, HSHELL_APPCOMMAND = 12;
    const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007, RIDI_DEVICEINFO = 0x2000000B, RIDEV_INPUTSINK = 0x00000100;
    const uint RIM_TYPEKEYBOARD = 1, RIM_TYPEHID = 2;
    const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE { public ushort UsagePage; public ushort Usage; public uint Flags; public IntPtr Target; }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER { public uint Type; public uint Size; public IntPtr Device; public IntPtr WParam; }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWKEYBOARD { public ushort MakeCode; public ushort Flags; public ushort Reserved; public ushort VKey; public uint Message; public uint Extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint VkCode; public uint ScanCode; public uint Flags; public uint Time; public IntPtr Extra; }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern bool RegisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    readonly int homeVk, homeAppCommand, homeHidUsage;
    readonly bool swallow;
    readonly string foregroundMethod;
    readonly StreamWriter log;
    readonly TextBox box;
    readonly Dictionary<IntPtr, string> devices = new Dictionary<IntPtr, string>();
    readonly Dictionary<uint, string> processNames = new Dictionary<uint, string>();
    HookProc hookProc; // kept in a field so the GC does not collect the delegate while the hook is active
    IntPtr hook;
    uint shellMessage;
    DateTime lastHome = DateTime.MinValue;
    readonly DateTime deadline;
    readonly string stopFile;

    public MsHomeLogger(string logPath, int homeVk, int homeAppCommand, int homeHidUsage, bool swallow, string foregroundMethod,
        int durationSec, string stopFile)
    {
        deadline = durationSec > 0 ? DateTime.Now.AddSeconds(durationSec) : DateTime.MaxValue;
        this.stopFile = stopFile;
        this.homeVk = homeVk;
        homeVkStatic = homeVk;
        this.homeAppCommand = homeAppCommand;
        this.homeHidUsage = homeHidUsage;
        this.swallow = swallow;
        this.foregroundMethod = foregroundMethod;
        log = new StreamWriter(logPath, true, new UTF8Encoding(false));
        log.AutoFlush = true;

        Text = "MediaStation S10 - Home logger";
        Width = 1200;
        Height = 650;
        box = new TextBox();
        box.Multiline = true;
        box.ReadOnly = true;
        box.WordWrap = false;
        box.ScrollBars = ScrollBars.Both;
        box.Dock = DockStyle.Fill;
        box.Font = new Font("Consolas", 10);
        Controls.Add(box);
        Log("log: " + logPath);

        // -DurationSec / stop file, so the logger can run unattended in the background.
        Timer stopCheck = new Timer();
        stopCheck.Interval = 500;
        stopCheck.Tick += delegate
        {
            if (DateTime.Now < deadline && !File.Exists(this.stopFile)) return;
            stopCheck.Stop();
            Log(DateTime.Now >= deadline ? "stop: duration elapsed" : "stop: stop file " + this.stopFile);
            Close();
        };
        stopCheck.Start();
    }

    RAWINPUTDEVICE RawDevice(ushort usagePage, ushort usage)
    {
        RAWINPUTDEVICE d = new RAWINPUTDEVICE();
        d.UsagePage = usagePage;
        d.Usage = usage;
        d.Flags = RIDEV_INPUTSINK; // receive input also when this window is in the background
        d.Target = Handle;
        return d;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RAWINPUTDEVICE[] raw = { RawDevice(0x01, 0x06), RawDevice(0x0C, 0x01), RawDevice(0x01, 0x80) }; // keyboard, consumer control, system control
        bool rawOk = RegisterRawInputDevices(raw, (uint)raw.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        Log("RegisterRawInputDevices(keyboard, consumer, system)=" + rawOk + (rawOk ? "" : " err=" + Marshal.GetLastWin32Error()));

        shellMessage = RegisterWindowMessage("SHELLHOOK");
        Log("RegisterShellHookWindow=" + RegisterShellHookWindow(Handle));

        hookProc = OnKeyboardHook;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle("user32.dll"), 0);
        Log("SetWindowsHookEx(WH_KEYBOARD_LL)=" + (hook != IntPtr.Zero) + (hook != IntPtr.Zero ? "" : " err=" + Marshal.GetLastWin32Error()));
        Log(string.Format("config: homeVk=0x{0:X2} homeAppCommand={1} homeHidUsage=0x{2:X} swallow={3} foreground={4}",
            homeVk, homeAppCommand, homeHidUsage, swallow, foregroundMethod));
        Log("Naciśnij Home (i inne przyciski pilota) przy fokusie w różnych aplikacjach.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        Log("closed");
        log.Dispose();
        base.OnFormClosed(e);
    }

    IntPtr OnKeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            int message = wParam.ToInt32();
            bool down = message == 0x0100 || message == 0x0104; // WM_KEYDOWN, WM_SYSKEYDOWN
            bool isHome = k.VkCode == homeVk;
            if (RemoteLikeVk((int)k.VkCode)) Log(string.Format("LL      {0,-4} vk=0x{1:X2} scan=0x{2:X2} flags=0x{3:X2} extra=0x{4:X} fg={5}{6}",
                down ? "down" : "up", k.VkCode, k.ScanCode, k.Flags, k.Extra.ToInt64(), Describe(GetForegroundWindow()),
                isHome ? "  <== HOME" : ""));
            if (isHome)
            {
                // Never do slow work inside a low-level hook: defer the foreground attempt.
                if (down) BeginInvoke(new Action<string>(OnHome), "ll-hook");
                if (swallow) return (IntPtr)1;
            }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT) OnRawInput(m.LParam);
        else if (m.Msg == WM_APPCOMMAND) OnAppCommand("WM_APPCOMMAND", m.LParam);
        else if (shellMessage != 0 && m.Msg == (int)shellMessage && m.WParam.ToInt64() == HSHELL_APPCOMMAND) OnAppCommand("HSHELL_APPCOMMAND", m.LParam);
        base.WndProc(ref m);
    }

    void OnAppCommand(string source, IntPtr lParam)
    {
        long high = (lParam.ToInt64() >> 16) & 0xFFFF;
        int command = (int)(high & 0x0FFF); // GET_APPCOMMAND_LPARAM
        int device = (int)(high & 0xF000);  // FAPPCOMMAND_KEY=0x0000, MOUSE=0x8000, OEM=0x1000
        bool isHome = command == homeAppCommand;
        Log(string.Format("{0} cmd={1} device=0x{2:X4} fg={3}{4}", source, command, device, Describe(GetForegroundWindow()), isHome ? "  <== HOME" : ""));
        if (isHome) OnHome(source);
    }

    void OnRawInput(IntPtr raw)
    {
        uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
        uint size = 0;
        GetRawInputData(raw, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(raw, RID_INPUT, buffer, ref size, headerSize) != size) return;
            RAWINPUTHEADER header = (RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(RAWINPUTHEADER));
            IntPtr body = IntPtr.Add(buffer, (int)headerSize);
            string device = DeviceTag(header.Device);
            if (header.Type == RIM_TYPEKEYBOARD)
            {
                RAWKEYBOARD k = (RAWKEYBOARD)Marshal.PtrToStructure(body, typeof(RAWKEYBOARD));
                bool down = (k.Flags & 1) == 0; // RI_KEY_BREAK
                bool isHome = k.VKey == homeVk;
                if (RemoteLikeVk(k.VKey)) Log(string.Format("RAW-KBD {0,-4} vk=0x{1:X2} make=0x{2:X2} flags=0x{3:X} msg=0x{4:X} {5} fg={6}{7}",
                    down ? "down" : "up", k.VKey, k.MakeCode, k.Flags, k.Message, device, Describe(GetForegroundWindow()),
                    isHome ? "  <== HOME" : ""));
                if (isHome && down) OnHome("raw-keyboard");
            }
            else if (header.Type == RIM_TYPEHID)
            {
                int reportSize = Marshal.ReadInt32(body);
                int count = Marshal.ReadInt32(body, 4);
                byte[] data = new byte[reportSize * count];
                Marshal.Copy(IntPtr.Add(body, 8), data, 0, data.Length);
                // Consumer reports usually are [reportId, usageLo, usageHi]; also try without a report id.
                bool isHome = (data.Length >= 3 && (data[1] | (data[2] << 8)) == homeHidUsage)
                              || (data.Length >= 2 && (data[0] | (data[1] << 8)) == homeHidUsage);
                Log(string.Format("RAW-HID data={0} {1} fg={2}{3}", BitConverter.ToString(data), device, Describe(GetForegroundWindow()),
                    isHome ? "  <== HOME" : ""));
                if (isHome) OnHome("raw-hid");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // Privacy (UM760): the hooks see every keyboard, including the person typing elsewhere. Only keys a remote can
    // send are logged; letters, digits and space never reach the log file.
    static bool RemoteLikeVk(int vk)
    {
        return vk == 0x08 || vk == 0x0D || vk == 0x1B || (vk >= 0x21 && vk <= 0x28) || vk == 0x5D
            || (vk >= 0xA6 && vk <= 0xB7) || vk == homeVkStatic;
    }

    static int homeVkStatic = 0xAC;

    string DeviceTag(IntPtr device)
    {
        if (device == IntPtr.Zero) return "dev=(injected)";
        string tag;
        if (devices.TryGetValue(device, out tag)) return tag;
        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        IntPtr buffer = Marshal.AllocHGlobal((int)(size + 1) * 2);
        string name;
        try
        {
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buffer, ref size);
            name = Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        tag = "dev#" + (devices.Count + 1);
        devices[device] = tag;
        Log("new raw input device " + tag + " = " + name + " | " + DeviceInfo(device));
        return tag;
    }

    // VID/PID and the top-level usage page/usage: tells the 2.4G dongle apart from other keyboards.
    static string DeviceInfo(IntPtr device)
    {
        const int infoSize = 32; // sizeof(RID_DEVICE_INFO)
        uint size = infoSize;
        IntPtr buffer = Marshal.AllocHGlobal(infoSize);
        try
        {
            Marshal.WriteInt32(buffer, 0, infoSize);
            if (GetRawInputDeviceInfo(device, RIDI_DEVICEINFO, buffer, ref size) == unchecked((uint)-1)) return "info=?";
            int type = Marshal.ReadInt32(buffer, 4);
            if (type == RIM_TYPEHID)
                return string.Format("hid vid=0x{0:X4} pid=0x{1:X4} ver=0x{2:X} usagePage=0x{3:X2} usage=0x{4:X2}",
                    Marshal.ReadInt32(buffer, 8), Marshal.ReadInt32(buffer, 12), Marshal.ReadInt32(buffer, 16),
                    (ushort)Marshal.ReadInt16(buffer, 20), (ushort)Marshal.ReadInt16(buffer, 22));
            if (type == RIM_TYPEKEYBOARD)
                return string.Format("keyboard type={0} subtype={1} keys={2}",
                    Marshal.ReadInt32(buffer, 8), Marshal.ReadInt32(buffer, 12), Marshal.ReadInt32(buffer, 28));
            return "type=" + type;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    void OnHome(string source)
    {
        if ((DateTime.Now - lastHome).TotalMilliseconds < 500)
        {
            Log("HOME via " + source + " (duplikat tego samego naciśnięcia)");
            return;
        }
        lastHome = DateTime.Now;
        IntPtr before = GetForegroundWindow();
        if (IsIconic(Handle)) ShowWindow(Handle, SW_RESTORE);

        bool ok;
        if (foregroundMethod == "attach")
        {
            uint pid;
            uint foregroundThread = GetWindowThreadProcessId(before, out pid);
            uint me = GetCurrentThreadId();
            bool attached = foregroundThread != me && AttachThreadInput(me, foregroundThread, true);
            ok = SetForegroundWindow(Handle);
            BringWindowToTop(Handle);
            if (attached) AttachThreadInput(me, foregroundThread, false);
        }
        else if (foregroundMethod == "alt")
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero); // synthetic Alt press unlocks SetForegroundWindow
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            ok = SetForegroundWindow(Handle);
        }
        else
        {
            ok = SetForegroundWindow(Handle);
        }
        Log(string.Format("HOME via {0}: SetForegroundWindow[{1}]={2} fg-before={3}", source, foregroundMethod, ok, Describe(before)));

        Timer check = new Timer();
        check.Interval = 300;
        check.Tick += delegate
        {
            check.Stop();
            check.Dispose();
            IntPtr now = GetForegroundWindow();
            Log(string.Format("HOME +300ms: MediaStation-on-top={0} fg={1}", now == Handle, Describe(now)));
        };
        check.Start();
    }

    string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(none)";
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        string name;
        if (!processNames.TryGetValue(pid, out name))
        {
            try { name = Process.GetProcessById((int)pid).ProcessName; }
            catch (Exception) { name = "?"; }
            processNames[pid] = name;
        }
        StringBuilder title = new StringBuilder(64);
        GetWindowText(hwnd, title, title.Capacity);
        return name + ":" + pid + " \"" + title + "\"";
    }

    void Log(string line)
    {
        string text = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + " " + line;
        log.WriteLine(text);
        if (box.TextLength > 200000) box.Clear();
        box.AppendText(text + Environment.NewLine);
    }
}
