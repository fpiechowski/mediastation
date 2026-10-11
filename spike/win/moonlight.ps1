#Requires -PSEdition Desktop
<#
S11: Moonlight from the CLI, hidden or minimized for N minutes, then restored.

  moonlight.ps1 -Action list      -HostName gaming-pc
  moonlight.ps1 -Action start     -HostName gaming-pc -App "Desktop" [-MuteOnFocusLoss] [-BackgroundGamepad] [-QuitAfter]
  moonlight.ps1 -Action hide-test -Mode min  -Minutes 5      # SW_MINIMIZE, then SW_RESTORE
  moonlight.ps1 -Action hide-test -Mode hide -Minutes 5      # SW_HIDE, then SW_SHOW
  moonlight.ps1 -Action quit      -HostName gaming-pc

hide-test runs unattended: it ends early when the stop file appears (default logs\moonlight.stop) and asks
nothing; after the restore it logs "SPRAWDŹ" so the person checks picture, sound and input.
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('start', 'hide-test', 'list', 'quit')][string]$Action,
    [string]$HostName,
    [string]$App = 'Desktop',
    [switch]$MuteOnFocusLoss,
    [switch]$BackgroundGamepad,
    [switch]$QuitAfter,
    [string]$ExtraArgs = '',
    [ValidateSet('min', 'hide')][string]$Mode = 'min',
    [double]$Minutes = 5,
    [string]$Exe = "$env:ProgramFiles\Moonlight Game Streaming\Moonlight.exe",
    [string]$StopFile,
    [string]$LogFile
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"
Add-MsType 'MsWin' 'WinTools.cs'
Start-MsRun -Label "S11-moonlight-$Action" -LogFile $LogFile -StopFile $StopFile -DurationSec 0 -Script 'moonlight'

function Get-MoonlightWindow {
    $proc = Get-Process -Name Moonlight -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) { return $null }
    $hwnd = [MsWin]::TopWindows([uint32]$proc.Id, $false) | Select-Object -First 1
    [pscustomobject]@{ Proc = $proc; Hwnd = $hwnd }
}

function Invoke-MoonlightCli([string[]]$CliArgs) {
    if (-not (Test-Path $Exe)) { throw "Nie znaleziono $Exe; podaj -Exe" }
    $p = Start-Process -FilePath $Exe -ArgumentList $CliArgs -PassThru
    Write-MsLog "pid=$($p.Id): `"$Exe`" $($CliArgs -join ' ')"
    $p
}

switch ($Action) {
    'list' {
        if (-not $HostName) { throw 'Podaj -HostName' }
        $p = Invoke-MoonlightCli @('list', $HostName)
        $p.WaitForExit()
        Write-MsLog "list exit=$($p.ExitCode)"
    }
    'quit' {
        if (-not $HostName) { throw 'Podaj -HostName' }
        $p = Invoke-MoonlightCli @('quit', $HostName)
        $p.WaitForExit()
        Write-MsLog "quit exit=$($p.ExitCode)"
    }
    'start' {
        if (-not $HostName) { throw 'Podaj -HostName' }
        $cli = @('stream')
        if ($MuteOnFocusLoss) { $cli += '--mute-on-focus-loss' }
        if ($BackgroundGamepad) { $cli += '--background-gamepad' }
        if ($QuitAfter) { $cli += '--quit-after' }
        $cli += $ExtraArgs.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
        $cli += @($HostName, ('"{0}"' -f $App))
        $p = Invoke-MoonlightCli $cli
        $deadline = (Get-Date).AddSeconds(60)
        do {
            Start-Sleep -Seconds 1
            $m = Get-MoonlightWindow
        } while ((-not $m -or -not $m.Hwnd) -and (Get-Date) -lt $deadline -and -not $p.HasExited)
        if ($m -and $m.Hwnd) { Write-MsLog ('okno hwnd=0x{0:X} "{1}"' -f $m.Hwnd.ToInt64(), [MsWin]::Title($m.Hwnd)) }
        else { Write-MsLog "brak okna Moonlighta po 60 s (exited=$($p.HasExited))" }
    }
    'hide-test' {
        $m = Get-MoonlightWindow
        if (-not $m -or -not $m.Hwnd) { throw 'Moonlight nie działa albo nie ma widocznego okna' }
        $hwnd = $m.Hwnd
        $proc = $m.Proc
        Write-MsLog ('hide-test pid={0} hwnd=0x{1:X} mode={2} minutes={3}' -f $proc.Id, $hwnd.ToInt64(), $Mode, $Minutes)
        [void][MsWin]::ShowWindow($hwnd, $(if ($Mode -eq 'hide') { 0 } else { 6 }))
        Write-MsLog "okno ukryte ($Mode); fg=$([MsWin]::Title([MsWin]::GetForegroundWindow()))"

        $deadline = (Get-Date).AddMinutes($Minutes)
        while ((Get-Date) -lt $deadline -and -not (Test-MsStop)) {
            Start-Sleep -Seconds 5
            $proc.Refresh()
            if ($proc.HasExited) {
                Write-MsLog 'FAIL: Moonlight zakończył się w trakcie ukrycia'
                return
            }
            Write-MsLog ('ukryte, proces żyje, pozostało {0:N0} s' -f ($deadline - (Get-Date)).TotalSeconds)
        }

        [void][MsWin]::ShowWindow($hwnd, $(if ($Mode -eq 'hide') { 5 } else { 9 }))
        Write-MsLog ('przywrócono; SetForegroundWindow={0}; proces żyje={1}' -f [MsWin]::SetForegroundWindow($hwnd), -not $proc.HasExited)
        Write-MsLog 'SPRAWDŹ: czy obraz, dźwięk i sterowanie wróciły? Wynik wpisz do spike-results.md (S11).'
    }
}
