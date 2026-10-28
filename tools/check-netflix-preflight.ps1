<#
.SYNOPSIS
    Acceptance gate for the Netflix audio scenarios (S15-S27).

.DESCRIPTION
    The check succeeds only when the Edge process uses profiles\netflix, its attached
    window is visible, the run log contains appWindowAttached for netflix, the window
    title or launch URL identifies netflix.com, and the operator confirms audible playback.
    MEDIASTATION_HARNESS_URL and other non-Netflix pages never satisfy the gate.

    Use -SelfTest for the regression matrix without touching the desktop.
#>
param(
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [string]$ProfileDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\profiles\netflix'),
    [string]$LogPath = '',
    [switch]$PlaybackConfirmed,
    [switch]$NonInteractive,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

if (-not ('NetflixPreflight.Native' -as [type])) {
    Add-Type -Namespace NetflixPreflight -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
'@
}

function Read-JsonLines {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $items = @()
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $items += ($line | ConvertFrom-Json) } catch { }
    }
    return $items
}

function Normalize-PathText {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    try { return [IO.Path]::GetFullPath($Value).TrimEnd('\').ToLowerInvariant() }
    catch { return '' }
}

function Test-NetflixUrl {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    $uri = [Uri]$null
    if (-not [Uri]::TryCreate($Value.Trim(), [UriKind]::Absolute, [ref]$uri)) { return $false }
    return $uri.Host -ieq 'netflix.com' -or $uri.Host.EndsWith('.netflix.com', [StringComparison]::OrdinalIgnoreCase)
}

function Test-NetFlixPreflightState {
    param(
        [object[]]$Entries,
        [object[]]$Processes,
        [string]$ExpectedProfile,
        [bool]$Playback
    )
    $reasons = [Collections.Generic.List[string]]::new()
    $profile = Normalize-PathText $ExpectedProfile
    $matching = @($Processes | Where-Object {
        (Normalize-PathText $_.ProfileDir) -eq $profile -and $_.Name -ieq 'msedge'
    })
    if ($matching.Count -eq 0) { [void]$reasons.Add("brak procesu Edge z profilem $ExpectedProfile") }

    $attach = @($Entries | Where-Object {
        $_.msg -eq 'appWindowAttached' -and $_.data.appId -eq 'netflix'
    } | Select-Object -Last 1)
    if ($attach.Count -eq 0) {
        [void]$reasons.Add('brak appWindowAttached dla netflix')
    }

    $root = $matching | Where-Object { [int64]$_.Id -eq [int64]$attach[0].data.rootPid } | Select-Object -First 1
    if ($null -eq $root) { [void]$reasons.Add('appWindowAttached rootPid nie należy do profilu netflix') }
    $window = $null
    if ($attach.Count -gt 0) {
        $window = $matching | Where-Object {
            $_.Hwnd -eq [int64]$attach[0].data.hwnd
        } | Select-Object -First 1
        if ($null -eq $window) { [void]$reasons.Add('attached window is not owned by the netflix profile') }
        elseif (-not $window.Visible) { [void]$reasons.Add('okno Edge jest niewidoczne') }
    }

    $urlEvidence = @($Entries | Where-Object {
        $_.msg -in @('appLaunch','appLaunchStage') -and $_.data.appId -eq 'netflix' -and
        (Test-NetflixUrl ([string]$_.data.url)) -or (([string]$_.data.commandLine) -match '(?i)(^|[\s/])netflix\.com([/\s]|$)')
    })
    $titleEvidence = @($window | Where-Object { ([string]$_.Title) -match '(?i)(netflix(?:\.com)?)' })
    $urlFallback = ($titleEvidence.Count -eq 0 -and $window -and [string]::IsNullOrWhiteSpace([string]$window.Title) -and $urlEvidence.Count -gt 0)
    if ($titleEvidence.Count -eq 0 -and -not $urlFallback) {
        [void]$reasons.Add('brak adresu lub tytulu wskazujacego netflix.com')
    }
    if (-not $Playback) { [void]$reasons.Add('operator did not confirm visible and audible playback') }

    [pscustomobject]@{
        ready = ($reasons.Count -eq 0)
        reasons = @($reasons)
        profile = $ExpectedProfile
        appWindowAttached = ($attach.Count -gt 0)
        playbackConfirmed = $Playback
    }
}

function Get-LatestLogPath {
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) { return $LogPath }
    return (Get-ChildItem -LiteralPath $LogsDir -Filter 'mediastation-*.jsonl' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime | Select-Object -Last 1).FullName
}

function Get-EdgeProfileProcesses {
    $rows = @()
    try { $cim = @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'") } catch { return @() }
    foreach ($p in $cim) {
        if ([string]::IsNullOrWhiteSpace($p.CommandLine)) { continue }
        $match = [regex]::Match($p.CommandLine, '(?i)--user-data-dir=(?:"([^"]+)"|([^\s]+))')
        if (-not $match.Success) { continue }
        $profile = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
        $proc = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
        $hwnd = if ($null -eq $proc) { 0 } else { $proc.MainWindowHandle.ToInt64() }
        [bool]$visible = $false
        if ($hwnd -ne 0) { $visible = [NetflixPreflight.Native]::IsWindowVisible([IntPtr]$hwnd) }
        $rows += [pscustomobject]@{ Name = 'msedge'; Id = [int]$p.ProcessId; ProfileDir = $profile; Hwnd = $hwnd; Visible = $visible; Title = if ($null -eq $proc) { '' } else { $proc.MainWindowTitle } }
    }
    return $rows
}

function Invoke-Preflight {
    $path = Get-LatestLogPath
    $entries = if ([string]::IsNullOrWhiteSpace($path)) { @() } else { @(Read-JsonLines $path) }
    $processes = @(Get-EdgeProfileProcesses)
    $confirmed = [bool]$PlaybackConfirmed
    if (-not $confirmed -and -not $NonInteractive) {
        $answer = Read-Host 'Is netflix.com signed in, visible, and playing audible media? (y/n)'
        $confirmed = $answer -match '^(?i:y|yes|t|tak)$'
    }
    $requiredProfile = Normalize-PathText (Join-Path $env:LOCALAPPDATA 'MediaStation\profiles\netflix')
    $result = Test-NetFlixPreflightState -Entries $entries -Processes $processes -ExpectedProfile $requiredProfile -Playback $confirmed
    if ((Normalize-PathText $ProfileDir) -ne $requiredProfile) {
        $result.reasons = @($result.reasons) + 'ProfileDir musi wskazywac profiles\\netflix'
        $result.ready = $false
    }
    $result | Add-Member -NotePropertyName computer -NotePropertyValue $env:COMPUTERNAME
    $result | ConvertTo-Json -Depth 5
    if ($result.ready) { exit 0 }
    exit 1
}

if ($SelfTest) {
    $okEntries = @(
        [pscustomobject]@{ msg = 'appWindowAttached'; data = [pscustomobject]@{ appId='netflix'; hwnd=100; rootPid=7 } },
        [pscustomobject]@{ msg = 'appLaunch'; data = [pscustomobject]@{ appId='netflix'; url='https://www.netflix.com/watch/123' } },
        [pscustomobject]@{ msg = 'audioRuleApplied'; data = [pscustomobject]@{ muted=$false; sessions=0 } }
    )
    $okProc = @([pscustomobject]@{ Name='msedge'; Id=7; ProfileDir='C:\profiles\netflix'; Hwnd=100; Visible=$true; Title='netflix.com - Edge' })
    $good = Test-NetFlixPreflightState -Entries $okEntries -Processes $okProc -ExpectedProfile 'C:\profiles\netflix' -Playback $true
    if (-not $good.ready) { throw "SelfTest valid state failed: $($good.reasons -join '; ')" }
    $bad = Test-NetFlixPreflightState -Entries @($okEntries[2]) -Processes @() -ExpectedProfile 'C:\profiles\netflix' -Playback $false
    if ($bad.ready -or $bad.reasons.Count -lt 2) { throw 'SelfTest invalid state was accepted.' }
    $wrongTitle = @([pscustomobject]@{ Name='msedge'; Id=7; ProfileDir='C:\profiles\netflix'; Hwnd=100; Visible=$true; Title='example.com - Edge' },
        [pscustomobject]@{ Name='msedge'; Id=8; ProfileDir='C:\profiles\netflix'; Hwnd=200; Visible=$true; Title='netflix.com - Edge' })
    $wrong = Test-NetFlixPreflightState -Entries $okEntries -Processes $wrongTitle -ExpectedProfile 'C:\profiles\netflix' -Playback $true
    if ($wrong.ready) { throw 'SelfTest accepted Netflix evidence from a different window.' }
    Write-Host 'PASS: Netflix preflight self-test distinguishes ready and blocked states.' -ForegroundColor Green
    exit 0
}

Invoke-Preflight
