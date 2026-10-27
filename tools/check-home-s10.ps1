<#
.SYNOPSIS
    S10 acceptance run for the Home key: every target x every press must produce exactly one
    `action {kind: home}` in the application log and no new browser window, with the shell UI
    thread hung. A second pass presses the same Home with the shell on top, not hung.

.DESCRIPTION
    The measurement reads only the application log (%LOCALAPPDATA%\MediaStation\logs\mediastation-*.jsonl)
    through FileShare.ReadWrite, and counts only entries of the current process (boundary: the last
    `start {pid}`). For each press the script records `From` (UTC) just before the beep, waits up to
    -PressTimeoutSeconds for a new `action home`, settles for -SettleSeconds and then scores the window
    [From, To): exactly one `action home`, no new browser window and the target window in the foreground are
    a pass; zero actions is "no action", more than one is "double action", a new window is "browser window"
    and another foreground window is "target not foreground". A browser window is every visible top-level
    `msedge`/`chrome` window of class `Chrome_WidgetWin_*`, identified as `pid:handle`, so a second window in
    an existing process is a new window too. The foreground check compares the target handle with the
    foreground handle, not the process, so another window of the same process is not a pass; one shell
    exception is named: the desktop window (`Progman`/`WorkerW` owned by `explorer`, target `desktop`). A DWM
    ghost window (`dwm`) over the shell is a failure ("target not foreground (DWM ghost over the shell)"):
    the keyboard hook receives no input while the ghost has the foreground.
    -EdgeTitle is a regular expression matched against the window title.

    Targets run in the order of -Targets (default `shell-hung, edge, moonlight, desktop`): the hung shell goes
    first and the desktop last, because MinimizeAll leaves a hung shell behind and Windows then replaces it
    with a ghost. Before the first press the script opens an `about:blank` Edge window when none is open
    (-EdgeTitle narrows the Edge window by title); Moonlight is not started by the script: open it
    (borderless) before the run, otherwise its presses fail with "no target window".

    The person only presses Home after each beep and touches nothing else.

    Pass 1 starts MediaStation with MEDIASTATION_DEBUG_HANG_UI=<HangSeconds>, waits for the `debugHangUi`
    entry (proof of the hang; the hang expires at `ts` + `seconds`) and runs -Targets in order. Every
    pass-1 press (not only `shell-hung`) must finish before the hang expires, otherwise it fails with
    "hang expired". A hung shell does not process WM_CLOSE, so the instance is force-stopped after the
    pass. Pass 2 starts the application without the variable, waits for `snapshotPublished`, presses Home
    -Presses times and closes the application via CloseMainWindow (expected `stop {exitCode: 0}`).

    The run log goes to -Out (default under the logs directory, never into the repository). Exit code 0
    only when pass 1 is X/<targets*presses> and pass 2 is Y/<presses> (unless -SkipShellTopPass).

    -SelfTest scores the recorded fixture without any hardware: four windows must give counts
    0/1/2/1 and verdicts fail/pass/fail/pass, and a correct count plus a new browser window must fail.
    It also exercises the clean decision cases (window ids, new ids, foreground) with fake windows, so
    the rules that used to need a live desktop (DW-13, DW-14, the Edge window choice, the ghost and the target
    order of 3.13) are covered before Add-Type.

.NOTES
    -InjectHome replaces the beep and the wait for a person with a `SendInput` `VK_HOME` (extended key, no
    `dwExtraInfo` marker), so the product sees a real keyboard press.
    -InjectHome only affects the S10 passes; -Restore always injects Home (and OK), with or without the switch.

    -Restore runs the shell-restore pass (no log counting): for each target in -Targets (default
    `edge, chrome, moonlight, desktop`) a fresh MediaStation instance starts with MEDIASTATION_DEV_HARNESS=1 and a
    generated `apps.json` with one tile (the real file is copied and put back in `finally`; an orphaned copy
    of an interrupted run is put back first). An injected OK opens the tile, the script waits for the
    `appWindowAttached` entry (target window = `hwnd`/`rootPid` of that entry, filtered by the OS window list)
    and then, -Presses times: target to the front, injected Home, verdict from the OS: shell in the foreground,
    target `IsIconic` and visible, no new browser window (desktop: no target-window condition). `windowCommand`
    entries are listed as extra evidence only. Moonlight needs MEDIASTATION_HARNESS_MOONLIGHT, -MoonlightHost
    and a reachable Sunshine port (-SunshinePort), otherwise it is "n/a" with the reason and is not counted.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -Restore -InjectHome

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1
#>
param(
    [string[]]$Targets = @('shell-hung', 'edge', 'moonlight', 'desktop'),
    [string]$EdgeTitle = '',
    [int]$Presses = 4,
    [string]$MediaStation = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [string]$Out = '',
    [int]$HangSeconds = 600,
    [int]$PressTimeoutSeconds = 45,
    [int]$SettleSeconds = 2,
    [switch]$SkipShellTopPass,
    [switch]$Restore,
    [switch]$InjectHome,
    [string]$MoonlightHost = '',
    [string]$MoonlightApp = 'Desktop',
    [int]$SunshinePort = 47989,
    [switch]$SelfTest,
    [string]$Fixture = ''
)

$ErrorActionPreference = 'Stop'

$script:TargetsBound = $PSBoundParameters.ContainsKey('Targets')

if ([string]::IsNullOrWhiteSpace($Fixture)) {
    $Fixture = Join-Path $PSScriptRoot 'fixtures\check-home-s10.fixture.jsonl'
}

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe'
}

if ([string]::IsNullOrWhiteSpace($Out)) {
    $Out = Join-Path $LogsDir ('check-home-s10-{0}.log' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}

# "edge,moonlight" and @('edge','moonlight') are both accepted.
$Targets = @($Targets | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_.Trim() })

$script:AppProcessId = 0

# ---------------------------------------------------------------------------
# Log reading and scoring (the same functions serve the live run and -SelfTest)
# ---------------------------------------------------------------------------

function ConvertTo-Utc {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    return [DateTimeOffset]::Parse(
        $Value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::AssumeUniversal)
}

function Get-LogPaths {
    # Daily rotation means a new file; paths are recomputed on every read (also across UTC midnight).
    return @(Get-ChildItem -LiteralPath $LogsDir -Filter 'mediastation-*.jsonl' -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -ExpandProperty FullName)
}

function Read-LogEntries {
    param([string[]]$Path)

    $entries = @()
    foreach ($file in $Path) {
        if (-not (Test-Path -LiteralPath $file)) {
            continue
        }

        # FileShare.ReadWrite: the application opens the log per write, so the handle must be shared.
        $stream = [System.IO.File]::Open($file, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
            try {
                $lines = @($reader.ReadToEnd() -split "`n" | Where-Object { $_.Trim().Length -gt 0 })
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }

        foreach ($line in $lines) {
            try {
                $entries += ($line | ConvertFrom-Json -ErrorAction Stop)
            }
            catch {
                # The application is still writing this line: skip it and read it on the next attempt.
            }
        }
    }

    return $entries
}

function Get-RunEntries {
    param(
        [int]$ProcessId,
        [object[]]$Entries = $null
    )

    $source = $Entries
    if ($null -eq $source) {
        $source = @(Read-LogEntries -Path @(Get-LogPaths))
    }

    # The last `start` of this process marks the beginning of the current run.
    $startIndex = -1
    for ($i = 0; $i -lt $source.Count; $i++) {
        $entry = $source[$i]
        if ($entry.msg -eq 'start' -and $entry.data.pid -eq $ProcessId) {
            $startIndex = $i
        }
    }

    if ($startIndex -lt 0) {
        return @()
    }

    return @($source | Select-Object -Skip ($startIndex + 1))
}

function Get-HomeActions {
    param([object[]]$Entries)

    $homes = @()
    foreach ($entry in $Entries) {
        if ($entry.msg -ne 'action' -or $null -eq $entry.data -or $entry.data.kind -ne 'home') {
            continue
        }

        $ts = ConvertTo-Utc ([string]$entry.ts)
        if ($null -eq $ts) {
            continue
        }

        $homes += [pscustomobject]@{
            Ts    = $ts
            Entry = $entry
        }
    }

    return $homes
}

function Get-PressCount {
    param(
        [object[]]$Homes,
        [DateTimeOffset]$From,
        [DateTimeOffset]$To
    )

    # `From` inclusive, `To` exclusive; `ts` is the only order shared by all logging threads.
    return @($Homes | Where-Object { $_.Ts -ge $From -and $_.Ts -lt $To }).Count
}

function Test-HasOwner {
    param($Window)

    # `Owner` is the GW_OWNER handle; windows built without it (older callers, fixtures) have no owner.
    if ($null -eq $Window.Owner) {
        return $false
    }

    return ([int64]$Window.Owner) -ne 0
}

function Get-BrowserWindowIds {
    param([object[]]$Windows)

    # Unswallowed Home opens a new browser window, so one `pid:handle` per window (not per process) makes a second
    # window in an existing Edge/Chrome process visible (DW-13). EnumWindows also returns tooltips and popups of
    # the same process; they appear and vanish by themselves between `before` and `after`, so only real browser
    # windows (`Chrome_WidgetWin_*`, the Chromium window class) count.
    $ids = @()
    foreach ($window in $Windows) {
        if ($window.ProcessName -notin @('msedge', 'chrome')) {
            continue
        }

        if (-not ([string]$window.Class).StartsWith('Chrome_WidgetWin_', [System.StringComparison]::Ordinal)) {
            continue
        }

        # Owned windows are popups/dialogs of a main window, not new browser windows.
        if (Test-HasOwner $window) {
            continue
        }

        $ids += '{0}:{1}' -f $window.ProcessId, ([IntPtr]$window.Handle).ToInt64()
    }

    return $ids
}

function Get-NewWindowIds {
    param(
        [string[]]$Before = @(),
        [string[]]$After = @()
    )

    return @($After | Where-Object { $Before -notcontains $_ })
}

function Select-EdgeWindow {
    param(
        [object[]]$Windows,
        [string]$TitlePattern = '',
        [int[]]$TreePids = $null
    )

    # The first (topmost) real Edge window with a title; the title is not part of the contract, so the default
    # accepts any (3.13: the window of the person was not called "Netflix"). A tooltip/popup has another class.
    # Owned windows are skipped. With -TreePids (the process tree of the `rootPid` of `appWindowAttached`) only the
    # visible, ownerless `Chrome_WidgetWin_1` window of that tree counts, whatever its title (DW-15, DW-32).
    foreach ($window in $Windows) {
        if ($window.ProcessName -ne 'msedge') {
            continue
        }

        if (Test-HasOwner $window) {
            continue
        }

        if ($null -ne $TreePids) {
            if ($window.Class -cne 'Chrome_WidgetWin_1' -or $TreePids -notcontains [int]$window.ProcessId) {
                continue
            }
        }
        else {
            if (-not ([string]$window.Class).StartsWith('Chrome_WidgetWin_', [System.StringComparison]::Ordinal)) {
                continue
            }

            if ([string]::IsNullOrWhiteSpace([string]$window.Title)) {
                continue
            }
        }

        if ($TitlePattern.Length -gt 0 -and $window.Title -notmatch $TitlePattern) {
            continue
        }

        return [IntPtr]$window.Handle
    }

    return [IntPtr]::Zero
}

function Get-ProcessTree {
    param(
        [int]$RootPid,
        [hashtable]$ParentOf
    )

    # Root and all descendants from a pid -> ppid map; the visited list keeps cycles (reused pids) from looping.
    $tree = New-Object 'System.Collections.Generic.List[int]'
    $tree.Add($RootPid)
    $index = 0
    while ($index -lt $tree.Count) {
        $current = $tree[$index]
        $index++
        foreach ($key in @($ParentOf.Keys)) {
            if ([int]$ParentOf[$key] -eq $current -and -not $tree.Contains([int]$key)) {
                $tree.Add([int]$key)
            }
        }
    }

    return [int[]]$tree.ToArray()
}

function Get-AttachedTarget {
    param(
        [object[]]$Entries,
        [string]$AppId
    )

    # The last `appWindowAttached` of the app wins: a restarted app gets a new window and root.
    $found = $null
    foreach ($entry in $Entries) {
        if ($entry.msg -eq 'appWindowAttached' -and $null -ne $entry.data -and $entry.data.appId -eq $AppId) {
            $found = [pscustomobject]@{ Hwnd = [int64]$entry.data.hwnd; RootPid = [int]$entry.data.rootPid }
        }
    }

    return $found
}

function Select-RestoreTargetWindow {
    param(
        [object[]]$Windows,
        $Attached,
        [int[]]$TreePids,
        [switch]$AnyClass
    )

    # Only a visible (the enumeration lists visible windows), ownerless window of the `rootPid` tree; browsers need
    # the main window class `Chrome_WidgetWin_1`, Moonlight (SDL/Qt) takes any class. The `hwnd` from the log is
    # preferred when it still qualifies. No `Attached` entry means no target.
    if ($null -eq $Attached) {
        return [IntPtr]::Zero
    }

    $candidates = @()
    foreach ($window in $Windows) {
        if (Test-HasOwner $window) {
            continue
        }

        if ($TreePids -notcontains [int]$window.ProcessId) {
            continue
        }

        if (-not $AnyClass -and $window.Class -cne 'Chrome_WidgetWin_1') {
            continue
        }

        $candidates += $window
    }

    foreach ($window in $candidates) {
        if (([IntPtr]$window.Handle).ToInt64() -eq $Attached.Hwnd) {
            return [IntPtr]$window.Handle
        }
    }

    if ($candidates.Count -gt 0) {
        return [IntPtr]$candidates[0].Handle
    }

    return [IntPtr]::Zero
}

function Test-RestoreVerdict {
    param(
        [string]$Target,
        [bool]$ShellForeground,
        [bool]$TargetIconic,
        [bool]$TargetVisible,
        [string[]]$NewBrowserWindow = @()
    )

    # The verdict comes from the OS window state after Home, not from the log (`windowCommand` is extra evidence).
    if (-not $ShellForeground) {
        return [pscustomobject]@{ Ok = $false; Reason = 'shell not foreground' }
    }

    if ($Target -ne 'desktop') {
        if (-not $TargetIconic) {
            return [pscustomobject]@{ Ok = $false; Reason = 'target not minimized' }
        }

        if (-not $TargetVisible) {
            return [pscustomobject]@{ Ok = $false; Reason = 'target not visible' }
        }
    }

    if ($null -ne $NewBrowserWindow -and @($NewBrowserWindow).Count -gt 0) {
        return [pscustomobject]@{ Ok = $false; Reason = 'browser window' }
    }

    return [pscustomobject]@{ Ok = $true; Reason = 'ok' }
}

function Get-RestoreSkipReason {
    param(
        [string]$Target,
        [string]$MoonlightExe = '',
        [string]$MoonlightHostName = '',
        [bool]$SunshineReachable = $false
    )

    # Only Moonlight can be "n/a": it needs the client, a host and a reachable Sunshine. Returns '' when it applies.
    if ($Target -ne 'moonlight') {
        return ''
    }

    if ([string]::IsNullOrWhiteSpace($MoonlightExe)) {
        return 'MEDIASTATION_HARNESS_MOONLIGHT is not set'
    }

    if (-not (Test-Path -LiteralPath $MoonlightExe)) {
        return "Moonlight.exe not found: $MoonlightExe"
    }

    if ([string]::IsNullOrWhiteSpace($MoonlightHostName)) {
        return 'no -MoonlightHost'
    }

    if (-not $SunshineReachable) {
        return 'Sunshine port not reachable'
    }

    return ''
}

function New-RestoreAppsJson {
    param(
        [string]$Target,
        [string]$MoonlightHostName = '',
        [string]$MoonlightAppName = 'Desktop'
    )

    # One tile per target. The desktop target uses an Edge tile: the app only has to exist before MinimizeAll.
    # Every tile is in the `video` category: with a lone `game` tile the injected OK did not start it (0/4, no StartApp), so the
    # moonlight target never ran; a lone `game` category is not what this pass checks (bug 5.8).
    $id = "restore-$Target"
    switch ($Target) {
        'moonlight' {
            $entry = [ordered]@{ type = 'moonlight'; id = $id; category = 'video'; title = 'Restore'; icon = 'restore.png'; host = $MoonlightHostName; app = $MoonlightAppName }
        }
        'chrome' {
            $entry = [ordered]@{ type = 'web'; id = $id; category = 'video'; title = 'Restore'; icon = 'restore.png'; service = 'netflix'; browser = 'chrome'; cdpPort = 9222 }
        }
        default {
            $entry = [ordered]@{ type = 'web'; id = $id; category = 'video'; title = 'Restore'; icon = 'restore.png'; service = 'netflix'; browser = 'edge'; cdpPort = 9222 }
        }
    }

    return (@{ apps = @($entry) } | ConvertTo-Json -Depth 5)
}

function Test-TargetForeground {
    param(
        [string]$Target,
        [IntPtr]$ForegroundHandle,
        [IntPtr]$TargetHandle,
        [string]$ForegroundProcessName = '',
        [string]$ForegroundClass = ''
    )

    if ($ForegroundHandle -eq [IntPtr]::Zero -or $TargetHandle -eq [IntPtr]::Zero) {
        return $false
    }

    # The strict rule: the target window itself is in the foreground, so another window of the same process fails.
    if ($ForegroundHandle -eq $TargetHandle) {
        return $true
    }

    # A DWM ghost (dwm.exe) over the hung shell is not "shell on top": the keyboard hook receives no input while the
    # ghost has the foreground (no keyEnter/action in the application log, Home then opens a browser), so a press
    # measured there would test Windows, not the product (3.13).
    # After MinimizeAll the desktop window is `Progman` (FindWindow) or `WorkerW` (GetForegroundWindow); both belong
    # to explorer. The class name is not reserved, so the process gate keeps a `WorkerW` window of another app
    # (e.g. a wallpaper app) from passing.
    if ($Target -eq 'desktop' -and $ForegroundProcessName -eq 'explorer' -and
        ($ForegroundClass -eq 'Progman' -or $ForegroundClass -eq 'WorkerW')) {
        return $true
    }

    return $false
}

function Get-NotForegroundReason {
    param(
        [string]$Target,
        [string]$ForegroundProcessName = ''
    )

    # Names the DWM ghost over the shell (hung or on top), so the log says why the press never measured the product.
    if ($Target -in @('shell-hung', 'shell-top') -and $ForegroundProcessName -eq 'dwm') {
        return 'target not foreground (DWM ghost over the shell)'
    }

    return 'target not foreground'
}

function Test-Press {
    param(
        [object[]]$Homes,
        [DateTimeOffset]$From,
        [DateTimeOffset]$To,
        [string[]]$NewBrowserWindow = @()
    )

    $count = Get-PressCount -Homes $Homes -From $From -To $To

    # A new browser window wins over the action count: the Home was not swallowed.
    if ($null -ne $NewBrowserWindow -and @($NewBrowserWindow).Count -gt 0) {
        return [pscustomobject]@{ Ok = $false; Count = $count; Reason = 'browser window' }
    }

    if ($count -eq 0) {
        return [pscustomobject]@{ Ok = $false; Count = 0; Reason = 'no action' }
    }

    if ($count -gt 1) {
        return [pscustomobject]@{ Ok = $false; Count = $count; Reason = 'double action' }
    }

    return [pscustomobject]@{ Ok = $true; Count = 1; Reason = 'ok' }
}

# ---------------------------------------------------------------------------
# Self-test: score the fixture without hardware
# ---------------------------------------------------------------------------

function Invoke-SelfTest {
    param([string]$FixturePath)

    $problems = New-Object System.Collections.Generic.List[string]

    # Absolute windows in the fixture timeline, asserted in this order: 0/1/2/1.
    # The fourth window has one Home inside and two Home presses outside it (one exactly at `To`).
    $windows = @(
        [pscustomobject]@{ Name = 'missing'; From = '2026-10-21T12:00:01.000Z'; To = '2026-10-21T12:00:03.000Z'; Count = 0; Verdict = 'fail' }
        [pscustomobject]@{ Name = 'correct'; From = '2026-10-21T12:00:07.000Z'; To = '2026-10-21T12:00:09.000Z'; Count = 1; Verdict = 'pass' }
        [pscustomobject]@{ Name = 'double'; From = '2026-10-21T12:00:04.000Z'; To = '2026-10-21T12:00:06.000Z'; Count = 2; Verdict = 'fail' }
        [pscustomobject]@{ Name = 'outside'; From = '2026-10-21T12:00:10.000Z'; To = '2026-10-21T12:00:12.000Z'; Count = 1; Verdict = 'pass' }
    )

    if (-not (Test-Path -LiteralPath $FixturePath)) {
        Write-Host ("FAIL: fixture not found: {0}" -f $FixturePath)
        Write-Host 'Self-test FAILED.'
        return 1
    }

    try {
        $entries = @(Read-LogEntries -Path @($FixturePath))
    }
    catch {
        Write-Host ("FAIL: fixture unreadable: {0}" -f $_.Exception.Message)
        Write-Host 'Self-test FAILED.'
        return 1
    }

    $run = @(Get-RunEntries -ProcessId 4242 -Entries $entries)
    if ($run.Count -eq 0) {
        $problems.Add('fixture has no start entry for pid 4242, so no measurement window can be built')
    }

    $homes = @(Get-HomeActions -Entries $run)
    Write-Host ("fixture: {0} lines, {1} run entries, {2} action home" -f $entries.Count, $run.Count, $homes.Count)

    foreach ($window in $windows) {
        $result = Test-Press -Homes $homes -From (ConvertTo-Utc $window.From) -To (ConvertTo-Utc $window.To)

        $verdict = 'pass'
        if (-not $result.Ok) {
            $verdict = 'fail'
        }

        Write-Host ("SelfTest '{0}': count {1}, verdict {2} ({3}) - expected count {4}, {5}" -f
            $window.Name, $result.Count, $verdict, $result.Reason, $window.Count, $window.Verdict)

        if ($result.Count -ne $window.Count) {
            $problems.Add(("SelfTest '{0}': count {1} instead of {2}" -f $window.Name, $result.Count, $window.Count))
        }

        if ($verdict -ne $window.Verdict) {
            $problems.Add(("SelfTest '{0}': verdict {1} instead of {2}" -f $window.Name, $verdict, $window.Verdict))
        }
    }

    # A correct action count plus a new browser window is still a failure.
    $correct = $windows | Where-Object { $_.Name -eq 'correct' } | Select-Object -First 1
    $browser = Test-Press -Homes $homes -From (ConvertTo-Utc $correct.From) -To (ConvertTo-Utc $correct.To) -NewBrowserWindow @('4242:1111')
    $browserVerdict = 'failed as expected'
    if ($browser.Ok) {
        $browserVerdict = 'unexpectedly passed'
    }

    Write-Host ("SelfTest 'browser window': count {0}, {1} ({2}) - expected failure reason 'browser window'" -f
        $browser.Count, $browserVerdict, $browser.Reason)

    if ($browser.Ok -or $browser.Reason -ne 'browser window') {
        $problems.Add("SelfTest 'browser window': expected failure reason 'browser window', got '$($browser.Reason)'")
    }

    # Clean decision cases without Add-Type: window ids, new ids and the foreground rule (DW-13, DW-14).
    $expect = {
        param([string]$Name, $Actual, $Expected)
        Write-Host ("SelfTest '{0}': {1} - expected {2}" -f $Name, $Actual, $Expected)
        if ($Actual -ne $Expected) {
            $problems.Add(("SelfTest '{0}': got '{1}' instead of '{2}'" -f $Name, $Actual, $Expected))
        }
    }

    $decisionWindows = @(
        [pscustomobject]@{ Handle = [IntPtr]273; ProcessId = 4242; ProcessName = 'msedge'; Class = 'Chrome_WidgetWin_1'; Title = 'Netflix' }
        [pscustomobject]@{ Handle = [IntPtr]546; ProcessId = 4242; ProcessName = 'msedge'; Class = 'Chrome_WidgetWin_1'; Title = 'Second window' }
        [pscustomobject]@{ Handle = [IntPtr]777; ProcessId = 4242; ProcessName = 'msedge'; Class = 'Chrome_Tooltip_Window'; Title = '' }
        [pscustomobject]@{ Handle = [IntPtr]888; ProcessId = 5353; ProcessName = 'notepad'; Class = 'Chrome_WidgetWin_1'; Title = 'Other app' }
        [pscustomobject]@{ Handle = [IntPtr]1365; ProcessId = 4343; ProcessName = 'chrome'; Class = 'Chrome_WidgetWin_0'; Title = 'Chrome' }
    )

    & $expect 'two windows of one pid give two ids' ((Get-BrowserWindowIds -Windows $decisionWindows) -join ',') '4242:273,4242:546,4343:1365'
    & $expect 'tooltip window gives no id' ((Get-BrowserWindowIds -Windows @($decisionWindows[2])) -join ',') ''
    & $expect 'new window in the same process is new' ((Get-NewWindowIds -Before @('4242:273') -After @('4242:273', '4242:546')) -join ',') '4242:546'
    & $expect 'same handle is foreground' (Test-TargetForeground -Target 'edge' -ForegroundHandle ([IntPtr]273) -TargetHandle ([IntPtr]273) -ForegroundProcessName 'msedge' -ForegroundClass 'Chrome_WidgetWin_1') $true
    & $expect 'other window of the same process is not foreground' (Test-TargetForeground -Target 'edge' -ForegroundHandle ([IntPtr]546) -TargetHandle ([IntPtr]273) -ForegroundProcessName 'msedge' -ForegroundClass 'Chrome_WidgetWin_1') $false
    & $expect 'dwm ghost does not pass for a hung shell' (Test-TargetForeground -Target 'shell-hung' -ForegroundHandle ([IntPtr]900) -TargetHandle ([IntPtr]273) -ForegroundProcessName 'dwm' -ForegroundClass 'Ghost') $false
    & $expect 'real hung shell window passes for a hung shell' (Test-TargetForeground -Target 'shell-hung' -ForegroundHandle ([IntPtr]273) -TargetHandle ([IntPtr]273) -ForegroundProcessName 'MediaStation' -ForegroundClass 'HwndWrapper') $true

    & $expect 'ghost reason for the hung shell' (Get-NotForegroundReason -Target 'shell-hung' -ForegroundProcessName 'dwm') 'target not foreground (DWM ghost over the shell)'
    & $expect 'ghost reason for the shell on top' (Get-NotForegroundReason -Target 'shell-top' -ForegroundProcessName 'dwm') 'target not foreground (DWM ghost over the shell)'
    & $expect 'plain reason for edge' (Get-NotForegroundReason -Target 'edge' -ForegroundProcessName 'dwm') 'target not foreground'
    & $expect 'plain reason for another foreground process' (Get-NotForegroundReason -Target 'shell-hung' -ForegroundProcessName 'msedge') 'target not foreground'

    # Edge target (3.13): any titled Edge window, not only "Netflix"; tooltips, other processes and untitled windows are skipped.
    $edgeWindows = @(
        [pscustomobject]@{ Handle = [IntPtr]777; ProcessId = 4242; ProcessName = 'msedge'; Class = 'Chrome_Tooltip_Window'; Title = 'tip' }
        [pscustomobject]@{ Handle = [IntPtr]888; ProcessId = 5353; ProcessName = 'notepad'; Class = 'Chrome_WidgetWin_1'; Title = 'Other app' }
        [pscustomobject]@{ Handle = [IntPtr]999; ProcessId = 4242; ProcessName = 'msedge'; Class = 'Chrome_WidgetWin_1'; Title = '' }
        [pscustomobject]@{ Handle = [IntPtr]1111; ProcessId = 4242; ProcessName = 'msedge'; Class = 'Chrome_WidgetWin_1'; Title = 'Nowa karta - Microsoft Edge' }
        [pscustomobject]@{ Handle = [IntPtr]2222; ProcessId = 4242; ProcessName = 'msedge'; Class = 'Chrome_WidgetWin_1'; Title = 'Netflix - Microsoft Edge' }
    )
    & $expect 'edge window without Netflix in the title is found' ((Select-EdgeWindow -Windows $edgeWindows).ToInt64()) 1111
    & $expect 'edge title pattern selects the matching window' ((Select-EdgeWindow -Windows $edgeWindows -TitlePattern 'Netflix').ToInt64()) 2222
    & $expect 'edge title pattern without a match gives no window' ((Select-EdgeWindow -Windows $edgeWindows -TitlePattern 'Disney').ToInt64()) 0
    & $expect 'no edge window gives no window' ((Select-EdgeWindow -Windows @($edgeWindows[0], $edgeWindows[1])).ToInt64()) 0

    # Order of the default targets (3.13): the hung shell is measured first (before any MinimizeAll can leave it
    # behind a DWM ghost) and the desktop last.
    if (-not $script:TargetsBound) {
        & $expect 'default targets start with the hung shell' $Targets[0] 'shell-hung'
        & $expect 'default targets end with the desktop' $Targets[$Targets.Count - 1] 'desktop'
    }
    & $expect 'dwm ghost does not pass for edge' (Test-TargetForeground -Target 'edge' -ForegroundHandle ([IntPtr]900) -TargetHandle ([IntPtr]273) -ForegroundProcessName 'dwm' -ForegroundClass 'Ghost') $false
    & $expect 'explorer WorkerW passes for desktop' (Test-TargetForeground -Target 'desktop' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'explorer' -ForegroundClass 'WorkerW') $true
    & $expect 'explorer Progman passes for desktop' (Test-TargetForeground -Target 'desktop' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'explorer' -ForegroundClass 'Progman') $true
    & $expect 'explorer window of another class does not pass for desktop' (Test-TargetForeground -Target 'desktop' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'explorer' -ForegroundClass 'CabinetWClass') $false
    & $expect 'foreign WorkerW does not pass for desktop' (Test-TargetForeground -Target 'desktop' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'wallpaperapp' -ForegroundClass 'WorkerW') $false
    & $expect 'explorer WorkerW does not pass for edge' (Test-TargetForeground -Target 'edge' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'explorer' -ForegroundClass 'WorkerW') $false

    # Target selection from `appWindowAttached` (DW-13/14/15/32): last entry, tree of `rootPid`, ownerless
    # `Chrome_WidgetWin_1`; a foreign msedge, another profile, owned windows and `Chrome_WidgetWin_0` are skipped.
    $attachedLines = @(
        '{"ts":"2026-10-27T10:00:00.000Z","level":"info","src":"host","msg":"appWindowAttached","data":{"appId":"restore-edge","hwnd":100,"rootPid":500}}'
        '{"ts":"2026-10-27T10:00:01.000Z","level":"info","src":"host","msg":"appWindowAttached","data":{"appId":"other","hwnd":150,"rootPid":900}}'
        '{"ts":"2026-10-27T10:00:02.000Z","level":"info","src":"host","msg":"appWindowAttached","data":{"appId":"restore-edge","hwnd":200,"rootPid":600}}'
    )
    $attachedEntries = @($attachedLines | ForEach-Object { $_ | ConvertFrom-Json })
    $attached = Get-AttachedTarget -Entries $attachedEntries -AppId 'restore-edge'
    & $expect 'last appWindowAttached gives hwnd' $attached.Hwnd 200
    & $expect 'last appWindowAttached gives rootPid' $attached.RootPid 600
    & $expect 'no appWindowAttached gives no target' ($null -eq (Get-AttachedTarget -Entries $attachedEntries -AppId 'missing')) $true
    & $expect 'no attached target gives no window' (Select-RestoreTargetWindow -Windows @() -Attached $null -TreePids @(600)).ToInt64() 0

    $parents = @{ 601 = 600; 602 = 601; 700 = 1; 603 = 700 }
    $tree = @(Get-ProcessTree -RootPid 600 -ParentOf $parents)
    & $expect 'process tree holds root and descendants' (($tree | Sort-Object) -join ',') '600,601,602'
    & $expect 'process tree survives a cycle' ((Get-ProcessTree -RootPid 10 -ParentOf @{ 10 = 11; 11 = 10 } | Sort-Object) -join ',') '10,11'

    $restoreWindows = @(
        [pscustomobject]@{ Handle = [IntPtr]1; ProcessId = 600; ProcessName = 'msedge'; Owner = 77; Class = 'Chrome_WidgetWin_1'; Title = 'owned popup' }
        [pscustomobject]@{ Handle = [IntPtr]2; ProcessId = 600; ProcessName = 'msedge'; Owner = 0; Class = 'Chrome_WidgetWin_0'; Title = 'helper' }
        [pscustomobject]@{ Handle = [IntPtr]3; ProcessId = 999; ProcessName = 'msedge'; Owner = 0; Class = 'Chrome_WidgetWin_1'; Title = 'other profile' }
        [pscustomobject]@{ Handle = [IntPtr]4; ProcessId = 5353; ProcessName = 'notepad'; Owner = 0; Class = 'Chrome_WidgetWin_1'; Title = 'foreign' }
        [pscustomobject]@{ Handle = [IntPtr]5; ProcessId = 602; ProcessName = 'msedge'; Owner = 0; Class = 'Chrome_WidgetWin_1'; Title = '' }
        [pscustomobject]@{ Handle = [IntPtr]6; ProcessId = 600; ProcessName = 'msedge'; Owner = 0; Class = 'Chrome_WidgetWin_1'; Title = 'second in tree' }
    )
    & $expect 'restore target: only ownerless Chrome_WidgetWin_1 of the tree, first match' (Select-RestoreTargetWindow -Windows $restoreWindows -Attached $attached -TreePids $tree).ToInt64() 5
    $loggedHwnd = [pscustomobject]@{ Hwnd = 6; RootPid = 600 }
    & $expect 'restore target prefers the logged hwnd when it qualifies' (Select-RestoreTargetWindow -Windows $restoreWindows -Attached $loggedHwnd -TreePids $tree).ToInt64() 6
    $ownedHwnd = [pscustomobject]@{ Hwnd = 1; RootPid = 600 }
    & $expect 'restore target ignores a logged hwnd that has an owner' (Select-RestoreTargetWindow -Windows $restoreWindows -Attached $ownedHwnd -TreePids $tree).ToInt64() 5
    & $expect 'restore target: only foreign and other-profile windows gives none' (Select-RestoreTargetWindow -Windows @($restoreWindows[2], $restoreWindows[3]) -Attached $attached -TreePids $tree).ToInt64() 0
    $moonWindows = @([pscustomobject]@{ Handle = [IntPtr]8; ProcessId = 600; ProcessName = 'Moonlight'; Owner = 0; Class = 'SDL_app'; Title = 'Moonlight' })
    & $expect 'moonlight target takes any class' (Select-RestoreTargetWindow -Windows $moonWindows -Attached $attached -TreePids $tree -AnyClass).ToInt64() 8
    & $expect 'browser target rejects a non-browser class' (Select-RestoreTargetWindow -Windows $moonWindows -Attached $attached -TreePids $tree).ToInt64() 0
    & $expect 'edge selector with a tree skips owners, helpers and other processes' (Select-EdgeWindow -Windows $restoreWindows -TreePids $tree).ToInt64() 5
    & $expect 'edge selector without a tree skips owned windows' (Select-EdgeWindow -Windows @($restoreWindows[0], $restoreWindows[5])).ToInt64() 6
    & $expect 'owned browser window gives no id' ((Get-BrowserWindowIds -Windows @($restoreWindows[0])) -join ',') ''

    # Restore verdict (OS state after Home).
    $okVerdict = Test-RestoreVerdict -Target 'edge' -ShellForeground $true -TargetIconic $true -TargetVisible $true
    & $expect 'restore verdict pass' ('{0}/{1}' -f $okVerdict.Ok, $okVerdict.Reason) 'True/ok'
    & $expect 'restore verdict: shell not foreground' (Test-RestoreVerdict -Target 'edge' -ShellForeground $false -TargetIconic $true -TargetVisible $true).Reason 'shell not foreground'
    & $expect 'restore verdict: target not minimized' (Test-RestoreVerdict -Target 'chrome' -ShellForeground $true -TargetIconic $false -TargetVisible $true).Reason 'target not minimized'
    & $expect 'restore verdict: target not visible' (Test-RestoreVerdict -Target 'edge' -ShellForeground $true -TargetIconic $true -TargetVisible $false).Reason 'target not visible'
    & $expect 'restore verdict: browser window' (Test-RestoreVerdict -Target 'edge' -ShellForeground $true -TargetIconic $true -TargetVisible $true -NewBrowserWindow @('600:9')).Reason 'browser window'
    & $expect 'restore verdict: desktop needs no target window' (Test-RestoreVerdict -Target 'desktop' -ShellForeground $true -TargetIconic $false -TargetVisible $false).Ok $true

    # Moonlight "n/a" (not counted) and the generated apps.json.
    $existingFile = $Fixture
    & $expect 'moonlight n/a without the variable' (Get-RestoreSkipReason -Target 'moonlight' -MoonlightExe '' -MoonlightHostName 'pc' -SunshineReachable $true) 'MEDIASTATION_HARNESS_MOONLIGHT is not set'
    & $expect 'moonlight n/a without the file' ((Get-RestoreSkipReason -Target 'moonlight' -MoonlightExe 'Z:\no\Moonlight.exe' -MoonlightHostName 'pc' -SunshineReachable $true) -like 'Moonlight.exe not found*') $true
    & $expect 'moonlight n/a without the host' (Get-RestoreSkipReason -Target 'moonlight' -MoonlightExe $existingFile -MoonlightHostName '' -SunshineReachable $true) 'no -MoonlightHost'
    & $expect 'moonlight n/a without Sunshine' (Get-RestoreSkipReason -Target 'moonlight' -MoonlightExe $existingFile -MoonlightHostName 'pc' -SunshineReachable $false) 'Sunshine port not reachable'
    & $expect 'moonlight applies with everything present' (Get-RestoreSkipReason -Target 'moonlight' -MoonlightExe $existingFile -MoonlightHostName 'pc' -SunshineReachable $true) ''
    & $expect 'edge never n/a' (Get-RestoreSkipReason -Target 'edge') ''

    $edgeJson = New-RestoreAppsJson -Target 'edge' | ConvertFrom-Json
    & $expect 'generated edge apps.json has one web tile' ('{0}/{1}/{2}/{3}' -f @($edgeJson.apps).Count, $edgeJson.apps[0].type, $edgeJson.apps[0].id, $edgeJson.apps[0].browser) '1/web/restore-edge/edge'
    $chromeJson = New-RestoreAppsJson -Target 'chrome' | ConvertFrom-Json
    & $expect 'generated chrome apps.json uses chrome' ('{0}/{1}' -f $chromeJson.apps[0].id, $chromeJson.apps[0].browser) 'restore-chrome/chrome'
    $moonJson = New-RestoreAppsJson -Target 'moonlight' -MoonlightHostName 'pc' -MoonlightAppName 'Desktop' | ConvertFrom-Json
    & $expect 'generated moonlight apps.json has host and app' ('{0}/{1}/{2}/{3}' -f @($moonJson.apps).Count, $moonJson.apps[0].type, $moonJson.apps[0].host, $moonJson.apps[0].app) '1/moonlight/pc/Desktop'
    $desktopJson = New-RestoreAppsJson -Target 'desktop' | ConvertFrom-Json
    & $expect 'generated desktop apps.json uses an edge tile' ('{0}/{1}' -f $desktopJson.apps[0].id, $desktopJson.apps[0].browser) 'restore-desktop/edge'

    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "FAIL: $_" }
        Write-Host 'Self-test FAILED.'
        return 1
    }

    Write-Host 'Self-test passed.'
    return 0
}

# ---------------------------------------------------------------------------
# Live run helpers
# ---------------------------------------------------------------------------

function Note {
    param([string]$Message)

    $line = '{0} {1}' -f [DateTimeOffset]::UtcNow.ToString('HH:mm:ss.fff', [System.Globalization.CultureInfo]::InvariantCulture), $Message
    Add-Content -LiteralPath $Out -Value $line -Encoding UTF8
    Write-Host $line
}

function Wait-For {
    param(
        [scriptblock]$Condition,
        [string]$What,
        [int]$Seconds = 10
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Condition) {
            return $true
        }

        Start-Sleep -Milliseconds 200
    }

    Write-Host "timeout: $What"
    return $false
}

function Set-FrontWindow {
    param([IntPtr]$Handle)

    if ($Handle -eq [IntPtr]::Zero) {
        return $false
    }

    if ([MediaStationHomeS10.Native]::IsIconic($Handle)) {
        [void][MediaStationHomeS10.Native]::ShowWindow($Handle, 9)
    }

    # Alt unlock: Windows lets a process take focus right after it received an Alt press.
    [MediaStationHomeS10.Native]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [MediaStationHomeS10.Native]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    return [MediaStationHomeS10.Native]::SetForegroundWindow($Handle)
}

function Get-WindowTitle {
    param([IntPtr]$Handle)

    if ($Handle -eq [IntPtr]::Zero) {
        return ''
    }

    $buffer = New-Object System.Text.StringBuilder 512
    [void][MediaStationHomeS10.Native]::GetWindowTextW($Handle, $buffer, $buffer.Capacity)
    return $buffer.ToString()
}

function Get-WindowClass {
    param([IntPtr]$Handle)

    if ($Handle -eq [IntPtr]::Zero) {
        return ''
    }

    $buffer = New-Object System.Text.StringBuilder 256
    [void][MediaStationHomeS10.Native]::GetClassNameW($Handle, $buffer, $buffer.Capacity)
    return $buffer.ToString()
}

function Get-ProcessName {
    param([int]$ProcessId)

    if ($ProcessId -eq 0) {
        return ''
    }

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        return $process.ProcessName
    }

    return ''
}

function Get-TopLevelWindows {
    # EnumWindows gives every visible top-level window (including tooltips and popups), not one per process, so a
    # second browser window in an existing process is visible (DW-13). Process names are cached per PID.
    $handles = New-Object 'System.Collections.Generic.List[IntPtr]'
    [void][MediaStationHomeS10.Native]::EnumWindows({
            param([IntPtr]$handle, [IntPtr]$state)

            if ([MediaStationHomeS10.Native]::IsWindowVisible($handle)) {
                $handles.Add($handle)
            }

            return $true
        }, [IntPtr]::Zero)

    $names = @{}
    $windows = @()
    foreach ($handle in $handles) {
        $processId = Get-WindowProcessId -Handle $handle
        if ($processId -eq 0) {
            continue
        }

        if (-not $names.ContainsKey($processId)) {
            $names[$processId] = Get-ProcessName -ProcessId $processId
        }

        $windows += [pscustomobject]@{
            Handle      = $handle
            ProcessId   = $processId
            ProcessName = $names[$processId]
            Owner       = [MediaStationHomeS10.Native]::GetWindow($handle, 4).ToInt64()
            Class       = Get-WindowClass -Handle $handle
            Title       = Get-WindowTitle -Handle $handle
        }
    }

    return $windows
}

function Send-KeyPress {
    param(
        [int]$VirtualKey,
        [bool]$Extended = $false
    )

    # SendInput without a marker: down, short hold, up. Home is an extended key (VK_HOME 0x24).
    [void][MediaStationHomeS10.Native]::SendKey([uint16]$VirtualKey, $Extended, $false)
    Start-Sleep -Milliseconds 60
    [void][MediaStationHomeS10.Native]::SendKey([uint16]$VirtualKey, $Extended, $true)
}

function Get-BrowserWindows {
    # The live wrapper over the clean decision: one `pid:handle` per real browser window.
    return @(Get-BrowserWindowIds -Windows @(Get-TopLevelWindows))
}

function Get-WindowProcessId {
    param([IntPtr]$Handle)

    if ($Handle -eq [IntPtr]::Zero) {
        return 0
    }

    [uint32]$windowProcessId = 0
    [void][MediaStationHomeS10.Native]::GetWindowThreadProcessId($Handle, [ref]$windowProcessId)
    return [int]$windowProcessId
}

function Find-WindowProcess {
    param(
        [string]$Name = '',
        [int]$ProcessId = 0,
        [string]$TitlePattern = ''
    )

    # The same top-level enumeration as the browser-window set: the result is a real top-level handle, so the
    # foreground check can compare handles instead of processes (DW-14).
    foreach ($window in @(Get-TopLevelWindows)) {
        if ($ProcessId -ne 0) {
            if ($window.ProcessId -ne $ProcessId) {
                continue
            }
        }
        elseif ($Name.Length -gt 0) {
            if ($window.ProcessName -ne $Name) {
                continue
            }
        }
        else {
            continue
        }

        if ($TitlePattern.Length -gt 0 -and $window.Title -notmatch $TitlePattern) {
            continue
        }

        return [IntPtr]$window.Handle
    }

    return [IntPtr]::Zero
}

function TargetWindow {
    param(
        [string]$Target,
        [int]$ProcessId = 0
    )

    switch ($Target) {
        'desktop' {
            # Desktop: minimise every window; Progman is then the target window.
            $shell = New-Object -ComObject Shell.Application
            try {
                $shell.MinimizeAll()
            }
            finally {
                [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
            }

            Start-Sleep -Milliseconds 800
            return [MediaStationHomeS10.Native]::FindWindow('Progman', [NullString]::Value)
        }
        'edge' {
            return Select-EdgeWindow -Windows @(Get-TopLevelWindows) -TitlePattern $EdgeTitle
        }
        'moonlight' {
            return Find-WindowProcess -Name 'Moonlight'
        }
        'shell-hung' {
            if ($ProcessId -eq 0) {
                return [IntPtr]::Zero
            }

            return Find-WindowProcess -ProcessId $ProcessId
        }
        default {
            return [IntPtr]::Zero
        }
    }
}

function Initialize-Targets {
    # Edge is prepared here (a new `about:blank` window when none is open); Moonlight cannot be started
    # reliably, so the person opens it borderless before the run (3.13). Both happen before the first press, so
    # the window the script opens is part of the baseline and never counts as a "new browser window".
    $desktopAt = [array]::IndexOf($Targets, 'desktop')
    $shellAt = [array]::IndexOf($Targets, 'shell-hung')
    if ($desktopAt -ge 0 -and $shellAt -ge 0 -and $desktopAt -lt $shellAt) {
        Note 'WARN: desktop runs before shell-hung; MinimizeAll leaves the hung shell behind a DWM ghost and its presses fail'
    }

    if ($Targets -contains 'edge' -and (Select-EdgeWindow -Windows @(Get-TopLevelWindows) -TitlePattern $EdgeTitle) -eq [IntPtr]::Zero) {
        Note 'edge: no Edge window; opening about:blank'
        try {
            Start-Process -FilePath 'msedge' -ArgumentList '--new-window', 'about:blank' | Out-Null
        }
        catch {
            Note ('WARN: cannot start Edge: {0}' -f $_.Exception.Message)
        }

        if (Wait-For -What 'Edge window' -Seconds 20 -Condition {
                (Select-EdgeWindow -Windows @(Get-TopLevelWindows) -TitlePattern $EdgeTitle) -ne [IntPtr]::Zero
            }) {
            # Session restore may add windows right after the first one; they belong to the baseline, not to a press.
            Start-Sleep -Seconds 2
        }
        else {
            Note 'WARN: no Edge window appeared; the edge presses will fail (open Edge by hand or use -EdgeTitle)'
        }
    }

    if ($Targets -contains 'moonlight' -and (Find-WindowProcess -Name 'Moonlight') -eq [IntPtr]::Zero) {
        Note 'WARN: no Moonlight window; open Moonlight (borderless) before the run, otherwise the moonlight presses fail'
    }
}

function Measure-Press {
    param(
        [string]$Target,
        [int]$Index,
        [IntPtr]$Handle,
        [DateTimeOffset]$HangExpires,
        [bool]$RequiresHung
    )

    if ($Handle -eq [IntPtr]::Zero) {
        Note ('  {0} #{1}: FAIL (no target window)' -f $Target, $Index)
        return [pscustomobject]@{ Ok = $false; Count = 0; Reason = 'no target window' }
    }

    if ($RequiresHung -and [DateTimeOffset]::UtcNow -ge $HangExpires) {
        Note ('  {0} #{1}: FAIL (hang expired; increase -HangSeconds)' -f $Target, $Index)
        return [pscustomobject]@{ Ok = $false; Count = 0; Reason = 'hang expired (increase -HangSeconds)' }
    }

    $front = Set-FrontWindow -Handle $Handle
    Start-Sleep -Milliseconds 700
    $foreground = [MediaStationHomeS10.Native]::GetForegroundWindow()
    $targetProcessId = Get-WindowProcessId -Handle $Handle
    $foregroundProcessId = Get-WindowProcessId -Handle $foreground
    $foregroundProcess = if ($foregroundProcessId -ne 0) { Get-Process -Id $foregroundProcessId -ErrorAction SilentlyContinue } else { $null }
    $foregroundProcessName = if ($null -ne $foregroundProcess) { $foregroundProcess.ProcessName } else { '' }
    $foregroundClass = Get-WindowClass -Handle $foreground

    # Exact handle, not the process: another window of the same process must not pass (DW-14). The shell exceptions
    # (DWM ghost of a hung shell, explorer's desktop window) live in Test-TargetForeground.
    $targetIsForeground = Test-TargetForeground -Target $Target -ForegroundHandle $foreground -TargetHandle $Handle `
        -ForegroundProcessName $foregroundProcessName -ForegroundClass $foregroundClass

    Note ('  {0} #{1}: foreground 0x{2:X} pid={3} class={4} (target 0x{5:X} pid={6}, SetForegroundWindow={7})' -f
        $Target, $Index, $foreground.ToInt64(), $foregroundProcessId, $foregroundClass, $Handle.ToInt64(), $targetProcessId, $front)

    # Focus is advisory: a press delivered to another window must not count as a pass for the target.
    if (-not $targetIsForeground) {
        $reason = Get-NotForegroundReason -Target $Target -ForegroundProcessName $foregroundProcessName
        Note ('  {0} #{1}: FAIL ({2})' -f $Target, $Index, $reason)
        return [pscustomobject]@{ Ok = $false; Count = 0; Reason = $reason }
    }

    $before = @(Get-BrowserWindows)
    $from = [DateTimeOffset]::UtcNow

    if ($InjectHome) {
        Note ('  {0} #{1}: injected Home' -f $Target, $Index)
        Send-KeyPress -VirtualKey 0x24 -Extended $true
    }
    else {
        Note ('  {0} #{1}: beep' -f $Target, $Index)
        [Console]::Beep(880, 250)
    }

    # Wait for the first `action home` inside the window; no action in -PressTimeoutSeconds is a failure.
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($PressTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $homes = @(Get-HomeActions -Entries @(Get-RunEntries -ProcessId $script:AppProcessId))
        if ((Get-PressCount -Homes $homes -From $from -To ([DateTimeOffset]::MaxValue)) -ge 1) {
            break
        }

        Start-Sleep -Milliseconds 200
    }

    # Settle: a double `action home` must have a chance to land inside the measurement window.
    Start-Sleep -Seconds $SettleSeconds
    $to = [DateTimeOffset]::UtcNow

    $after = @(Get-BrowserWindows)
    $newBrowserWindows = @(Get-NewWindowIds -Before $before -After $after)
    $homes = @(Get-HomeActions -Entries @(Get-RunEntries -ProcessId $script:AppProcessId))
    $result = Test-Press -Homes $homes -From $from -To $to -NewBrowserWindow $newBrowserWindows

    $detail = $result.Reason
    if ($newBrowserWindows.Count -gt 0 -and $result.Reason -eq 'browser window') {
        $detail = 'browser window {0}' -f ($newBrowserWindows -join ',')
    }

    if ($RequiresHung -and $to -ge $HangExpires) {
        $result = [pscustomobject]@{ Ok = $false; Count = $result.Count; Reason = 'hang expired (increase -HangSeconds)' }
        $detail = $result.Reason
    }

    $verdict = 'PASS'
    if (-not $result.Ok) {
        $verdict = 'FAIL'
    }

    Note ('  {0} #{1}: {2} count={3} ({4})' -f $Target, $Index, $verdict, $result.Count, $detail)
    return $result
}

# ---------------------------------------------------------------------------
# Shell-restore pass (-Restore)
# ---------------------------------------------------------------------------

function Get-ConfigDirectory {
    return (Join-Path $env:LOCALAPPDATA 'MediaStation\config')
}

function Get-ParentMap {
    $map = @{}
    foreach ($process in @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue)) {
        $map[[int]$process.ProcessId] = [int]$process.ParentProcessId
    }

    return $map
}

function Test-TcpPort {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutMilliseconds = 2000
    )

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync($HostName, $Port)
        return ($task.Wait($TimeoutMilliseconds) -and $client.Connected)
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Restore-AppsJson {
    # Puts the real apps.json back from the copy of this (or an interrupted earlier) run.
    $appsPath = Join-Path (Get-ConfigDirectory) 'apps.json'
    $backupPath = $appsPath + '.restore-bak'
    if (Test-Path -LiteralPath $backupPath) {
        Move-Item -LiteralPath $backupPath -Destination $appsPath -Force
        return $true
    }

    return $false
}

function Stop-RestoreProcesses {
    param([int[]]$RootPids = @())

    # Only what the run started: process trees of the logged roots and browsers with a `profiles\restore-*` profile.
    # taskkill reports an already gone child on stderr; with $ErrorActionPreference Stop that would abort the run.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        foreach ($rootPid in $RootPids) {
            if ($rootPid -gt 0) {
                & taskkill.exe /PID $rootPid /T /F 2>&1 | Out-Null
            }
        }

        foreach ($process in @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue |
                Where-Object { $_.CommandLine -like '*\profiles\restore-*' })) {
            & taskkill.exe /PID ([int]$process.ProcessId) /T /F 2>&1 | Out-Null
        }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Get-RestoreEvidence {
    param(
        [object[]]$Entries,
        [DateTimeOffset]$From
    )

    # `windowCommand` entries since `From` (extra evidence only; the verdict is the OS window state).
    $lines = @()
    foreach ($entry in $Entries) {
        if ($entry.msg -ne 'windowCommand') {
            continue
        }

        $ts = ConvertTo-Utc ([string]$entry.ts)
        if ($null -ne $ts -and $ts -ge $From) {
            $lines += ('{0}:{1}' -f $entry.data.type, $entry.data.result)
        }
    }

    return $lines
}

function Invoke-RestoreTarget {
    param([string]$Target)

    $result = [pscustomobject]@{ Target = $Target; Skipped = $false; Reason = ''; Passed = 0; Total = 0; Problems = @() }
    $moonlightExe = [string]$env:MEDIASTATION_HARNESS_MOONLIGHT
    if ($Target -eq 'moonlight') {
        $reachable = $false
        if (-not [string]::IsNullOrWhiteSpace($MoonlightHost)) {
            $reachable = Test-TcpPort -HostName $MoonlightHost -Port $SunshinePort
        }

        $skip = Get-RestoreSkipReason -Target $Target -MoonlightExe $moonlightExe -MoonlightHostName $MoonlightHost -SunshineReachable $reachable
        if ($skip.Length -gt 0) {
            $result.Skipped = $true
            $result.Reason = $skip
            Note ('== {0}: n/a ({1})' -f $Target, $skip)
            return $result
        }
    }

    $appId = "restore-$Target"
    $configDir = Get-ConfigDirectory
    $appsPath = Join-Path $configDir 'apps.json'
    $backupPath = $appsPath + '.restore-bak'
    $app = $null
    $rootPid = 0
    try {
        Stop-RestoreProcesses
        Copy-Item -LiteralPath $appsPath -Destination $backupPath -Force
        Set-Content -LiteralPath $appsPath -Value (New-RestoreAppsJson -Target $Target -MoonlightHostName $MoonlightHost -MoonlightAppName $MoonlightApp) -Encoding UTF8

        $env:MEDIASTATION_DEV_HARNESS = '1'
        $app = Start-Process -FilePath $MediaStation -PassThru
        $script:AppProcessId = $app.Id
        Note ('== {0}: pid {1}, MEDIASTATION_DEV_HARNESS=1, tile {2}' -f $Target, $app.Id, $appId)

        if (-not (Wait-For -What 'MediaStation window' -Seconds 30 -Condition { $app.Refresh(); $app.MainWindowHandle -ne 0 })) {
            throw 'MediaStation window did not appear within 30s.'
        }

        if (-not (Wait-For -What 'snapshotPublished' -Seconds 30 -Condition {
                    @(Get-RunEntries -ProcessId $app.Id | Where-Object { $_.msg -eq 'snapshotPublished' }).Count -ge 1
                })) {
            throw 'No snapshotPublished within 30s.'
        }

        $shell = [IntPtr]$app.MainWindowHandle
        $managed = @(Get-RunEntries -ProcessId $app.Id | Where-Object { $_.msg -eq 'managedShell' }) | Select-Object -Last 1
        if ($null -ne $managed) {
            $shell = [IntPtr]([int64]$managed.data.hwnd)
        }

        # OK on the only tile (the focus starts on the first tile).
        Start-Sleep -Seconds 2
        [void](Set-FrontWindow -Handle $shell)
        Start-Sleep -Milliseconds 800
        Note ('  {0}: injected OK' -f $Target)
        Send-KeyPress -VirtualKey 0x0D

        if (-not (Wait-For -What 'appWindowAttached' -Seconds 40 -Condition {
                    $null -ne (Get-AttachedTarget -Entries @(Get-RunEntries -ProcessId $app.Id) -AppId $appId)
                })) {
            throw "No appWindowAttached for $appId within 40s (OK did not start the tile)."
        }

        $attached = Get-AttachedTarget -Entries @(Get-RunEntries -ProcessId $app.Id) -AppId $appId
        $rootPid = $attached.RootPid
        Note ('  {0}: appWindowAttached hwnd=0x{1:X} rootPid={2}' -f $Target, $attached.Hwnd, $attached.RootPid)
        Start-Sleep -Seconds 2

        for ($index = 1; $index -le $Presses; $index++) {
            $result.Total++
            $label = '  {0} #{1}' -f $Target, $index

            if ($index -gt 1) {
                # Back into the app through the product: after Home the Orchestrator is in shell mode, so only OK on
                # the tile resumes the app (an external SetForegroundWindow would leave Home unhandled).
                [void](Set-FrontWindow -Handle $shell)
                Start-Sleep -Milliseconds 800
                Send-KeyPress -VirtualKey 0x0D
                Start-Sleep -Seconds 2
            }

            $handle = [IntPtr]::Zero
            if ($Target -eq 'desktop') {
                $handle = TargetWindow -Target 'desktop'
                # MinimizeAll skips borderless/kiosk windows (no minimize box): minimise the app window itself too.
                $appTree = Get-ProcessTree -RootPid $attached.RootPid -ParentOf (Get-ParentMap)
                $appWindow = Select-RestoreTargetWindow -Windows @(Get-TopLevelWindows) -Attached $attached -TreePids $appTree
                if ($appWindow -ne [IntPtr]::Zero -and -not [MediaStationHomeS10.Native]::IsIconic($appWindow)) {
                    [void][MediaStationHomeS10.Native]::ShowWindow($appWindow, 6)
                    Start-Sleep -Milliseconds 800
                }
            }
            else {
                $tree = Get-ProcessTree -RootPid $attached.RootPid -ParentOf (Get-ParentMap)
                $handle = Select-RestoreTargetWindow -Windows @(Get-TopLevelWindows) -Attached $attached -TreePids $tree -AnyClass:($Target -eq 'moonlight')
                if ($handle -ne [IntPtr]::Zero) {
                    [void](Set-FrontWindow -Handle $handle)
                }
            }

            if ($handle -eq [IntPtr]::Zero) {
                Note ('{0}: FAIL (no target window)' -f $label)
                $result.Problems += ('{0} press {1}: no target window' -f $Target, $index)
                continue
            }

            Start-Sleep -Milliseconds 1200
            $foreground = [MediaStationHomeS10.Native]::GetForegroundWindow()
            $foregroundProcessId = Get-WindowProcessId -Handle $foreground
            $foregroundClass = Get-WindowClass -Handle $foreground
            $targetOnTop = Test-TargetForeground -Target $Target -ForegroundHandle $foreground -TargetHandle $handle `
                -ForegroundProcessName (Get-ProcessName -ProcessId $foregroundProcessId) -ForegroundClass $foregroundClass
            # Desktop: no target-window condition (the minimised app window may keep the foreground handle, which is
            # exactly what the product sees); the verdict below is the shell on top and no new browser window.
            if (-not $targetOnTop -and $Target -ne 'desktop') {
                Note ('{0}: FAIL (target not foreground: 0x{1:X} class={2})' -f $label, $foreground.ToInt64(), $foregroundClass)
                $result.Problems += ('{0} press {1}: target not foreground' -f $Target, $index)
                continue
            }

            $before = @(Get-BrowserWindows)
            $from = [DateTimeOffset]::UtcNow
            Note ('{0}: injected Home (target 0x{1:X})' -f $label, $handle.ToInt64())
            Send-KeyPress -VirtualKey 0x24 -Extended $true

            # Poll the OS state until it is a pass (then settle and re-check for late windows) or the time is up.
            $verdict = $null
            $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
            do {
                Start-Sleep -Milliseconds 250
                $verdict = Test-RestoreVerdict -Target $Target `
                    -ShellForeground ([MediaStationHomeS10.Native]::GetForegroundWindow() -eq $shell) `
                    -TargetIconic ([MediaStationHomeS10.Native]::IsIconic($handle)) `
                    -TargetVisible ([MediaStationHomeS10.Native]::IsWindowVisible($handle)) `
                    -NewBrowserWindow @(Get-NewWindowIds -Before $before -After @(Get-BrowserWindows))
            } while (-not $verdict.Ok -and [DateTimeOffset]::UtcNow -lt $deadline)

            if ($verdict.Ok) {
                Start-Sleep -Seconds $SettleSeconds
                $verdict = Test-RestoreVerdict -Target $Target `
                    -ShellForeground ([MediaStationHomeS10.Native]::GetForegroundWindow() -eq $shell) `
                    -TargetIconic ([MediaStationHomeS10.Native]::IsIconic($handle)) `
                    -TargetVisible ([MediaStationHomeS10.Native]::IsWindowVisible($handle)) `
                    -NewBrowserWindow @(Get-NewWindowIds -Before $before -After @(Get-BrowserWindows))
            }

            $evidence = @(Get-RestoreEvidence -Entries @(Get-RunEntries -ProcessId $app.Id) -From $from)
            if ($verdict.Ok) {
                $result.Passed++
                Note ('{0}: PASS (windowCommand: {1})' -f $label, ($evidence -join ', '))
            }
            else {
                $result.Problems += ('{0} press {1}: {2}' -f $Target, $index, $verdict.Reason)
                Note ('{0}: FAIL ({1}; windowCommand: {2})' -f $label, $verdict.Reason, ($evidence -join ', '))
            }
        }
    }
    catch {
        $result.Problems += ('{0}: run aborted: {1}' -f $Target, $_.Exception.Message)
        Note ('  {0}: ABORTED {1}' -f $Target, $_.Exception.Message)
    }
    finally {
        Remove-Item Env:\MEDIASTATION_DEV_HARNESS -ErrorAction SilentlyContinue
        if ($null -ne $app) {
            try {
                [void]$app.CloseMainWindow()
                if (-not $app.WaitForExit(8000)) {
                    Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
                    [void]$app.WaitForExit(5000)
                }
            }
            catch {
                Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
            }
        }

        Stop-RestoreProcesses -RootPids @($rootPid)
        [void](Restore-AppsJson)
    }

    return $result
}

function Invoke-RestoreRun {
    $restoreTargets = $Targets
    if (-not $script:TargetsBound) {
        $restoreTargets = @('edge', 'chrome', 'moonlight', 'desktop')
    }

    New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null
    Set-Content -LiteralPath $Out -Value '' -Encoding UTF8
    Note ('S10 restore: targets={0}; presses={1}; injectHome={2}; exe={3}' -f ($restoreTargets -join ','), $Presses, $InjectHome.IsPresent, $MediaStation)

    $problems = New-Object System.Collections.Generic.List[string]
    $summary = @()
    try {
        if (-not (Test-Path -LiteralPath $MediaStation)) {
            throw "MediaStation.exe not found: $MediaStation - build/publish Release first."
        }

        if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) {
            throw 'A MediaStation instance is already running; close it and retry.'
        }

        if (Restore-AppsJson) {
            Note 'WARN: put back apps.json from an interrupted earlier run'
        }

        foreach ($target in $restoreTargets) {
            $summary += Invoke-RestoreTarget -Target $target
        }
    }
    catch {
        $problems.Add(('run aborted: {0}' -f $_.Exception.Message))
    }
    finally {
        [void](Restore-AppsJson)
    }

    Note ''
    foreach ($item in $summary) {
        if ($item.Skipped) {
            Note ('{0}: n/a ({1})' -f $item.Target, $item.Reason)
            continue
        }

        Note ('{0}: {1}/{2}' -f $item.Target, $item.Passed, $Presses)
        foreach ($problem in $item.Problems) {
            $problems.Add($problem)
        }

        if ($item.Passed -ne $Presses -and $item.Problems.Count -eq 0) {
            $problems.Add(('{0}: {1}/{2}' -f $item.Target, $item.Passed, $Presses))
        }
    }

    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "FAIL: $_" }
        Note ('FAILED: {0} problem(s)' -f $problems.Count)
        return 1
    }

    Note 'OK'
    return 0
}

# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

if ($SelfTest) {
    exit (Invoke-SelfTest -FixturePath $Fixture)
}

if (-not ('MediaStationHomeS10.Native' -as [type])) {
    Add-Type -Namespace MediaStationHomeS10 -Name Native -MemberDefinition @'
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
[StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
[StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
[StructLayout(LayoutKind.Explicit)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }
[DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
// One key transition through SendInput, no dwExtraInfo marker (the product treats it as a real keyboard press).
public static uint SendKey(ushort vk, bool extended, bool up)
{
    INPUT[] input = new INPUT[1];
    input[0].type = 1;
    input[0].ki.wVk = vk;
    input[0].ki.dwFlags = (extended ? 1u : 0u) | (up ? 2u : 0u);
    return SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
}
'@
}

if ($Restore) {
    exit (Invoke-RestoreRun)
}

$problems = New-Object System.Collections.Generic.List[string]
$total = 0
$passed = 0
$shellTopPassed = 0
$expectedTotal = @($Targets).Count * $Presses
$app = $null

New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null
Set-Content -LiteralPath $Out -Value '' -Encoding UTF8
Note ('S10 check: targets={0}; presses={1}; hang={2}s; exe={3}' -f ($Targets -join ','), $Presses, $HangSeconds, $MediaStation)

try {
    if (-not (Test-Path -LiteralPath $MediaStation)) {
        throw "MediaStation.exe not found: $MediaStation - build/publish Release first."
    }

    Initialize-Targets

    # ---- Pass 1: hung shell ---------------------------------------------------------------
    $env:MEDIASTATION_DEBUG_HANG_UI = [string]$HangSeconds
    $app = Start-Process -FilePath $MediaStation -PassThru
    $script:AppProcessId = $app.Id
    Note ('== pass 1: hung shell, pid {0}, MEDIASTATION_DEBUG_HANG_UI={1}' -f $app.Id, $HangSeconds)

    if (-not (Wait-For -What 'MediaStation window' -Seconds 30 -Condition {
                $app.Refresh()
                $app.MainWindowHandle -ne 0
            })) {
        $rejected = @(Get-RunEntries -ProcessId $app.Id | Where-Object { $_.msg -eq 'instanceRejected' }) | Select-Object -First 1
        if ($null -ne $rejected) {
            throw 'MediaStation refused to start (instanceRejected); close the running instance and retry.'
        }

        throw 'MediaStation window did not appear within 30s.'
    }

    if (-not (Wait-For -What 'debugHangUi entry' -Seconds 30 -Condition {
                @(Get-RunEntries -ProcessId $app.Id | Where-Object { $_.msg -eq 'debugHangUi' }).Count -ge 1
            })) {
        throw 'No debugHangUi entry within 30s - publish a Release build with the debug hang code and retry.'
    }

    $hangEntry = @(Get-RunEntries -ProcessId $app.Id | Where-Object { $_.msg -eq 'debugHangUi' }) | Select-Object -Last 1
    $hangSeconds = [double]$hangEntry.data.seconds
    $hangExpires = (ConvertTo-Utc ([string]$hangEntry.ts)).AddSeconds($hangSeconds)
    Note ('hang entry: {0}s, expires {1}' -f $hangSeconds, $hangExpires.ToString('HH:mm:ss.fff'))
    Note 'pass 1: every press must end before the hang expires, otherwise it fails with "hang expired"'
    foreach ($target in $Targets) {
        for ($index = 1; $index -le $Presses; $index++) {
            $handle = TargetWindow -Target $target -ProcessId $app.Id
            $result = Measure-Press -Target $target -Index $index -Handle $handle -HangExpires $hangExpires -RequiresHung $true
            $total++
            if ($result.Ok) {
                $passed++
            }
            else {
                $problems.Add(('{0} press {1}: {2}' -f $target, $index, $result.Reason))
            }
        }

        Note ('  {0}: done' -f $target)
    }

    # A hung shell does not process WM_CLOSE, so the instance is force-stopped after the pass.
    Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    $app.WaitForExit(5000) | Out-Null
    $app = $null
    Note 'pass 1: hung instance stopped'

    # ---- Pass 2: shell on top, not hung ---------------------------------------------------
    if (-not $SkipShellTopPass) {
        Remove-Item Env:\MEDIASTATION_DEBUG_HANG_UI -ErrorAction SilentlyContinue
        $app = Start-Process -FilePath $MediaStation -PassThru
        $script:AppProcessId = $app.Id
        Note ('== pass 2: shell on top, pid {0}' -f $app.Id)

        if (-not (Wait-For -What 'MediaStation window' -Seconds 30 -Condition {
                    $app.Refresh()
                    $app.MainWindowHandle -ne 0
                })) {
            throw 'MediaStation window did not appear within 30s in pass 2.'
        }

        if (-not (Wait-For -What 'snapshotPublished' -Seconds 30 -Condition {
                    @(Get-RunEntries -ProcessId $app.Id | Where-Object { $_.msg -eq 'snapshotPublished' }).Count -ge 1
                })) {
            throw 'No snapshotPublished within 30s in pass 2.'
        }

        for ($index = 1; $index -le $Presses; $index++) {
            $handle = TargetWindow -Target 'shell-hung' -ProcessId $app.Id
            $result = Measure-Press -Target 'shell-top' -Index $index -Handle $handle -HangExpires ([DateTimeOffset]::MaxValue) -RequiresHung $false
            if ($result.Ok) {
                $shellTopPassed++
            }
            else {
                $problems.Add(('shell-top press {0}: {1}' -f $index, $result.Reason))
            }
        }

        $app.CloseMainWindow() | Out-Null
        if (-not $app.WaitForExit(10000)) {
            Note 'WARN: pass 2 did not close gracefully - killing the process'
            Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
            $app.WaitForExit(5000) | Out-Null
            $problems.Add('shell-top: application did not close gracefully')
        }

        $stop = @(Get-RunEntries -ProcessId $app.Id | Where-Object { $_.msg -eq 'stop' }) | Select-Object -Last 1
        if ($null -eq $stop) {
            $problems.Add('shell-top: no stop entry')
        }
        elseif ($stop.data.exitCode -ne 0) {
            $problems.Add(('shell-top: stop exitCode {0}' -f $stop.data.exitCode))
        }
        else {
            Note 'pass 2: stop exitCode 0'
        }

        $app = $null
    }
}
catch {
    $problems.Add(('run aborted: {0}' -f $_.Exception.Message))
}
finally {
    Remove-Item Env:\MEDIASTATION_DEBUG_HANG_UI -ErrorAction SilentlyContinue
    if ($null -ne $app) {
        Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
        $app = $null
    }
}

Note ''
Note ('shell hung: {0}/{1}' -f $passed, $expectedTotal)
if (-not $SkipShellTopPass) {
    Note ('shell on top: {0}/{1}' -f $shellTopPassed, $Presses)
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "FAIL: $_" }
    Note ('FAILED: {0} problem(s)' -f $problems.Count)
    exit 1
}

if ($passed -ne $expectedTotal) {
    Note ('FAILED: shell hung {0}/{1}' -f $passed, $expectedTotal)
    exit 1
}

if (-not $SkipShellTopPass -and $shellTopPassed -ne $Presses) {
    Note ('FAILED: shell on top {0}/{1}' -f $shellTopPassed, $Presses)
    exit 1
}

Note 'OK'
exit 0
