<#
.SYNOPSIS
    Injects a synthetic WM_POWERBROADCAST/PBT_APMRESUMEAUTOMATIC into a running MediaStation instance,
    appends a powerSignalInjected marker to the log and asserts the wake trace.

.DESCRIPTION
    The evidence produced by this ticket is synthetic: the script appends powerSignalInjected
    {source: "synthetic"} to the log so it cannot be mistaken for a real wake, and it requires every
    marker to be immediately followed by its own powerSignal. Scenario: two signals within 1 s, then a
    third one after >= 6 s, so only the second candidate falls into the deduplication window. The 5 s
    boundary itself (4.999 s vs 5 s) cannot be measured through PostMessage - that boundary is covered
    by the WakeSignalGateTests unit tests.

    Assertions only cover entries produced by the injected signals (source PBT_APMRESUMEAUTOMATIC);
    powerSignal/systemResumed entries from other sources (e.g. PowerModeChanged) are reported
    separately and never counted as proof. Expected trace: 3 x powerSignal, 2 x systemResumed
    (src `system`) and 3 x snapshotPublished (start + 2 wakes) with seq 1, 2, 3. The script always
    closes the application (also on failure) and treats a non-zero stop exit code as a failure.

    MediaStation must already be running with a valid config (%LOCALAPPDATA%\MediaStation\config);
    the script does not start it.

.EXAMPLE
    powershell -File tools/inject-power.ps1
#>
param(
    [string]$ProcessName = 'MediaStation',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [int]$TimeoutSeconds = 30,
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'

if (-not ('MediaStationPowerInject.Native' -as [type])) {
    Add-Type -Namespace MediaStationPowerInject -Name Native -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll")]
public static extern bool IsWindow(IntPtr hWnd);
'@
}

$WM_POWERBROADCAST = 0x0218
$PBT_APMRESUMEAUTOMATIC = 0x0012
$PBT_SOURCE = 'PBT_APMRESUMEAUTOMATIC'

function Wait-For {
    param(
        [scriptblock]$Condition,
        [string]$What,
        [int]$Seconds = 10
    )

    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) {
            return $true
        }

        Start-Sleep -Milliseconds 200
    }

    Write-Host "timeout: $What"
    return $false
}

function Get-CurrentLogPath {
    Join-Path $LogsDir ("mediastation-{0}.jsonl" -f [DateTime]::UtcNow.ToString('yyyy-MM-dd'))
}

function Get-LogPaths {
    # Daily rotation means a new file; paths are recomputed on every read (also across UTC midnight).
    return @(Get-ChildItem -LiteralPath $LogsDir -Filter 'mediastation-*.jsonl' -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -ExpandProperty FullName)
}

function Read-LogLines {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    # FileShare.ReadWrite: the application opens the log per write, so the handle must be shared.
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
        try {
            return @($reader.ReadToEnd() -split "`n" | Where-Object { $_.Trim().Length -gt 0 })
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Read-LogEntries {
    param([string]$Path)

    $entries = @()
    foreach ($line in (Read-LogLines $Path)) {
        try {
            $entries += ($line | ConvertFrom-Json -ErrorAction Stop)
        }
        catch {
            # Line still being written by the application: skip it and read it on the next attempt.
        }
    }

    return $entries
}

function Append-LogLine {
    param(
        [string]$Path,
        [string]$Line
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Line + "`n")
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush()
    }
    finally {
        $stream.Dispose()
    }
}

function Add-InjectionMarker {
    param(
        [int]$Index,
        [int]$AppProcessId
    )

    $line = [ordered]@{
        ts    = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
        level = 'info'
        src   = 'harness'
        msg   = 'powerSignalInjected'
        data  = [ordered]@{
            source = 'synthetic'
            index  = $Index
            pid    = $AppProcessId
        }
    } | ConvertTo-Json -Compress -Depth 5

    Append-LogLine -Path (Get-CurrentLogPath) -Line $line
}

function Get-RunEntries {
    param([int]$AppProcessId)

    # Paths are recomputed on every read: the run may cross the UTC day boundary.
    $entries = @()
    foreach ($path in (Get-LogPaths)) {
        $entries += @(Read-LogEntries $path)
    }

    # The last `start` of this process marks the beginning of the current run.
    $startIndex = -1
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $entry = $entries[$i]
        if ($entry.msg -eq 'start' -and $entry.data.pid -eq $AppProcessId) {
            $startIndex = $i
        }
    }

    if ($startIndex -lt 0) {
        return @()
    }

    return @($entries | Select-Object -Skip ($startIndex + 1))
}

function Find-App {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $found = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1

        if ($null -ne $found) {
            return $found
        }

        Start-Sleep -Milliseconds 200
    }

    return $null
}

# ---------------------------------------------------------------------------

$problems = New-Object System.Collections.Generic.List[string]
$app = $null
$injectedCount = 0

try {
    if (-not (Test-Path -LiteralPath $LogsDir)) {
        throw "Log directory not found: $LogsDir"
    }

    $app = Find-App
    if ($null -eq $app) {
        throw "Process '$ProcessName' with a window not found within ${TimeoutSeconds}s - start MediaStation.exe with a valid config and retry."
    }

    $hwnd = [IntPtr]$app.MainWindowHandle
    if (-not [MediaStationPowerInject.Native]::IsWindow($hwnd)) {
        throw "HWND $hwnd of process $($app.Id) is not a window."
    }

    Write-Host "MediaStation: pid $($app.Id), hwnd $hwnd"
    Write-Host 'Log: mediastation-*.jsonl, path recomputed on every read'

    # The first snapshot (rev 1) must exist so the counters only cover wakes.
    if (-not (Wait-For -What 'first snapshotPublished' -Seconds $TimeoutSeconds -Condition {
                @(Get-RunEntries -AppProcessId $app.Id | Where-Object { $_.msg -eq 'snapshotPublished' }).Count -ge 1
            })) {
        throw "No snapshotPublished within ${TimeoutSeconds}s - check that the shell started."
    }

    function Send-SyntheticResume {
        param([int]$Index)

        Add-InjectionMarker -Index $Index -AppProcessId $app.Id

        if (-not [MediaStationPowerInject.Native]::PostMessage($hwnd, $WM_POWERBROADCAST, [IntPtr]$PBT_APMRESUMEAUTOMATIC, [IntPtr]::Zero)) {
            throw "PostMessage(WM_POWERBROADCAST) failed (Win32: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
        }

        Write-Host "powerSignalInjected: #$Index"
    }

    # Two signals within 1 s (the second falls into the 5 s window), third after >= 6 s (outside it).
    Send-SyntheticResume -Index 1
    Start-Sleep -Milliseconds 500
    Send-SyntheticResume -Index 2

    if (-not (Wait-For -What 'two powerSignal' -Seconds 10 -Condition {
                @(Get-RunEntries -AppProcessId $app.Id | Where-Object { $_.msg -eq 'powerSignal' }).Count -ge 2
            })) {
        $problems.Add('powerSignal: two entries after the first pair did not appear')
    }

    Start-Sleep -Seconds 6
    Send-SyntheticResume -Index 3

    if (-not (Wait-For -What 'two systemResumed' -Seconds 15 -Condition {
                @(Get-RunEntries -AppProcessId $app.Id | Where-Object { $_.msg -eq 'systemResumed' -and $_.data.source -eq $PBT_SOURCE }).Count -ge 2
            })) {
        $problems.Add('systemResumed: two entries from the injected source did not appear')
    }

    # -- Assertions ---------------------------------------------------------

    $run = @(Get-RunEntries -AppProcessId $app.Id)
    $markers = @($run | Where-Object { $_.msg -eq 'powerSignalInjected' })
    $signals = @($run | Where-Object { $_.msg -eq 'powerSignal' })

    if ($markers.Count -ne 3) {
        $problems.Add("powerSignalInjected: expected 3, got $($markers.Count)")
    }

    # A marker must sit immediately before its own powerSignal; only then is the entry ours.
    $injectedSignals = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $run.Count; $i++) {
        if ($run[$i].msg -ne 'powerSignalInjected') {
            continue
        }

        $marker = $run[$i]
        $next = if ($i + 1 -lt $run.Count) { $run[$i + 1] } else { $null }

        if ($null -eq $next -or $next.msg -ne 'powerSignal') {
            $problems.Add("powerSignalInjected #$($marker.data.index): no powerSignal immediately after the marker")
            continue
        }

        if ($next.data.source -ne $PBT_SOURCE) {
            $problems.Add("powerSignalInjected #$($marker.data.index): source '$($next.data.source)' instead of '$PBT_SOURCE'")
            continue
        }

        $injectedSignals.Add($next)
        $injectedCount++
    }

    if ($injectedSignals.Count -ne 3) {
        $problems.Add("powerSignal (injected): expected 3, got $($injectedSignals.Count)")
    }

    $otherSignals = @($signals | Where-Object { $injectedSignals -notcontains $_ })
    if ($otherSignals.Count -gt 0) {
        Write-Host "INFO: powerSignal from other sources (outside the proof): $($otherSignals.Count)"
        $otherSignals | Group-Object { $_.data.source } | ForEach-Object { Write-Host "  $($_.Name): $($_.Count)" }
    }

    $resumes = @($run | Where-Object { $_.msg -eq 'systemResumed' })
    $injectedResumes = @($resumes | Where-Object { $_.data.source -eq $PBT_SOURCE })
    $otherResumes = @($resumes | Where-Object { $_.data.source -ne $PBT_SOURCE })

    if ($injectedResumes.Count -ne 2) {
        $problems.Add("systemResumed ($PBT_SOURCE): expected 2, got $($injectedResumes.Count)")
    }

    foreach ($resume in $injectedResumes) {
        if ($resume.src -ne 'system') {
            $problems.Add("systemResumed: src '$($resume.src)' instead of 'system'")
        }
    }

    if ($otherResumes.Count -gt 0) {
        Write-Host "INFO: systemResumed from other sources (outside the proof): $($otherResumes.Count)"
        $otherResumes | Group-Object { $_.data.source } | ForEach-Object { Write-Host "  $($_.Name): $($_.Count)" }
    }

    # Start (seq 1) plus two wakes: the registration echo must not produce an extra snapshot.
    $snapshots = @($run | Where-Object { $_.msg -eq 'snapshotPublished' })
    if ($snapshots.Count -ne 3) {
        $problems.Add("snapshotPublished: expected 3 (start + 2 wakes), got $($snapshots.Count)")
    }

    for ($i = 0; $i -lt $snapshots.Count; $i++) {
        $seq = $snapshots[$i].data.focusRequest.seq
        if ($seq -ne ($i + 1)) {
            $problems.Add("snapshotPublished[$i]: seq $seq instead of $($i + 1)")
        }
    }

    $errors = @($run | Where-Object { $_.level -eq 'error' })
    if ($errors.Count -ne 0) {
        $problems.Add("error: $($errors.Count) entries")
    }
}
catch {
    $problems.Add("run aborted: $($_.Exception.Message)")
}
finally {
    # Always clean up, also after timeouts and assertion failures.
    if ($null -ne $app -and -not $KeepRunning) {
        $app.CloseMainWindow() | Out-Null
        if (-not $app.WaitForExit(10000)) {
            Write-Host 'WARN: the application did not close gracefully - killing the process.'
            $app.Kill()
            $app.WaitForExit(5000) | Out-Null
        }

        $stop = @(Get-RunEntries -AppProcessId $app.Id | Where-Object { $_.msg -eq 'stop' }) | Select-Object -Last 1
        if ($null -eq $stop) {
            Write-Host 'WARN: no stop entry (process ended through Kill?).'
        }
        elseif ($stop.data.exitCode -ne 0) {
            $problems.Add("stop exitCode $($stop.data.exitCode)")
        }
        else {
            Write-Host 'stop: exitCode 0'
        }
    }

    Write-Host '--- log tail ---'
    $paths = @(Get-LogPaths)
    if ($paths.Count -gt 0) {
        @(Read-LogLines ($paths[-1])) | Select-Object -Last 10 | ForEach-Object { Write-Host $_ }
    }
    else {
        Write-Host '(no log files)'
    }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "FAIL: $_" }
    Write-Host 'Synthetic wake proof FAILED.'
    exit 1
}

Write-Host ''
Write-Host "OK: $injectedCount powerSignal, 2 systemResumed (src system, $PBT_SOURCE), 3 snapshotPublished (seq 1..3), 3 powerSignalInjected."
exit 0
