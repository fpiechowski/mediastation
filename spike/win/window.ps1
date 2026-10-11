#Requires -PSEdition Desktop
<#
Show/hide/minimize the top-level windows of a process (S8 "hide" variant, S11 helper).

  window.ps1 -Action list    -ProcessName msedge
  window.ps1 -Action hide    -ProcessId 1234       # SW_HIDE; HWNDs saved to logs/hidden-windows.txt
  window.ps1 -Action show                          # SW_SHOW for HWNDs from the last hide
  window.ps1 -Action min|restore|front -Hwnd 0x1A2B3C
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('list', 'hide', 'show', 'min', 'restore', 'front')][string]$Action,
    [string]$ProcessName,
    [int]$ProcessId,
    [Int64]$Hwnd
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"
Add-MsType 'MsWin' 'WinTools.cs'
New-MsLog "window-$Action" | Out-Null
$stateFile = Join-Path $MsLogs 'hidden-windows.txt'

if ($Hwnd) {
    $targets = @([IntPtr]$Hwnd)
} elseif ($Action -eq 'show' -and -not $ProcessName -and -not $ProcessId) {
    if (-not (Test-Path $stateFile)) { throw "Brak $stateFile; podaj -Hwnd albo proces" }
    $targets = Get-Content $stateFile | ForEach-Object { [IntPtr][Int64]$_ }
} else {
    $pids = if ($ProcessId) { @($ProcessId) } elseif ($ProcessName) { @(Get-Process -Name $ProcessName | ForEach-Object { $_.Id }) } else { throw 'Podaj -ProcessName, -ProcessId albo -Hwnd' }
    $includeHidden = $Action -in 'list', 'show'
    $targets = @(foreach ($p in $pids) { [MsWin]::TopWindows([uint32]$p, $includeHidden) })
}
if (-not $targets.Count) { throw 'Nie znaleziono okien' }

$showCmd = @{ hide = 0; show = 5; min = 6; restore = 9 }
foreach ($h in $targets) {
    $desc = 'hwnd=0x{0:X} pid={1} visible={2} "{3}"' -f $h.ToInt64(), [MsWin]::ProcessIdOf($h), [MsWin]::IsWindowVisible($h), [MsWin]::Title($h)
    switch ($Action) {
        'list' { Write-MsLog $desc }
        'front' { Write-MsLog ('SetForegroundWindow={0} {1}' -f [MsWin]::SetForegroundWindow($h), $desc) }
        default { [void][MsWin]::ShowWindow($h, $showCmd[$Action]); Write-MsLog "$Action -> $desc" }
    }
}
if ($Action -eq 'hide') { $targets | ForEach-Object { $_.ToInt64() } | Set-Content $stateFile }
