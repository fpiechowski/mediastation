# Polls the current display mode every 100 ms while bringing Moonlight to the front and back to the chat.
param([int]$MoonPid, [int]$ChatPid)
. "$PSScriptRoot\..\win\_common.ps1"
Add-MsType 'MsWin' 'WinTools.cs'
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Dm2 { [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct DEVMODE { [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string n; public short a,b,c,d; public int f; public int x,y,o,fo; public short col,dup,yres,tt,coll; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string form; public short lp; public int bpp,w,h,dfl,freq; }
[DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
public static string Cur() { var d = new DEVMODE(); d.c = (short)Marshal.SizeOf(typeof(DEVMODE)); EnumDisplaySettings(null, -1, ref d); return d.w + "x" + d.h + "@" + d.freq + "Hz"; } }
'@
function Front($id) { $h = (Get-Process -Id $id).MainWindowHandle; [Dm2]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [Dm2]::keybd_event(0x12,0,2,[UIntPtr]::Zero); if ([MsWin]::IsIconic) {}; [void][MsWin]::ShowWindow($h, 9); [MsWin]::SetForegroundWindow($h) }
$last = ''; $t0 = Get-Date
function Poll([double]$sec, [string]$phase) { $end = (Get-Date).AddSeconds($sec); while ((Get-Date) -lt $end) { $m = [Dm2]::Cur(); if ($m -ne $script:last) { '{0,6:N1}s {1,-10} {2}' -f ((Get-Date) - $t0).TotalSeconds, $phase, $m; $script:last = $m }; Start-Sleep -Milliseconds 100 } }
Poll 1 'start'
"front Moonlight: $(Front $MoonPid)"; Poll 5 'moonlight'
"front chat: $(Front $ChatPid)"; Poll 5 'chat'
