<#
.SYNOPSIS
    Story 6.9: the BrowserHost end-to-end suite. One unattended command runs the five BrowserHost smokes against an
    isolated install copy with a test-only sample driver and writes one report with a verdict per epic requirement.

.DESCRIPTION
    Preflight (no phase runs and the summary is `fail` with the reason when any check fails): the published
    MediaStation.exe and its `page\runtime.js` exist, MediaStation is not running, the sample start URL answers over
    https, port 9237 is free, `apps.json` exists and no orphaned `apps.json.restore-bak` is left, and msedge.exe is
    installed. With -Publish the Release publish is built first.

    The suite never starts the published folder itself. It copies it to
    `%TEMP%\mediastation-browserhost-e2e-<ts>\app` and adds the test-only sample driver
    `tools\samples\page\drivers\browserhost-sample\manifest.json` (a public https start page) to the copy's
    `page\drivers`. The copy is removed in `finally` unless -KeepInstall is given. The suite uses only the throwaway tile
    `browserhost-check` on port 9237 (never `netflix` or `profiles\netflix`) and deletes `profiles\browserhost-check`
    before the first phase and after the last.

    Phases, in this order (-Phases selects a subset): tracer, reconnect, errors, adopt, restart. Each one runs
    `tools\check-browserhost-<phase>.ps1` as a child `powershell -NoProfile -ExecutionPolicy Bypass -File` process with
    `-MediaStation <copy>`, `-Service browserhost-sample` and `-EvidenceDir <report>\<phase>`; tracer and reconnect also
    get `-AppId browserhost-check -CdpPort 9237`, and reconnect gets `-ReconnectTimeoutSeconds` from `settings.json`
    `cdpReconnectTimeout` (default 30). A failing phase does not stop the suite. A phase passes when the smoke exits 0
    and its folder holds `measurements.jsonl`, at least one screenshot and a non-empty log slice.

    After each phase the suite cuts the MediaStation JSON Lines log to the phase's UTC window
    (`<phase>\mediastation.jsonl`) and saves the smoke's console output (`<phase>\output.txt`). The requirement matrix is
    then evaluated by this script from those slices and the phases' `measurements.jsonl`; a passing smoke is not
    enough on its own:
      R-start            tracer: `appLaunch` (src browser, args with `--kiosk` and `--remote-debugging-port=`, without
                         `--remote-allow-origins`), `pageHello {webdriver: false, service: browserhost-sample}`,
                         `browserHostEnabled` present and `devHarnessEnabled` absent;
      R-navigation       tracer: at least 2 `pageHello` and a `reload` measurement with a non-empty `newPageHello`;
      R-reconnect-limit  reconnect: `cdpLost` and `cdpLostHandled {state: failed}`;
      R-reconnect-renew  adopt: `cdpReconnected {trigger: renewSession}`;
      R-version          errors: `browserVersionChanged`;
      R-port-in-use, R-profile-in-use, R-cdp-unavailable
                         errors: a `commandFailed` with errorCode `portInUse`, `profileInUse`, `cdpUnavailable`;
      R-probe-existing   adopt: `probeExisting {outcome: adopted}` and `appAdopted`;
      R-restart          restart: `appStateChanged` failed>stopping (cause RestartApp), later stopping>starting, then
                         starting>foreground;
      R-no-sw-hide       tracer `after-home` iconic and visible; an iconic record in tracer, adopt and restart; every
                         measurement with `iconic` true in every phase also has `visible` true.
    A row whose phase was not selected is `skipped`, which makes the summary `partial`.

    Isolation checks: `apps.json` has the same SHA256 as before, no `apps.json.restore-bak` is left (a leftover is put
    back after it is recorded), the test profile is gone and the install copy is removed (or kept on request).

    Report folder (default `%LOCALAPPDATA%\MediaStation\logs\browserhost-e2e-<ts>`, -Out overrides): `report.jsonl`
    (UTF-8 without BOM; one `phase` record per phase, one `requirement` record per row, one `isolation` record and a
    final `summary` record with the machine, the Edge FileVersion, the install copy, the sample start URL and the
    verdict) and per phase `<phase>\` with the smoke's evidence, `mediastation.jsonl` and `output.txt`. Requirement
    records are written only when preflight passed; a preflight failure gives only the isolation and summary records.
    Exit code 0 only when every phase passes, every row passes and the isolation checks hold.

    -SelfTest scores the recorded fixture (`tools\fixtures\check-browserhost-e2e.fixture.jsonl`, -Fixture overrides)
    without any hardware, with the same matrix functions as the live run: every row must pass, then each mutation of
    the fixture (each row's markers removed in turn, plus a few targeted variants) must fail exactly its own row. It
    also checks the subset rule (`partial`), the verdict rules and the log window cut.

.PARAMETER MediaStation
    The published MediaStation.exe whose folder is copied (default: the Release win-x64 publish folder).

.PARAMETER Publish
    Runs `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained` before the preflight.

.PARAMETER Phases
    The phases to run, as a list or comma-separated (default `tracer,reconnect,errors,adopt,restart`).

.PARAMETER Out
    The report folder (default `%LOCALAPPDATA%\MediaStation\logs\browserhost-e2e-<ts>`).

.PARAMETER KeepInstall
    Keeps the install copy under `%TEMP%` after the run.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1 -Phases tracer,errors

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1 -SelfTest
#>
param(
    [string]$MediaStation = '',
    [switch]$Publish,
    [string[]]$Phases = @('tracer', 'reconnect', 'errors', 'adopt', 'restart'),
    [string]$Out = '',
    [switch]$KeepInstall,
    [switch]$SelfTest,
    [string]$Fixture = ''
)

$ErrorActionPreference = 'Stop'

$AllPhases = @('tracer', 'reconnect', 'errors', 'adopt', 'restart')
$AppId = 'browserhost-check'
$CdpPort = 9237
$PhaseTimeoutMinutes = 15
$repoRoot = Split-Path -Parent $PSScriptRoot
$sampleManifestPath = Join-Path $PSScriptRoot 'samples\page\drivers\browserhost-sample\manifest.json'
$utf8 = New-Object System.Text.UTF8Encoding $false
$logsDir = Join-Path $env:LOCALAPPDATA 'MediaStation\logs'
$configDir = Join-Path $env:LOCALAPPDATA 'MediaStation\config'
$appsPath = Join-Path $configDir 'apps.json'
$appsBackupPath = $appsPath + '.restore-bak'
$profileDir = Join-Path $env:LOCALAPPDATA ('MediaStation\profiles\' + $AppId)
$ts = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe'
}
if ([string]::IsNullOrWhiteSpace($Fixture)) { $Fixture = Join-Path $PSScriptRoot 'fixtures\check-browserhost-e2e.fixture.jsonl' }
if ([string]::IsNullOrWhiteSpace($Out)) { $Out = Join-Path $logsDir ('browserhost-e2e-' + $ts) }

# "tracer,errors" and @('tracer','errors') are both accepted; the run order is always the canonical one.
$requested = @($Phases | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_.Length -gt 0 })
$unknown = @($requested | Where-Object { $AllPhases -notcontains $_ })
if ($unknown.Count -gt 0) { throw "Unknown phase(s): $($unknown -join ', '). Known: $($AllPhases -join ', ')." }
$Selected = @($AllPhases | Where-Object { $requested -contains $_ })
if ($Selected.Count -eq 0) { throw 'No phase selected.' }

if (-not (Test-Path -LiteralPath $sampleManifestPath)) { throw "Sample driver not found: $sampleManifestPath" }
$sample = [System.IO.File]::ReadAllText($sampleManifestPath, $utf8) | ConvertFrom-Json
$Service = [string]$sample.service
$StartUrl = [string]$sample.startUrl
# Guard: the suite never runs on the real Netflix tile, driver or profile.
if ($AppId -eq 'netflix' -or $Service -eq 'netflix' -or (Split-Path -Leaf $profileDir) -ne 'browserhost-check') { throw "Refusing to run with appId '$AppId' / service '$Service'." }

function Note { param([string]$Message) Write-Host ('{0} {1}' -f [DateTimeOffset]::UtcNow.ToString('HH:mm:ss.fff'), $Message) }

function Wait-For {
    param([scriptblock]$Condition, [double]$Seconds)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return [bool](& $Condition)
}

function Write-Jsonl {
    param([string]$Path, [object[]]$Records)
    # UTF-8 without BOM: Out-File/Set-Content in Windows PowerShell write a BOM or ANSI.
    $lines = @($Records | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 8 })
    $text = if ($lines.Count -gt 0) { ($lines -join "`n") + "`n" } else { '' }
    [System.IO.File]::WriteAllText($Path, $text, $utf8)
}

# ---------------------------------------------------------------------------
# Log cut and requirement matrix (the same functions serve the live run and -SelfTest)
# ---------------------------------------------------------------------------

function Get-LineTs {
    param([string]$Line)
    if ($Line -notmatch '"ts":"([^"]+)"') { return $null }
    try {
        return [DateTimeOffset]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal)
    } catch { return $null }
}

# The raw log lines whose `ts` falls inside [From, To].
function Select-LogWindow {
    param([string[]]$Lines, [DateTimeOffset]$From, [DateTimeOffset]$To)
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($line in $Lines) {
        $trimmed = $line.TrimEnd("`r")
        if ($trimmed.Trim().Length -eq 0) { continue }
        $at = Get-LineTs -Line $trimmed
        if ($null -ne $at -and $at -ge $From -and $at -le $To) { $out.Add($trimmed) }
    }
    return , $out.ToArray()
}

# Every line of the log files written since the given time (daily rotation may split a phase over two files).
function Read-LogLines {
    param([DateTimeOffset]$Since)
    $lines = New-Object System.Collections.Generic.List[string]
    $files = @(Get-ChildItem -LiteralPath $logsDir -Filter 'mediastation-*.jsonl' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -ge $Since.UtcDateTime.AddMinutes(-5) } | Sort-Object Name)
    foreach ($file in $files) {
        $stream = [System.IO.File]::Open($file.FullName, 'Open', 'Read', 'ReadWrite')
        try { $text = (New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)).ReadToEnd() } finally { $stream.Dispose() }
        foreach ($line in ($text -split "`n")) { $lines.Add($line) }
    }
    return , $lines.ToArray()
}

function ConvertFrom-JsonLines {
    param([string[]]$Lines)
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($line in $Lines) {
        if ($null -eq $line -or $line.Trim().Length -eq 0) { continue }
        try { $out.Add(($line | ConvertFrom-Json)) } catch { }
    }
    return , $out.ToArray()
}

function Get-PhaseLog { param([hashtable]$Data, [string]$Phase) if ($Data.ContainsKey($Phase)) { return @($Data[$Phase].log) } return @() }
function Get-PhaseMeasurements { param([hashtable]$Data, [string]$Phase) if ($Data.ContainsKey($Phase)) { return @($Data[$Phase].measurements) } return @() }
function Get-Msg { param([object[]]$Log, [string]$Msg) return @($Log | Where-Object { $null -ne $_ -and $_.msg -eq $Msg }) }
function Test-True { param($Value) return ($Value -is [bool]) -and $Value }
function Test-False { param($Value) return ($Value -is [bool]) -and -not $Value }

# One test per row: returns the missing markers (empty = pass).
$Requirements = @(
    [ordered]@{ id = 'R-start'; phases = @('tracer'); test = {
            param([hashtable]$D)
            $log = Get-PhaseLog $D 'tracer'
            $miss = @()
            $launches = @(Get-Msg $log 'appLaunch')
            if ($launches.Count -eq 0) { $miss += 'no appLaunch' }
            foreach ($launch in $launches) {
                $launchArgs = @($launch.data.args)
                if ($launch.src -ne 'browser') { $miss += "appLaunch src '$($launch.src)', expected 'browser'" }
                if ($launchArgs -notcontains '--kiosk') { $miss += 'appLaunch args lack --kiosk' }
                if (@($launchArgs | Where-Object { "$_" -like '--remote-debugging-port=*' }).Count -eq 0) { $miss += 'appLaunch args lack --remote-debugging-port=' }
                if (@($launchArgs | Where-Object { "$_" -like '--remote-allow-origins*' }).Count -gt 0) { $miss += 'appLaunch args contain --remote-allow-origins' }
            }
            $hellos = @(Get-Msg $log 'pageHello')
            if (@($hellos | Where-Object { (Test-False $_.data.webdriver) -and $_.data.service -ceq $Service }).Count -eq 0) {
                $seen = @($hellos | ForEach-Object { 'service=' + $_.data.service + ' webdriver=' + $_.data.webdriver }) -join '; '
                $miss += "no pageHello {webdriver: false, service: $Service} (seen: $(if ($seen) { $seen } else { 'none' }))"
            }
            if (@(Get-Msg $log 'browserHostEnabled').Count -eq 0) { $miss += 'no browserHostEnabled' }
            if (@(Get-Msg $log 'devHarnessEnabled').Count -ne 0) { $miss += 'devHarnessEnabled present' }
            return $miss
        }
    },
    [ordered]@{ id = 'R-navigation'; phases = @('tracer'); test = {
            param([hashtable]$D)
            $miss = @()
            $hellos = @(Get-Msg (Get-PhaseLog $D 'tracer') 'pageHello').Count
            if ($hellos -lt 2) { $miss += "pageHello x$hellos, expected at least 2" }
            $reload = @(Get-PhaseMeasurements $D 'tracer' | Where-Object { $_.stage -eq 'reload' -and @($_.newPageHello | Where-Object { $null -ne $_ }).Count -gt 0 })
            if ($reload.Count -eq 0) { $miss += 'no reload measurement with a non-empty newPageHello' }
            return $miss
        }
    },
    [ordered]@{ id = 'R-reconnect-limit'; phases = @('reconnect'); test = {
            param([hashtable]$D)
            $log = Get-PhaseLog $D 'reconnect'
            $miss = @()
            if (@(Get-Msg $log 'cdpLost').Count -eq 0) { $miss += 'no cdpLost' }
            if (@(Get-Msg $log 'cdpLostHandled' | Where-Object { $_.data.state -eq 'failed' }).Count -eq 0) { $miss += 'no cdpLostHandled {state: failed}' }
            return $miss
        }
    },
    [ordered]@{ id = 'R-reconnect-renew'; phases = @('adopt'); test = {
            param([hashtable]$D)
            $miss = @()
            if (@(Get-Msg (Get-PhaseLog $D 'adopt') 'cdpReconnected' | Where-Object { $_.data.trigger -eq 'renewSession' }).Count -eq 0) { $miss += 'no cdpReconnected {trigger: renewSession}' }
            return $miss
        }
    },
    [ordered]@{ id = 'R-version'; phases = @('errors'); test = {
            param([hashtable]$D)
            $miss = @()
            if (@(Get-Msg (Get-PhaseLog $D 'errors') 'browserVersionChanged').Count -eq 0) { $miss += 'no browserVersionChanged' }
            return $miss
        }
    },
    [ordered]@{ id = 'R-port-in-use'; phases = @('errors'); errorCode = 'portInUse' },
    [ordered]@{ id = 'R-profile-in-use'; phases = @('errors'); errorCode = 'profileInUse' },
    [ordered]@{ id = 'R-cdp-unavailable'; phases = @('errors'); errorCode = 'cdpUnavailable' },
    [ordered]@{ id = 'R-probe-existing'; phases = @('adopt'); test = {
            param([hashtable]$D)
            $log = Get-PhaseLog $D 'adopt'
            $miss = @()
            if (@(Get-Msg $log 'probeExisting' | Where-Object { $_.data.outcome -eq 'adopted' }).Count -eq 0) { $miss += 'no probeExisting {outcome: adopted}' }
            if (@(Get-Msg $log 'appAdopted').Count -eq 0) { $miss += 'no appAdopted' }
            return $miss
        }
    },
    [ordered]@{ id = 'R-restart'; phases = @('restart'); test = {
            param([hashtable]$D)
            $changes = @(Get-Msg (Get-PhaseLog $D 'restart') 'appStateChanged' | Where-Object { $_.data.appId -eq $AppId })
            $steps = @($changes | ForEach-Object { '{0}>{1}:{2}' -f $_.data.from, $_.data.to, $_.data.cause })
            for ($i = 0; $i -lt $steps.Count; $i++) {
                if ($steps[$i] -ne 'failed>stopping:RestartApp') { continue }
                for ($j = $i + 1; $j -lt $steps.Count; $j++) {
                    if (-not $steps[$j].StartsWith('stopping>starting:')) { continue }
                    for ($k = $j + 1; $k -lt $steps.Count; $k++) {
                        if ($steps[$k].StartsWith('starting>foreground:')) { return }
                    }
                }
            }
            return ("no appStateChanged failed>stopping:RestartApp followed by stopping>starting and starting>foreground (seen: $(if ($steps.Count) { $steps -join ', ' } else { 'none' }))")
        }
    },
    [ordered]@{ id = 'R-no-sw-hide'; phases = @('tracer', 'adopt', 'restart'); test = {
            param([hashtable]$D)
            $miss = @()
            $afterHome = @(Get-PhaseMeasurements $D 'tracer' | Where-Object { $_.stage -eq 'after-home' })
            if ($afterHome.Count -eq 0) { $miss += 'no tracer after-home measurement' }
            elseif (@($afterHome | Where-Object { (Test-True $_.iconic) -and (Test-True $_.visible) }).Count -eq 0) { $miss += "tracer after-home is not iconic and visible (iconic=$($afterHome[0].iconic) visible=$($afterHome[0].visible))" }
            foreach ($phase in @('tracer', 'adopt', 'restart')) {
                if (@(Get-PhaseMeasurements $D $phase | Where-Object { Test-True $_.iconic }).Count -eq 0) { $miss += "no iconic measurement in $phase" }
            }
            foreach ($phase in @($D.Keys | Sort-Object)) {
                foreach ($m in @(Get-PhaseMeasurements $D $phase | Where-Object { (Test-True $_.iconic) -and -not (Test-True $_.visible) })) {
                    $miss += "$phase/$($m.stage) is iconic but not visible"
                }
            }
            return $miss
        }
    }
)

function Get-Requirements {
    param([hashtable]$Data, [string[]]$SelectedPhases)
    $rows = @()
    foreach ($row in $Requirements) {
        $record = [ordered]@{ type = 'requirement'; id = $row.id; phases = @($row.phases); verdict = 'pass'; detail = '' }
        $notSelected = @($row.phases | Where-Object { $SelectedPhases -notcontains $_ })
        if ($notSelected.Count -gt 0) {
            $record.verdict = 'skipped'
            $record.detail = 'phase not selected: ' + ($notSelected -join ', ')
        }
        else {
            if ($row.Contains('errorCode')) {
                $code = $row.errorCode
                $found = @(Get-Msg (Get-PhaseLog $Data 'errors') 'commandFailed' | Where-Object { $_.data.errorCode -ceq $code })
                $miss = @(if ($found.Count -eq 0) { "no commandFailed {errorCode: $code}" })
            }
            else { $miss = @(& $row.test $Data) }
            if ($miss.Count -gt 0) { $record.verdict = 'fail'; $record.detail = $miss -join '; ' }
        }
        $rows += $record
    }
    return , $rows
}

function Get-Verdict {
    param([object[]]$PhaseRecords, [object[]]$Rows, [bool]$IsolationOk, [string]$PreflightError)
    if (-not [string]::IsNullOrEmpty($PreflightError)) { return 'fail' }
    if (@($PhaseRecords | Where-Object { $_.verdict -ne 'pass' }).Count -gt 0) { return 'fail' }
    if (@($Rows | Where-Object { $_.verdict -eq 'fail' }).Count -gt 0) { return 'fail' }
    if (-not $IsolationOk) { return 'fail' }
    if (@($Rows | Where-Object { $_.verdict -eq 'skipped' }).Count -gt 0) { return 'partial' }
    return 'pass'
}

# Fixture lines `{ phase, source: log|measurement, record }` -> per phase { log, measurements }.
function ConvertTo-PhaseData {
    param([string[]]$Lines)
    $data = @{}
    foreach ($item in (ConvertFrom-JsonLines -Lines $Lines)) {
        if (-not $data.ContainsKey($item.phase)) { $data[$item.phase] = @{ log = @(); measurements = @() } }
        if ($item.source -eq 'log') { $data[$item.phase].log += $item.record }
        elseif ($item.source -eq 'measurement') { $data[$item.phase].measurements += $item.record }
    }
    return $data
}

# ---------------------------------------------------------------------------
# -SelfTest
# ---------------------------------------------------------------------------

function Invoke-SelfTest {
    if (-not (Test-Path -LiteralPath $Fixture)) { throw "Fixture not found: $Fixture" }
    $lines = @([System.IO.File]::ReadAllLines($Fixture, $utf8) | Where-Object { $_.Trim().Length -gt 0 })
    $failures = New-Object System.Collections.Generic.List[string]
    Write-Host "Self-test on $Fixture ($($lines.Count) records, service $Service)"

    $rows = Get-Requirements -Data (ConvertTo-PhaseData -Lines $lines) -SelectedPhases $AllPhases
    foreach ($row in $rows) {
        Write-Host ('  {0,-18} {1}{2}' -f $row.id, $row.verdict, $(if ($row.detail) { ' - ' + $row.detail } else { '' }))
        if ($row.verdict -ne 'pass') { $failures.Add("full fixture: $($row.id) is $($row.verdict): $($row.detail)") }
    }

    $drop = { param([string]$Pattern) return { param([string[]]$L) @($L | Where-Object { $_ -notmatch $Pattern }) }.GetNewClosure() }
    $replace = { param([string]$LinePattern, [string]$From, [string]$To) return { param([string[]]$L) @($L | ForEach-Object { if ($_ -match $LinePattern) { $_.Replace($From, $To) } else { $_ } }) }.GetNewClosure() }
    $devHarness = '{"phase":"tracer","source":"log","record":{"ts":"2026-10-29T21:57:13.300Z","level":"warn","src":"app","msg":"devHarnessEnabled","data":{"type":"Edge","apps":1}}}'
    # Removal mutations (one per row: that row's own markers) and targeted variants of single clauses.
    $mutations = @(
        @{ row = 'R-start'; kind = 'removal'; name = 'tracer appLaunch and browserHostEnabled removed'; apply = (& $drop '"phase":"tracer".*"msg":"(appLaunch|browserHostEnabled)"') },
        @{ row = 'R-navigation'; kind = 'removal'; name = 'tracer reload measurement removed'; apply = (& $drop '"phase":"tracer","source":"measurement".*"stage":"reload"') },
        @{ row = 'R-reconnect-limit'; kind = 'removal'; name = 'cdpLost and cdpLostHandled removed'; apply = (& $drop '"phase":"reconnect".*"msg":"cdpLost(Handled)?"') },
        @{ row = 'R-reconnect-renew'; kind = 'removal'; name = 'cdpReconnected removed'; apply = (& $drop '"phase":"adopt".*"msg":"cdpReconnected"') },
        @{ row = 'R-version'; kind = 'removal'; name = 'browserVersionChanged removed'; apply = (& $drop '"phase":"errors".*"msg":"browserVersionChanged"') },
        @{ row = 'R-port-in-use'; kind = 'removal'; name = 'commandFailed portInUse removed'; apply = (& $drop '"phase":"errors".*"errorCode":"portInUse"') },
        @{ row = 'R-profile-in-use'; kind = 'removal'; name = 'commandFailed profileInUse removed'; apply = (& $drop '"phase":"errors".*"errorCode":"profileInUse"') },
        @{ row = 'R-cdp-unavailable'; kind = 'removal'; name = 'commandFailed cdpUnavailable removed'; apply = (& $drop '"phase":"errors".*"errorCode":"cdpUnavailable"') },
        @{ row = 'R-probe-existing'; kind = 'removal'; name = 'probeExisting and appAdopted removed'; apply = (& $drop '"phase":"adopt".*"msg":"(probeExisting|appAdopted)"') },
        @{ row = 'R-restart'; kind = 'removal'; name = 'restart appStateChanged removed'; apply = (& $drop '"phase":"restart".*"msg":"appStateChanged"') },
        @{ row = 'R-no-sw-hide'; kind = 'removal'; name = 'iconic measurements removed'; apply = (& $drop '"source":"measurement".*"iconic":true') },
        @{ row = 'R-start'; kind = 'variant'; name = 'pageHello of another service'; apply = (& $replace '"phase":"tracer".*"msg":"pageHello"' '"service":"browserhost-sample"' '"service":"netflix"') },
        @{ row = 'R-start'; kind = 'variant'; name = 'appLaunch with --remote-allow-origins'; apply = (& $replace '"msg":"appLaunch"' '"--kiosk"' '"--kiosk","--remote-allow-origins=*"') },
        @{ row = 'R-start'; kind = 'variant'; name = 'devHarnessEnabled present'; apply = { param([string[]]$L) @($L) + $devHarness }.GetNewClosure() },
        @{ row = 'R-navigation'; kind = 'variant'; name = 'only one pageHello'; apply = (& $drop '"phase":"tracer".*"ts":"2026-10-29T21:57:26.800Z".*"msg":"pageHello"') },
        @{ row = 'R-restart'; kind = 'variant'; name = 'RestartApp after starting>foreground'; apply = { param([string[]]$L) @($L | Where-Object { $_ -notmatch 'RestartApp' }) + @($L | Where-Object { $_ -match 'RestartApp' }) } },
        @{ row = 'R-no-sw-hide'; kind = 'variant'; name = 'adopt iconic window not visible'; apply = (& $replace '"phase":"adopt".*"stage":"after-adoption"' '"visible":true' '"visible":false') },
        @{ row = 'R-no-sw-hide'; kind = 'variant'; name = 'tracer after-home removed'; apply = (& $drop '"phase":"tracer".*"stage":"after-home"') },
        @{ row = 'R-reconnect-renew'; kind = 'variant'; name = 'cdpReconnected trigger cdpClosed'; apply = (& $replace '"phase":"adopt".*"msg":"cdpReconnected"' '"trigger":"renewSession"' '"trigger":"cdpClosed"') },
        @{ row = 'R-reconnect-limit'; kind = 'variant'; name = 'cdpLostHandled state null'; apply = (& $replace '"phase":"reconnect".*"msg":"cdpLostHandled"' '"state":"failed"' '"state":null') },
        @{ row = 'R-probe-existing'; kind = 'variant'; name = 'probeExisting outcome launched'; apply = (& $replace '"phase":"adopt".*"msg":"probeExisting"' '"outcome":"adopted"' '"outcome":"launched"') },
        @{ row = 'R-start'; kind = 'variant'; name = 'only browserHostEnabled removed'; apply = (& $drop '"phase":"tracer".*"msg":"browserHostEnabled"') },
        @{ row = 'R-start'; kind = 'variant'; name = 'pageHello webdriver true'; apply = (& $replace '"phase":"tracer","source":"log".*"msg":"pageHello"' '"webdriver":false' '"webdriver":true') },
        @{ row = 'R-start'; kind = 'variant'; name = 'appLaunch without --kiosk'; apply = (& $replace '"phase":"tracer".*"msg":"appLaunch"' '"--kiosk",' '') },
        @{ row = 'R-no-sw-hide'; kind = 'variant'; name = 'restart p6-home not iconic'; apply = (& $replace '"phase":"restart".*"stage":"p6-home"' '"iconic":true' '"iconic":false') }
    )
    foreach ($id in @($Requirements | ForEach-Object { $_.id })) {
        if (@($mutations | Where-Object { $_.row -eq $id -and $_.kind -eq 'removal' }).Count -eq 0) { $failures.Add("no removal mutation for $id") }
    }
    foreach ($mutation in $mutations) {
        $mutated = @(& $mutation.apply $lines)
        $label = '{0} ({1}: {2})' -f $mutation.row, $mutation.kind, $mutation.name
        if (($mutated -join "`n") -eq ($lines -join "`n")) { $failures.Add("mutation $label changed nothing in the fixture"); Write-Host "  FAIL mutation $label changed nothing" -ForegroundColor Red; continue }
        $result = Get-Requirements -Data (ConvertTo-PhaseData -Lines $mutated) -SelectedPhases $AllPhases
        $failed = @($result | Where-Object { $_.verdict -ne 'pass' } | ForEach-Object { $_.id })
        $own = $result | Where-Object { $_.id -eq $mutation.row } | Select-Object -First 1
        $ok = $failed.Count -eq 1 -and $failed[0] -eq $mutation.row
        $status = if ($ok) { 'ok  ' } else { 'FAIL' }
        Write-Host ('  {0} mutation {1} -> failed [{2}]: {3}' -f $status, $label, ($failed -join ', '), $own.detail) -ForegroundColor $(if ($ok) { 'Gray' } else { 'Red' })
        if (-not $ok) { $failures.Add("mutation $label failed [$($failed -join ', ')], expected exactly [$($mutation.row)]") }
    }

    # Subset: unselected phases give skipped rows and a partial summary.
    $subset = Get-Requirements -Data (ConvertTo-PhaseData -Lines $lines) -SelectedPhases @('tracer', 'errors')
    $skipped = @($subset | Where-Object { $_.verdict -eq 'skipped' } | ForEach-Object { $_.id }) -join ','
    $expectedSkipped = 'R-reconnect-limit,R-reconnect-renew,R-probe-existing,R-restart,R-no-sw-hide'
    $subsetVerdict = Get-Verdict -PhaseRecords @(@{ verdict = 'pass' }, @{ verdict = 'pass' }) -Rows $subset -IsolationOk $true -PreflightError ''
    Write-Host ("  subset tracer,errors: skipped [{0}], other rows {1}, verdict {2}" -f $skipped, (@($subset | Where-Object { $_.verdict -ne 'skipped' } | ForEach-Object { $_.verdict } | Sort-Object -Unique) -join ','), $subsetVerdict)
    if ($skipped -ne $expectedSkipped) { $failures.Add("subset: skipped [$skipped], expected [$expectedSkipped]") }
    if (@($subset | Where-Object { $_.verdict -eq 'fail' }).Count -ne 0) { $failures.Add('subset: a selected row failed') }
    if ($subsetVerdict -ne 'partial') { $failures.Add("subset: verdict $subsetVerdict, expected partial") }

    # Verdict rules.
    $passPhase = @{ verdict = 'pass' }
    $cases = @(
        @{ name = 'all pass'; phases = @($passPhase); rows = $rows; iso = $true; pre = ''; expect = 'pass' },
        @{ name = 'a phase fails'; phases = @($passPhase, @{ verdict = 'fail' }); rows = $rows; iso = $true; pre = ''; expect = 'fail' },
        @{ name = 'isolation broken'; phases = @($passPhase); rows = $rows; iso = $false; pre = ''; expect = 'fail' },
        @{ name = 'preflight failed'; phases = @(); rows = @(); iso = $true; pre = 'offline'; expect = 'fail' },
        @{ name = 'skip and fail'; phases = @(@{ verdict = 'fail' }); rows = $subset; iso = $true; pre = ''; expect = 'fail' }
    )
    foreach ($case in $cases) {
        $got = Get-Verdict -PhaseRecords $case.phases -Rows $case.rows -IsolationOk $case.iso -PreflightError $case.pre
        Write-Host ("  verdict '{0}': {1}" -f $case.name, $got)
        if ($got -ne $case.expect) { $failures.Add("verdict '$($case.name)': $got, expected $($case.expect)") }
    }

    # Log window cut: inclusive bounds, lines without ts and blank lines dropped.
    $window = Select-LogWindow -Lines @('{"ts":"2026-10-29T10:00:00.000Z","msg":"a"}', "{`"ts`":`"2026-10-29T10:00:05.000Z`",`"msg`":`"b`"}`r", '{"msg":"no-ts"}', '', '{"ts":"2026-10-29T10:00:10.000Z","msg":"c"}', '{"ts":"2026-10-29T10:00:10.001Z","msg":"d"}') -From ([DateTimeOffset]::Parse('2026-10-29T10:00:05Z')) -To ([DateTimeOffset]::Parse('2026-10-29T10:00:10Z'))
    $cut = @($window | ForEach-Object { ($_ | ConvertFrom-Json).msg }) -join ','
    Write-Host "  log window [10:00:05, 10:00:10]: $cut"
    if ($cut -ne 'b,c') { $failures.Add("log window: [$cut], expected [b,c]") }

    Write-Host ''
    if ($failures.Count -gt 0) {
        Write-Host ('SELF-TEST FAIL ({0})' -f $failures.Count) -ForegroundColor Red
        $failures | ForEach-Object { Write-Host ('  - ' + $_) }
        return 1
    }
    Write-Host ('SELF-TEST PASS: every row passes on the fixture and each of {0} mutations fails exactly its own row.' -f $mutations.Count) -ForegroundColor Green
    return 0
}

if ($SelfTest) { exit (Invoke-SelfTest) }

# ---------------------------------------------------------------------------
# Live run helpers
# ---------------------------------------------------------------------------

function Get-ProfileProcesses {
    return @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.Replace('"', '') -like "*--user-data-dir=$profileDir*" })
}

function Invoke-TaskKill {
    param([int]$ProcessId)
    # Windows PowerShell turns native stderr into a terminating error under 'Stop'; a child that is already exiting
    # makes taskkill /T print "could not be terminated", which is not a failure here.
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & taskkill.exe /PID $ProcessId /T /F 2>&1 | Out-Null } catch { } finally { $ErrorActionPreference = $saved }
}

function Stop-Profile {
    foreach ($root in @(Get-ProfileProcesses | Where-Object { $_.CommandLine -notmatch '--type=' })) { Invoke-TaskKill -ProcessId $root.ProcessId }
    [void](Wait-For -Seconds 10 -Condition { (Get-ProfileProcesses).Count -eq 0 })
}

function Remove-TestProfile {
    Stop-Profile
    if (Test-Path -LiteralPath $profileDir) {
        [void](Wait-For -Seconds 10 -Condition { Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue; -not (Test-Path -LiteralPath $profileDir) })
    }
    return -not (Test-Path -LiteralPath $profileDir)
}

function Stop-InstallProcesses {
    param([string]$Folder)
    if ([string]::IsNullOrEmpty($Folder)) { return }
    foreach ($process in @(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue)) {
        $path = try { $process.Path } catch { $null }
        if ($null -ne $path -and $path.StartsWith($Folder, [StringComparison]::OrdinalIgnoreCase)) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
}

function Get-FileVersionText {
    param([string]$Path)
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    return '{0}.{1}.{2}.{3}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart
}

function Get-Sha256 {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($Path))).Replace('-', '') } finally { $sha.Dispose() }
}

# One command-line argument for CreateProcess: quoted when needed, with backslashes before quotes doubled.
function ConvertTo-Argument {
    param([string]$Value)
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    return '"' + (($Value -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

# Echoes what the child wrote since the last call; returns the new position.
function Write-NewOutput {
    param([string]$Path, [long]$Position)
    if (-not (Test-Path -LiteralPath $Path)) { return $Position }
    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        if ($stream.Length -le $Position) { return $Position }
        [void]$stream.Seek($Position, 'Begin')
        $buffer = New-Object byte[] ($stream.Length - $Position)
        $read = $stream.Read($buffer, 0, $buffer.Length)
        Write-Host -NoNewline ([Console]::OutputEncoding.GetString($buffer, 0, $read))
        return $Position + $read
    } finally { $stream.Dispose() }
}

function Read-ChildOutput {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    return [Console]::OutputEncoding.GetString([System.IO.File]::ReadAllBytes($Path))
}

function Invoke-Phase {
    param([string]$Phase, [string]$Exe, [int]$ReconnectTimeout)
    $dir = Join-Path $Out $Phase
    [void](New-Item -ItemType Directory -Path $dir -Force)
    $script = Join-Path $PSScriptRoot ("check-browserhost-{0}.ps1" -f $Phase)
    $childArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script, '-MediaStation', $Exe, '-Service', $Service, '-EvidenceDir', $dir)
    if ($Phase -in @('tracer', 'reconnect')) { $childArgs += @('-AppId', $AppId, '-CdpPort', [string]$CdpPort) }
    if ($Phase -eq 'reconnect') { $childArgs += @('-ReconnectTimeoutSeconds', [string]$ReconnectTimeout) }
    $stdoutPath = Join-Path $dir 'output.stdout.tmp'
    $stderrPath = Join-Path $dir 'output.stderr.tmp'

    Note "=== phase $Phase ==="
    $start = [DateTimeOffset]::UtcNow
    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList (($childArgs | ForEach-Object { ConvertTo-Argument $_ }) -join ' ') -NoNewWindow -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    [void]$process.Handle  # keeps ExitCode readable after the exit
    $deadline = $start.AddMinutes($PhaseTimeoutMinutes)
    $position = 0L
    $timedOut = $false
    while (-not $process.HasExited) {
        $position = Write-NewOutput -Path $stdoutPath -Position $position
        if ([DateTimeOffset]::UtcNow -gt $deadline) {
            $timedOut = $true
            Invoke-TaskKill -ProcessId $process.Id
            break
        }
        Start-Sleep -Milliseconds 500
    }
    [void]$process.WaitForExit(15000)
    $position = Write-NewOutput -Path $stdoutPath -Position $position
    $end = [DateTimeOffset]::UtcNow
    $exitCode = if ($timedOut) { -1 } else { [int]$process.ExitCode }

    # Output: stdout, then stderr (a child that throws before its own verdict writes there).
    $stdoutText = Read-ChildOutput -Path $stdoutPath
    $stderrText = Read-ChildOutput -Path $stderrPath
    $outputText = $stdoutText
    if ($stderrText.Trim().Length -gt 0) { $outputText += "`n--- stderr ---`n" + $stderrText; Write-Host $stderrText -ForegroundColor Red }
    if ($timedOut) { $outputText += "`n--- killed by check-browserhost-e2e.ps1 after $PhaseTimeoutMinutes min ---`n" }
    [System.IO.File]::WriteAllText((Join-Path $dir 'output.txt'), $outputText, $utf8)
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

    # Whatever a killed or crashed smoke left behind.
    Stop-InstallProcesses -Folder (Split-Path -Parent $Exe)
    Stop-Profile
    $restoreBakLeft = Test-Path -LiteralPath $appsBackupPath
    if ($restoreBakLeft) { Move-Item -LiteralPath $appsBackupPath -Destination $appsPath -Force }

    # The smoke's FAIL lines.
    $failLines = @()
    $inFail = $false
    foreach ($line in ($stdoutText -split "`r?`n")) {
        if ($line -match '^FAIL \(') { $inFail = $true; continue }
        if ($inFail -and $line -match '^  - (.*)$') { $failLines += $Matches[1] }
    }
    if ($exitCode -ne 0 -and $failLines.Count -eq 0) {
        $failLines += @(($stderrText -split "`r?`n") | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 5)
        if ($failLines.Count -eq 0) { $failLines += @(($stdoutText -split "`r?`n") | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -Last 5) }
    }
    if ($timedOut) { $failLines += "timed out after $PhaseTimeoutMinutes min" }
    if ($restoreBakLeft) { $failLines += 'apps.json.restore-bak was left behind (put back by the suite)' }

    # Log slice of the phase window.
    $slice = Select-LogWindow -Lines (Read-LogLines -Since $start) -From $start -To $end
    [System.IO.File]::WriteAllText((Join-Path $dir 'mediastation.jsonl'), $(if ($slice.Count -gt 0) { ($slice -join "`n") + "`n" } else { '' }), $utf8)
    $measurementsPath = Join-Path $dir 'measurements.jsonl'
    $measurements = if (Test-Path -LiteralPath $measurementsPath) { ConvertFrom-JsonLines -Lines ([System.IO.File]::ReadAllLines($measurementsPath, $utf8)) } else { @() }
    $screenshots = @(Get-ChildItem -LiteralPath $dir -Filter '*.png' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })

    $missing = @()
    if (-not (Test-Path -LiteralPath $measurementsPath)) { $missing += 'measurements.jsonl' }
    if ($screenshots.Count -eq 0) { $missing += 'screenshot' }
    if ($slice.Count -eq 0) { $missing += 'mediastation.jsonl lines' }
    $verdict = if ($exitCode -eq 0 -and $missing.Count -eq 0 -and -not $restoreBakLeft) { 'pass' } else { 'fail' }
    Note ("phase {0}: exit {1}, {2}, {3} log lines, {4} measurements, {5} screenshots ({6:N0} s)" -f $Phase, $exitCode, $verdict, $slice.Count, @($measurements).Count, $screenshots.Count, ($end - $start).TotalSeconds)

    return [ordered]@{
        record = [ordered]@{ type = 'phase'; phase = $Phase; script = $script; exitCode = $exitCode; timedOut = $timedOut; verdict = $verdict; startUtc = $start.ToString('o'); endUtc = $end.ToString('o'); durationSeconds = [Math]::Round(($end - $start).TotalSeconds, 1); failLines = @($failLines); missingEvidence = @($missing); evidenceDir = $dir; logLines = $slice.Count; measurements = @($measurements).Count; screenshots = $screenshots; appsJsonRestoreBakLeft = $restoreBakLeft }
        data = @{ log = (ConvertFrom-JsonLines -Lines $slice); measurements = @($measurements) }
    }
}

# ---------------------------------------------------------------------------
# Live run
# ---------------------------------------------------------------------------

$startedUtc = [DateTimeOffset]::UtcNow
# The smokes run in Windows PowerShell. Launched from PowerShell 7 (or a tool started from it) they inherit its
# PSModulePath, load the 7.x Microsoft.PowerShell.Utility and lose Get-FileHash. A console started from Explorer has
# the machine (and user) value; Windows PowerShell adds the user's Documents module path itself.
$env:PSModulePath = (@([Environment]::GetEnvironmentVariable('PSModulePath', 'User'), [Environment]::GetEnvironmentVariable('PSModulePath', 'Machine')) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ';'
[void](New-Item -ItemType Directory -Path $Out -Force)
$machine = [ordered]@{ computer = $env:COMPUTERNAME; board = (Get-CimInstance Win32_BaseBoard).Product; model = (Get-CimInstance Win32_ComputerSystem).Model }
Note ('machine ' + ($machine | ConvertTo-Json -Compress))
Note "report $Out"

$preflight = New-Object System.Collections.Generic.List[string]
if ($Publish) {
    Note 'dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained'
    & dotnet publish (Join-Path $repoRoot 'src\MediaStation.App') -c Release -r win-x64 --self-contained
    if ($LASTEXITCODE -ne 0) { $preflight.Add("dotnet publish failed (exit $LASTEXITCODE)") }
}
$publishDir = Split-Path -Parent $MediaStation
if (-not (Test-Path -LiteralPath $MediaStation)) { $preflight.Add("MediaStation.exe not found: $MediaStation - publish Release first (or pass -Publish)") }
elseif (-not (Test-Path -LiteralPath (Join-Path $publishDir 'page\runtime.js'))) { $preflight.Add("the publish has no page\runtime.js next to $MediaStation") }
if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) { $preflight.Add('MediaStation is running; close it and retry') }
try {
    if ([Net.ServicePointManager]::SecurityProtocol -ne [Net.SecurityProtocolType]::SystemDefault) { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 }
    $reply = Invoke-WebRequest -Uri $StartUrl -UseBasicParsing -TimeoutSec 20
    Note ("sample start URL {0}: HTTP {1}" -f $StartUrl, $reply.StatusCode)
} catch { $preflight.Add("the sample start URL $StartUrl is unreachable (offline?): $($_.Exception.Message)") }
if (@(Get-NetTCPConnection -LocalPort $CdpPort -State Listen -ErrorAction SilentlyContinue).Count -gt 0) { $preflight.Add("port $CdpPort is already listening; free it and retry") }
if (-not (Test-Path -LiteralPath $appsPath)) { $preflight.Add("$appsPath does not exist") }
if (Test-Path -LiteralPath $appsBackupPath) { $preflight.Add("an orphaned $appsBackupPath exists; put it back as apps.json (or delete it) and retry") }
$edgeExe = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\Application\msedge.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($null -eq $edgeExe) { $preflight.Add('msedge.exe not found') }
$edgeVersion = if ($null -ne $edgeExe) { Get-FileVersionText -Path $edgeExe } else { $null }
$preflightError = $preflight -join '; '

$cdpReconnectTimeout = 30
try {
    $settings = Get-Content -LiteralPath (Join-Path $configDir 'settings.json') -Raw | ConvertFrom-Json
    if ($null -ne $settings.cdpReconnectTimeout) { $cdpReconnectTimeout = [int]$settings.cdpReconnectTimeout }
} catch { }

$appsHashBefore = Get-Sha256 -Path $appsPath
$installRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('mediastation-browserhost-e2e-' + $ts)
$installApp = Join-Path $installRoot 'app'
$installCreated = $false
$installRemoved = $true
$profileRemoved = $true
$phaseRecords = @()
$phaseData = @{}
$runError = ''

if ($preflight.Count -gt 0) {
    Note "PREFLIGHT FAIL: $preflightError"
}
else {
    try {
        Note "install copy $installApp"
        [void](New-Item -ItemType Directory -Path $installRoot -Force)
        $installCreated = $true
        Copy-Item -LiteralPath $publishDir -Destination $installApp -Recurse -Force
        $driverDir = Join-Path $installApp ('page\drivers\' + $Service)
        [void](New-Item -ItemType Directory -Path $driverDir -Force)
        Copy-Item -LiteralPath $sampleManifestPath -Destination (Join-Path $driverDir 'manifest.json') -Force
        $exe = Join-Path $installApp (Split-Path -Leaf $MediaStation)

        if (-not (Remove-TestProfile)) { throw "Could not delete $profileDir before the first phase." }
        foreach ($phase in $Selected) {
            $result = Invoke-Phase -Phase $phase -Exe $exe -ReconnectTimeout $cdpReconnectTimeout
            $phaseRecords += $result.record
            $phaseData[$phase] = $result.data
        }
    }
    catch {
        $runError = $_.Exception.Message
        Note "RUN ABORTED: $runError"
    }
    finally {
        Stop-InstallProcesses -Folder $installApp
        $profileRemoved = Remove-TestProfile
        if ($installCreated -and -not $KeepInstall) {
            [void](Wait-For -Seconds 15 -Condition { Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue; -not (Test-Path -LiteralPath $installRoot) })
            $installRemoved = -not (Test-Path -LiteralPath $installRoot)
        }
        elseif ($KeepInstall) { $installRemoved = $false }
    }
}

# Isolation: recorded as found, then a leftover backup is put back.
$appsHashAfter = Get-Sha256 -Path $appsPath
$restoreBakLeft = Test-Path -LiteralPath $appsBackupPath
$restoreBakRepaired = $false
if ($restoreBakLeft -and $preflight.Count -eq 0) { Move-Item -LiteralPath $appsBackupPath -Destination $appsPath -Force; $restoreBakRepaired = $true }
$appsUnchanged = $null -ne $appsHashBefore -and $appsHashBefore -eq $appsHashAfter
$isolationOk = $appsUnchanged -and -not $restoreBakLeft -and $profileRemoved -and ($installRemoved -or $KeepInstall)
$isolation = [ordered]@{ type = 'isolation'; ok = $isolationOk; appsJson = $appsPath; sha256Before = $appsHashBefore; sha256After = $appsHashAfter; appsJsonUnchanged = $appsUnchanged; restoreBakLeft = $restoreBakLeft; restoreBakRepaired = $restoreBakRepaired; profile = $profileDir; profileRemoved = $profileRemoved; installCopy = $installApp; installCreated = $installCreated; installRemoved = ($installCreated -and $installRemoved); keepInstall = [bool]$KeepInstall; publishedFolder = $publishDir }

$rows = if ($preflight.Count -eq 0) { Get-Requirements -Data $phaseData -SelectedPhases $Selected } else { @() }
$reason = @($preflightError, $runError) | Where-Object { -not [string]::IsNullOrEmpty($_) }
$verdict = Get-Verdict -PhaseRecords $phaseRecords -Rows $rows -IsolationOk $isolationOk -PreflightError (($reason) -join '; ')
# A run that aborted before all selected phases ran is a failure even if what ran passed.
if ($phaseRecords.Count -ne $Selected.Count -and $preflight.Count -eq 0) { $verdict = 'fail' }
$exitCode = if ($verdict -eq 'pass') { 0 } else { 1 }
$endedUtc = [DateTimeOffset]::UtcNow
$summary = [ordered]@{
    type = 'summary'; verdict = $verdict; reason = (@($reason) -join '; '); machine = $machine; edge = [ordered]@{ path = $edgeExe; fileVersion = $edgeVersion }
    installCopy = $installApp; sampleService = $Service; sampleStartUrl = $StartUrl; appId = $AppId; cdpPort = $CdpPort; cdpReconnectTimeout = $cdpReconnectTimeout
    phasesSelected = $Selected; phasesPassed = @($phaseRecords | Where-Object { $_.verdict -eq 'pass' } | ForEach-Object { $_.phase })
    requirements = [ordered]@{ pass = @($rows | Where-Object { $_.verdict -eq 'pass' }).Count; fail = @($rows | Where-Object { $_.verdict -eq 'fail' }).Count; skipped = @($rows | Where-Object { $_.verdict -eq 'skipped' }).Count }
    isolationOk = $isolationOk; startUtc = $startedUtc.ToString('o'); endUtc = $endedUtc.ToString('o'); reportDir = $Out; exitCode = $exitCode
}
Write-Jsonl -Path (Join-Path $Out 'report.jsonl') -Records (@($phaseRecords) + @($rows) + @($isolation) + @($summary))

Write-Host ''
Write-Host ('Machine: {0} / {1} / {2}; Edge {3}' -f $machine.computer, $machine.board, $machine.model, $edgeVersion)
foreach ($p in $phaseRecords) {
    Write-Host ('  phase {0,-10} exit {1,-3} {2}' -f $p.phase, $p.exitCode, $p.verdict) -ForegroundColor $(if ($p.verdict -eq 'pass') { 'Gray' } else { 'Red' })
    foreach ($line in $p.failLines) { Write-Host ('      - ' + $line) }
    if ($p.missingEvidence.Count -gt 0) { Write-Host ('      - missing evidence: ' + ($p.missingEvidence -join ', ')) }
}
foreach ($row in $rows) {
    Write-Host ('  {0,-18} {1,-8} {2}' -f $row.id, $row.verdict, $row.detail) -ForegroundColor $(switch ($row.verdict) { 'pass' { 'Gray' } 'skipped' { 'Yellow' } default { 'Red' } })
}
Write-Host ('  isolation          {0}' -f $(if ($isolationOk) { 'ok' } else { 'BROKEN ' + (($isolation | ConvertTo-Json -Compress)) }))
Write-Host ''
Write-Host ('Report: ' + (Join-Path $Out 'report.jsonl'))
$color = switch ($verdict) { 'pass' { 'Green' } 'partial' { 'Yellow' } default { 'Red' } }
Write-Host ('{0}: {1}' -f $verdict.ToUpperInvariant(), $(if ($summary.reason) { $summary.reason } else { "$($phaseRecords.Count) phase(s), $($summary.requirements.pass) pass / $($summary.requirements.fail) fail / $($summary.requirements.skipped) skipped" })) -ForegroundColor $color
exit $exitCode
