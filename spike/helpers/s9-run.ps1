# S9 unattended: six 10 s segments; each starts with the action, then one beep. Ends with three tones.
param([string]$Out)
$spike = Split-Path $PSScriptRoot -Parent
$am = Join-Path $spike 'win\audio-mute.ps1'
function Note([string]$m) { $l = '{0} {1}' -f [DateTime]::UtcNow.ToString('HH:mm:ss'), $m; Add-Content -LiteralPath $Out -Value $l -Encoding UTF8 }
function Am([string]$name, [string[]]$a) {
    $log = Join-Path $spike "logs\S9-run-$name.log"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $am @a -LogFile $log | Out-Null
    Get-Content -LiteralPath $log -Encoding UTF8 | Where-Object { $_ -match 'SetMute|changed|roots|session pid=\d+ +(msedge|chrome)\.exe +active' } | ForEach-Object { Note "    $($_.Substring(25))" }
}
function Seg([int]$n, [string]$desc, [scriptblock]$action) {
    Note "odcinek $n`: $desc"; & $action; [Console]::Beep(880, 250); Start-Sleep -Seconds 10
}
Set-Content -LiteralPath $Out -Value '' -Encoding UTF8
Seg 1 'nic (oba grają)' { Am 'list' @('-Action', 'list') }
Seg 2 'mute edge-fresh1 -NoTree' { Am 'edge-notree' @('-Action', 'mute', '-ProfileDir', 'edge-fresh1', '-NoTree') }
Seg 3 'mute edge-fresh1 (drzewo)' { Am 'edge-tree' @('-Action', 'mute', '-ProfileDir', 'edge-fresh1') }
Seg 4 'unmute edge-fresh1' { Am 'edge-unmute' @('-Action', 'unmute', '-ProfileDir', 'edge-fresh1') }
Seg 5 'mute chrome-netflix (drzewo)' { Am 'chrome-tree' @('-Action', 'mute', '-ProfileDir', 'chrome-netflix') }
Seg 6 'unmute chrome-netflix' { Am 'chrome-unmute' @('-Action', 'unmute', '-ProfileDir', 'chrome-netflix') }
Am 'list-end' @('-Action', 'list')
[Console]::Beep(660, 150); [Console]::Beep(660, 150); [Console]::Beep(990, 400)
Note 'KONIEC'
