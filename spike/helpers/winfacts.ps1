#Requires -PSEdition Desktop
<# Window facts for 6.1: style bits, rect vs monitor, iconic state; min/restore via ShowWindow (never SW_HIDE).
   winfacts.ps1 -ProcessId N [-Action facts|min|restore|front] #>
param([Parameter(Mandatory = $true)][int]$ProcessId, [ValidateSet('facts', 'min', 'restore', 'front', 'top', 'untop')][string]$Action = 'facts')
Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class W61 {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct MI { public int cb; public RECT mon, work; public uint flags; }
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc c, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder t, int m);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder t, int m);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
  [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MI mi);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  public static void Top(IntPtr h, bool on) { SetWindowPos(h, on ? (IntPtr)(-1) : (IntPtr)(-2), 0, 0, 0, 0, 0x13); }
  public static void Front(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); SetForegroundWindow(h); }
  public static List<IntPtr> Top(uint pid) {
    List<IntPtr> f = new List<IntPtr>();
    EnumWindows(delegate (IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p); StringBuilder c = new StringBuilder(64); GetClassName(h, c, 64);
      if (p == pid && c.ToString() == "Chrome_WidgetWin_1") f.Add(h); return true; }, IntPtr.Zero);
    return f;
  }
  public static string Facts(IntPtr h) {
    StringBuilder t = new StringBuilder(256); GetWindowText(h, t, 256);
    RECT r; GetWindowRect(h, out r); MI mi = new MI(); mi.cb = Marshal.SizeOf(typeof(MI)); GetMonitorInfo(MonitorFromWindow(h, 2), ref mi);
    int s = GetWindowLong(h, -16), e = GetWindowLong(h, -20);
    return string.Format("hwnd=0x{0:X} title=\"{1}\" visible={2} iconic={3} zoomed={4} fg={5} caption={6} thickframe={7} sysmenu={8} topmost={9} rect=({10},{11},{12},{13}) monitor=({14},{15},{16},{17}) coversMonitor={18}",
      h.ToInt64(), t, IsWindowVisible(h), IsIconic(h), IsZoomed(h), GetForegroundWindow() == h, (s & 0xC00000) == 0xC00000, (s & 0x40000) != 0, (s & 0x80000) != 0, (e & 8) != 0,
      r.L, r.T, r.R, r.B, mi.mon.L, mi.mon.T, mi.mon.R, mi.mon.B, r.L <= mi.mon.L && r.T <= mi.mon.T && r.R >= mi.mon.R && r.B >= mi.mon.B);
  }
}
'@
$all = [W61]::Top([uint32]$ProcessId)
if (-not $all.Count) { Write-Output "no Chrome_WidgetWin_1 windows for pid $ProcessId"; exit 2 }
foreach ($h in $all) {
  switch ($Action) {
    'min' { [void][W61]::ShowWindow($h, 6); Start-Sleep -Milliseconds 700 }
    'restore' { [void][W61]::ShowWindow($h, 9); Start-Sleep -Milliseconds 700 }
    'top' { [W61]::Top($h, $true); Start-Sleep -Milliseconds 700 }
    'untop' { [W61]::Top($h, $false); Start-Sleep -Milliseconds 300 }
    'front' { [W61]::Front($h); Start-Sleep -Milliseconds 500 }
  }
  Write-Output ("{0} {1}" -f $Action, [W61]::Facts($h))
}
