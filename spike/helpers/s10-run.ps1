# S10 unattended run. For each logger mode, restart win\home-logger.ps1, then for each target: bring it to the
# foreground (Alt unlock), beep, wait for one Home press, record the foreground +1 s, screenshot, pause.
# The person only presses Home after each beep and touches nothing else. At the end the chat window comes back.
param([string]$Targets, [string]$Modes = 'plain,swallow-plain,swallow-alt,swallow-attach', [string]$Out, [string]$ChatPid)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\win\_common.ps1"
Add-MsType 'MsWin' 'WinTools.cs'
Add-Type -Namespace S10 -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
'@
$spike = Split-Path $PSScriptRoot -Parent
function Note([string]$m) { $l = '{0} {1}' -f [DateTime]::UtcNow.ToString('HH:mm:ss.fff'), $m; Add-Content -LiteralPath $Out -Value $l -Encoding UTF8; Write-Host $l }
function Fg { $h = [MsWin]::GetForegroundWindow(); 'pid={0} "{1}"' -f [MsWin]::ProcessIdOf($h), [MsWin]::Title($h) }
function Front([IntPtr]$h) { if ([S10.Native]::IsIconic($h)) { [void][MsWin]::ShowWindow($h, 9) }; [S10.Native]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [S10.Native]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero); [MsWin]::SetForegroundWindow($h) }
function TargetWindow([string]$t) {
    if ($t -eq 'desktop') { (New-Object -ComObject Shell.Application).MinimizeAll(); Start-Sleep -Milliseconds 800; return [S10.Native]::FindWindow('Progman', [NullString]::Value) }
    $p = Get-Process -Name $t -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and ($t -ne 'msedge' -or $_.MainWindowTitle -match 'Netflix') } | Select-Object -First 1
    if (-not $p) { return [IntPtr]::Zero }
    $p.MainWindowHandle
}
Set-Content -LiteralPath $Out -Value '' -Encoding UTF8
Note "start; tryby=$Modes; cele=$Targets"
foreach ($mode in $Modes.Split(',')) {
    $log = Join-Path $spike "logs\S10r-$mode.log"
    Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
    $largs = @('-ExecutionPolicy', 'Bypass', '-File', (Join-Path $spike 'win\home-logger.ps1'), '-DurationSec', '600', '-LogFile', $log)
    if ($mode -like 'swallow-*') { $largs += @('-Swallow', '-Foreground', $mode.Substring(8)) }
    $logger = Start-Process powershell.exe -ArgumentList $largs -PassThru -WindowStyle Minimized
    while (-not (Select-String -LiteralPath $log -Pattern 'config:' -Quiet -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 300 }
    Note "== tryb $mode (logger pid=$($logger.Id))"
    foreach ($t in $Targets.Split(',')) {
        $h = TargetWindow $t
        if ($h -eq [IntPtr]::Zero) { Note "  ${t}: brak okna, pomijam"; continue }
        $ok = Front $h; Start-Sleep -Milliseconds 700
        $n = (Select-String -LiteralPath $log -Pattern 'HOME \+300ms').Count
        Note "  ${t}: SetForegroundWindow=$ok, przed Home fg=$(Fg)"
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $spike 'win\screenshot.ps1') -Name "S10r-$mode-$t-before" | Out-Null
        [Console]::Beep(880, 250)
        $deadline = (Get-Date).AddSeconds(45)
        while ((Select-String -LiteralPath $log -Pattern 'HOME \+300ms').Count -le $n -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
        if ((Get-Date) -ge $deadline) { Note "  ${t}: brak Home w 45 s"; continue }
        Start-Sleep -Milliseconds 1000
        Note "  ${t}: po Home (+1 s) fg=$(Fg)"
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $spike 'win\screenshot.ps1') -Name "S10r-$mode-$t-after" | Out-Null
        Start-Sleep -Seconds 3
    }
    New-Item -ItemType File -Force -Path (Join-Path $spike 'logs\home-logger.stop') | Out-Null
    Start-Sleep -Seconds 2
    Remove-Item -LiteralPath (Join-Path $spike 'logs\home-logger.stop') -ErrorAction SilentlyContinue
}
[Console]::Beep(660, 150); [Console]::Beep(660, 150); [Console]::Beep(990, 400)
if ($ChatPid) { $c = Get-Process -Id $ChatPid -ErrorAction SilentlyContinue; if ($c) { Front $c.MainWindowHandle | Out-Null } }
Note 'KONIEC'
