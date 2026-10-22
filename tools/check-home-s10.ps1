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
    foreground handle, not the process, so another window of the same process is not a pass; two shell
    exceptions are named: the DWM ghost window of a hung shell (`shell-hung`) and the desktop window
    (`Progman`/`WorkerW` owned by `explorer`, target `desktop`).

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
    the rules that used to need a live desktop (DW-13, DW-14) are covered before Add-Type.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1
#>
param(
    [string[]]$Targets = @('edge', 'moonlight', 'desktop', 'shell-hung'),
    [int]$Presses = 4,
    [string]$MediaStation = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [string]$Out = '',
    [int]$HangSeconds = 600,
    [int]$PressTimeoutSeconds = 45,
    [int]$SettleSeconds = 2,
    [switch]$SkipShellTopPass,
    [switch]$SelfTest,
    [string]$Fixture = ''
)

$ErrorActionPreference = 'Stop'

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

    # A shell hung for more than ~5 s is replaced on screen by a DWM ghost window owned by dwm.exe; that ghost is the
    # hung shell on top.
    if ($Target -eq 'shell-hung' -and $ForegroundProcessName -eq 'dwm') {
        return $true
    }

    # After MinimizeAll the desktop window is `Progman` (FindWindow) or `WorkerW` (GetForegroundWindow); both belong
    # to explorer. The class name is not reserved, so the process gate keeps a `WorkerW` window of another app
    # (e.g. a wallpaper app) from passing.
    if ($Target -eq 'desktop' -and $ForegroundProcessName -eq 'explorer' -and
        ($ForegroundClass -eq 'Progman' -or $ForegroundClass -eq 'WorkerW')) {
        return $true
    }

    return $false
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
    & $expect 'dwm ghost passes for a hung shell' (Test-TargetForeground -Target 'shell-hung' -ForegroundHandle ([IntPtr]900) -TargetHandle ([IntPtr]273) -ForegroundProcessName 'dwm' -ForegroundClass 'Ghost') $true
    & $expect 'dwm ghost does not pass for edge' (Test-TargetForeground -Target 'edge' -ForegroundHandle ([IntPtr]900) -TargetHandle ([IntPtr]273) -ForegroundProcessName 'dwm' -ForegroundClass 'Ghost') $false
    & $expect 'explorer WorkerW passes for desktop' (Test-TargetForeground -Target 'desktop' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'explorer' -ForegroundClass 'WorkerW') $true
    & $expect 'explorer Progman passes for desktop' (Test-TargetForeground -Target 'desktop' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'explorer' -ForegroundClass 'Progman') $true
    & $expect 'explorer window of another class does not pass for desktop' (Test-TargetForeground -Target 'desktop' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'explorer' -ForegroundClass 'CabinetWClass') $false
    & $expect 'foreign WorkerW does not pass for desktop' (Test-TargetForeground -Target 'desktop' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'wallpaperapp' -ForegroundClass 'WorkerW') $false
    & $expect 'explorer WorkerW does not pass for edge' (Test-TargetForeground -Target 'edge' -ForegroundHandle ([IntPtr]901) -TargetHandle ([IntPtr]902) -ForegroundProcessName 'explorer' -ForegroundClass 'WorkerW') $false

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
            Class       = Get-WindowClass -Handle $handle
            Title       = Get-WindowTitle -Handle $handle
        }
    }

    return $windows
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
            return Find-WindowProcess -Name 'msedge' -TitlePattern 'Netflix'
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
        Note ('  {0} #{1}: FAIL (target not foreground)' -f $Target, $Index)
        return [pscustomobject]@{ Ok = $false; Count = 0; Reason = 'target not foreground' }
    }

    $before = @(Get-BrowserWindows)
    $from = [DateTimeOffset]::UtcNow

    Note ('  {0} #{1}: beep' -f $Target, $Index)
    [Console]::Beep(880, 250)

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
'@
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
