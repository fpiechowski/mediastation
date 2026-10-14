# S11: Moonlight not minimized but behind the chat window for 15 s (focus loss without minimize).
param([int]$MoonPid, [int]$ChatPid)
. "$PSScriptRoot\..\win\_common.ps1"
Add-MsType 'MsWin' 'WinTools.cs'
Add-Type -Namespace S11 -Name Kb -MemberDefinition '[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);'
function Front($id) { $h = (Get-Process -Id $id).MainWindowHandle; [S11.Kb]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [S11.Kb]::keybd_event(0x12,0,2,[UIntPtr]::Zero); [MsWin]::SetForegroundWindow($h) }
"moonlight front: $(Front $MoonPid)"; Start-Sleep 3
[Console]::Beep(880, 250); "chat front: $(Front $ChatPid) $([DateTime]::UtcNow.ToString('HH:mm:ss'))"
Start-Sleep 15
"moonlight front: $(Front $MoonPid) $([DateTime]::UtcNow.ToString('HH:mm:ss'))"; [Console]::Beep(660,150); [Console]::Beep(660,150); [Console]::Beep(990,400)
