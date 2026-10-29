<#
.SYNOPSIS
    Story 6.5 check: after a MediaStation crash BrowserHost adopts the live Edge (ProbeExisting) and a synthetic wake
    renews its CDP session (RenewSession), live on this machine.

.DESCRIPTION
    Uses a throwaway web tile `browserhost-check` (service netflix, cdpPort 9237) with its own profile
    `profiles\browserhost-check`, deleted before and after the run (never `profiles\netflix`). The real `apps.json` is
    copied and put back in `finally`. The published MediaStation runs with the harness off.

      1. Start MediaStation, OK on the tile, wait for `pageHello`; Home.
      2. Kill MediaStation (`Stop-Process -Force`); Edge stays alive (no job object). Record the profile's root PIDs.
      3. Start MediaStation again and assert:
         - the profile's browser root set is unchanged (one root, same PID);
         - the new run logs `probeExisting {adopted}`, `appAdopted`, `cdpConnected {adopted: true}` and
           `audioRuleApplied {rootPid, muted: true}` for that root, and no `appLaunch`;
         - the Edge window IsIconic and IsWindowVisible;
         - the shell is foreground within 5 s.
         Screenshot `after-adoption.png`.
      4. PostMessage a synthetic WM_POWERBROADCAST/PBT_APMRESUMEAUTOMATIC to the MediaStation window and assert
         `systemResumed`, `renewSession {renewing}`, `cdpReconnected {trigger: renewSession}`, still one root with the
         same PID, and the shell still foreground. Screenshot `after-renew.png`.
      5. OK on the tile: the Edge window is foreground; F5 (fallback Ctrl+R) in it gives a new `pageHello` from the
         renewed session.
      6. Close MediaStation and the profile's browser tree.
    Evidence (`measurements.jsonl`, screenshots) goes to `logs\browserhost-adopt-evidence-<ts>`. Exit code 0 = PASS.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-adopt.ps1
#>
param(
    [string]$MediaStation = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [ValidateRange(10, 120)][int]$HelloSeconds = 45
)

$ErrorActionPreference = 'Stop'

$AppId = 'browserhost-check'
$CdpPort = 9237
$WM_POWERBROADCAST = 0x0218
$PBT_APMRESUMEAUTOMATIC = 0x0012

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe'
}

if (-not ('BrowserAdopt.Native' -as [type])) {
    Add-Type -Namespace BrowserAdopt -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll", SetLastError = true)] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
'@
}

$native = [BrowserAdopt.Native]
# Physical pixels: without DPI awareness a scaled TV output gives logical metrics and the screenshot shows only a corner.
[void]$native::SetProcessDPIAware()
$problems = New-Object System.Collections.Generic.List[string]
$allowed = @('appIconMissing')
$profilesRoot = Join-Path $env:LOCALAPPDATA 'MediaStation\profiles'
$profileDir = Join-Path $profilesRoot $AppId
$evidenceDir = Join-Path $LogsDir ('browserhost-adopt-evidence-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$evidenceJsonl = Join-Path $evidenceDir 'measurements.jsonl'
$configDir = Join-Path $env:LOCALAPPDATA 'MediaStation\config'
$appsPath = Join-Path $configDir 'apps.json'
$backupPath = $appsPath + '.restore-bak'
$utf8 = New-Object System.Text.UTF8Encoding $false

# Guard: this script only ever touches its own throwaway profile.
if ($AppId -eq 'netflix' -or (Split-Path -Leaf $profileDir) -ne 'browserhost-check') { throw "Refusing to use profile $profileDir." }

function Add-Jsonl {
    param([object]$Record)
    [System.IO.File]::AppendAllText($evidenceJsonl, ($Record | ConvertTo-Json -Compress -Depth 8) + "`n", $utf8)
}

function Note { param([string]$Message) Write-Host ('{0} {1}' -f [DateTimeOffset]::UtcNow.ToString('HH:mm:ss.fff'), $Message) }

function Problem { param([string]$Message) $problems.Add($Message); Note "  PROBLEM: $Message" }

function Send-Key {
    param([byte]$Vk, [bool]$Extended = $false)
    $flags = if ($Extended) { 1 } else { 0 }
    $native::keybd_event($Vk, 0, $flags, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    $native::keybd_event($Vk, 0, ($flags -bor 2), [UIntPtr]::Zero)
}

function Send-Chord {
    param([byte]$Modifier, [byte]$Vk)
    $native::keybd_event($Modifier, 0, 0, [UIntPtr]::Zero)
    Send-Key -Vk $Vk
    $native::keybd_event($Modifier, 0, 2, [UIntPtr]::Zero)
}

function Set-Front {
    param([IntPtr]$Handle)
    if ($native::IsIconic($Handle)) { [void]$native::ShowWindow($Handle, 9) }
    $native::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    $native::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [void]$native::SetForegroundWindow($Handle)
}

function Wait-For {
    param([scriptblock]$Condition, [double]$Seconds)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return [bool](& $Condition)
}

function Get-RunEntries {
    param([int]$ProcessId, [string]$Msg = '')
    $path = Get-ChildItem -LiteralPath $LogsDir -Filter 'mediastation-*.jsonl' | Sort-Object Name | Select-Object -Last 1
    $stream = [System.IO.File]::Open($path.FullName, 'Open', 'Read', 'ReadWrite')
    try { $text = (New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)).ReadToEnd() } finally { $stream.Dispose() }
    $lines = $text -split "`n"
    $startAt = -1
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        if ($lines[$i] -match '"msg":"start".*"pid":(\d+)' -and [int]$Matches[1] -eq $ProcessId) { $startAt = $i; break }
    }
    $entries = @()
    if ($startAt -ge 0) {
        # A killed run has no end marker: stop at the next run's start.
        for ($i = $startAt; $i -lt $lines.Count; $i++) {
            if ($i -gt $startAt -and $lines[$i] -match '"msg":"start".*"pid":(\d+)' -and [int]$Matches[1] -ne $ProcessId) { break }
            if ($lines[$i].Trim().Length -gt 0 -and ($Msg.Length -eq 0 -or $lines[$i].Contains('"msg":"' + $Msg + '"'))) {
                try { $entries += ($lines[$i] | ConvertFrom-Json) } catch { }
            }
        }
    }
    return $entries
}

function Get-Ts {
    param([object]$Entry)
    if ($Entry.ts -is [DateTime]) { return [DateTimeOffset]::new([DateTime]::SpecifyKind($Entry.ts, 'Utc')) }
    return [DateTimeOffset]::Parse([string]$Entry.ts, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-Foreground {
    $window = $native::GetForegroundWindow()
    [uint32]$owner = 0
    [void]$native::GetWindowThreadProcessId($window, [ref]$owner)
    return [ordered]@{ hwnd = $window.ToInt64(); pid = [int]$owner }
}

function Save-Evidence {
    param([string]$Stage, [IntPtr]$Window, [hashtable]$Extra = @{})
    $png = Join-Path $evidenceDir ('{0}.png' -f $Stage)
    try {
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
        $left = $native::GetSystemMetrics(76); $top = $native::GetSystemMetrics(77)
        $width = $native::GetSystemMetrics(78); $height = $native::GetSystemMetrics(79)
        $bitmap = New-Object System.Drawing.Bitmap($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($left, $top, 0, 0, [System.Drawing.Size]::new($width, $height))
        $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose(); $bitmap.Dispose()
    } catch { $problems.Add("${Stage}: screenshot failed: $($_.Exception.Message)"); $png = $null }
    $record = [ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = $Stage; screenshot = $png; foreground = (Get-Foreground); iconic = $native::IsIconic($Window); visible = $native::IsWindowVisible($Window) }
    foreach ($key in $Extra.Keys) { $record[$key] = $Extra[$key] }
    Add-Jsonl -Record $record
    return $record
}

function Get-ProfileProcesses {
    return @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.Replace('"', '') -like "*--user-data-dir=$profileDir*" })
}

function Get-ProfileRoots {
    return @(Get-ProfileProcesses | Where-Object { $_.CommandLine -notmatch ' --type=' } | ForEach-Object { [int]$_.ProcessId } | Sort-Object)
}

function Stop-Profile {
    foreach ($root in @(Get-ProfileProcesses | Where-Object { $_.CommandLine -notmatch '--type=' })) { & taskkill.exe /PID $root.ProcessId /T /F 2>&1 | Out-Null }
    [void](Wait-For -Seconds 10 -Condition { (Get-ProfileProcesses).Count -eq 0 })
}

function Remove-TestProfile {
    Stop-Profile
    if (Test-Path -LiteralPath $profileDir) {
        [void](Wait-For -Seconds 10 -Condition { Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue; -not (Test-Path -LiteralPath $profileDir) })
        if (Test-Path -LiteralPath $profileDir) { throw "Could not delete $profileDir." }
    }
}

function Start-Run {
    $app = Start-Process -FilePath $MediaStation -PassThru
    Note ("MediaStation pid {0}" -f $app.Id)
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'mediastation-start'; pid = $app.Id })
    if (-not (Wait-For -Seconds 30 -Condition { $app.Refresh(); $app.MainWindowHandle -ne 0 })) { throw 'MediaStation window did not appear within 30s.' }
    if (-not (Wait-For -Seconds 30 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'snapshotPublished').Count -ge 1 })) { throw 'No snapshotPublished within 30s.' }
    if (@(Get-RunEntries -ProcessId $app.Id -Msg 'browserHostEnabled').Count -ne 1) { throw 'No browserHostEnabled entry: BrowserHost is not wired.' }
    if (@(Get-RunEntries -ProcessId $app.Id -Msg 'devHarnessEnabled').Count -ne 0) { throw 'The dev harness is enabled.' }
    return $app
}

function Get-ShellHandle {
    param($App)
    $shell = [IntPtr]$App.MainWindowHandle
    $managed = @(Get-RunEntries -ProcessId $App.Id -Msg 'managedShell') | Select-Object -Last 1
    if ($null -ne $managed) { $shell = [IntPtr]([int64]$managed.data.hwnd) }
    return $shell
}

function Send-Ok {
    param($App)
    Start-Sleep -Seconds 2
    Set-Front -Handle (Get-ShellHandle -App $App)
    Start-Sleep -Milliseconds 800
    Note 'injected OK'
    Send-Key -Vk 0x0D
}

function Wait-Log {
    param($App, [string]$Msg, [double]$Seconds, [scriptblock]$Where = { $true })
    return Wait-For -Seconds $Seconds -Condition { @(Get-RunEntries -ProcessId $App.Id -Msg $Msg | Where-Object $Where).Count -ge 1 }
}

function Assert-Levels {
    param([string]$Stage, [object[]]$Entries)
    foreach ($entry in @($Entries | Where-Object { $_.level -in @('warn', 'error') -and $allowed -notcontains $_.msg })) {
        Problem ("{0}: unexpected log {1}: {2} {3}" -f $Stage, $entry.level, $entry.msg, ($entry.data | ConvertTo-Json -Compress -Depth 4))
    }
}

function Test-ShellForeground {
    param($App, [double]$Seconds)
    $started = [DateTimeOffset]::UtcNow
    $ok = Wait-For -Seconds $Seconds -Condition { (Get-Foreground).pid -eq $App.Id }
    return [ordered]@{ ok = $ok; waitedMs = [int]([DateTimeOffset]::UtcNow - $started).TotalMilliseconds }
}

if (-not (Test-Path -LiteralPath $MediaStation)) { throw "MediaStation.exe not found: $MediaStation - publish Release first." }
foreach ($asset in @('page\runtime.js', 'page\drivers\netflix\manifest.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $MediaStation) $asset))) { throw "Publish has no $asset next to MediaStation.exe." }
}
if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'MediaStation is already running; close it and retry.' }
if (@(Get-NetTCPConnection -LocalPort $CdpPort -State Listen -ErrorAction SilentlyContinue).Count -gt 0) { throw "Port $CdpPort is already listening; free it and retry." }
if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }

[void](New-Item -ItemType Directory -Path $evidenceDir -Force)
$machine = [ordered]@{ computer = $env:COMPUTERNAME; board = (Get-CimInstance Win32_BaseBoard).Product; model = (Get-CimInstance Win32_ComputerSystem).Model; mediaStation = $MediaStation }
Note ('machine ' + ($machine | ConvertTo-Json -Compress))
Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'machine'; machine = $machine })

$tile = [ordered]@{ type = 'web'; id = $AppId; category = 'video'; title = 'BrowserHost check'; icon = 'netflix.png'; service = 'netflix'; browser = 'edge'; cdpPort = $CdpPort }
$first = $null
$second = $null
try {
    Copy-Item -LiteralPath $appsPath -Destination $backupPath -Force
    Set-Content -LiteralPath $appsPath -Value (@{ apps = @($tile) } | ConvertTo-Json -Depth 5) -Encoding UTF8
    Remove-Item Env:\MEDIASTATION_DEV_HARNESS -ErrorAction SilentlyContinue
    Remove-Item Env:\MEDIASTATION_HARNESS_URL -ErrorAction SilentlyContinue
    Remove-Item Env:\__COMPAT_LAYER -ErrorAction SilentlyContinue
    Remove-TestProfile

    # 1. A normal start up to the page's hello, then Home.
    Note '=== 1. start ==='
    $first = Start-Run
    Send-Ok -App $first
    if (-not (Wait-Log -App $first -Msg 'appWindowAttached' -Seconds 30)) { throw 'no appWindowAttached within 30s' }
    if (-not (Wait-Log -App $first -Msg 'pageHello' -Seconds $HelloSeconds)) { throw "no pageHello within ${HelloSeconds}s" }
    $launched = Get-RunEntries -ProcessId $first.Id -Msg 'appWindowAttached' | Select-Object -First 1
    $rootPid = [int]$launched.data.rootPid
    $window = [IntPtr]([int64]$launched.data.hwnd)
    Note ("started: rootPid {0} hwnd {1}" -f $rootPid, $window)
    Start-Sleep -Seconds 1
    Note 'injected Home'
    Send-Key -Vk 0x24 -Extended $true
    if (-not (Wait-For -Seconds 5 -Condition { $native::IsIconic($window) })) { Problem 'Edge window not minimized after Home in the first run' }

    # 2. Crash: MediaStation dies, Edge stays.
    Note '=== 2. kill MediaStation ==='
    $rootsBefore = Get-ProfileRoots
    Stop-Process -Id $first.Id -Force
    [void]$first.WaitForExit(10000)
    $first = $null
    Start-Sleep -Seconds 2
    $rootsAfterKill = Get-ProfileRoots
    Note ("roots before kill [{0}], after kill [{1}]" -f ($rootsBefore -join ','), ($rootsAfterKill -join ','))
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'killed'; rootPid = $rootPid; rootsBefore = $rootsBefore; rootsAfterKill = $rootsAfterKill })
    if ($rootsAfterKill.Count -ne 1 -or $rootsAfterKill[0] -ne $rootPid) { throw "after the kill the profile roots are [$($rootsAfterKill -join ',')], expected [$rootPid]" }

    # 3. Restart: adoption without a second launch.
    Note '=== 3. restart and adoption ==='
    $second = Start-Run
    if (-not (Wait-Log -App $second -Msg 'appAdopted' -Seconds 30)) { Problem 'no appAdopted within 30s' }
    $shellAfterAdopt = Test-ShellForeground -App $second -Seconds 5
    if (-not (Wait-Log -App $second -Msg 'probeExisting' -Seconds 30)) { Problem 'no probeExisting within 30s' }
    [void](Wait-Log -App $second -Msg 'audioRuleApplied' -Seconds 5 -Where { [int]$_.data.rootPid -eq $rootPid -and $_.data.muted -eq $true })
    $entries = @(Get-RunEntries -ProcessId $second.Id)
    $probe = $entries | Where-Object { $_.msg -eq 'probeExisting' } | Select-Object -First 1
    $adopted = $entries | Where-Object { $_.msg -eq 'appAdopted' } | Select-Object -First 1
    $connected = @($entries | Where-Object { $_.msg -eq 'cdpConnected' })
    $mutes = @($entries | Where-Object { $_.msg -eq 'audioRuleApplied' -and [int]$_.data.rootPid -eq $rootPid -and $_.data.muted -eq $true })
    $launches = @($entries | Where-Object { $_.msg -eq 'appLaunch' })
    $rootsAfterAdopt = Get-ProfileRoots
    foreach ($e in @($probe, $adopted) + $connected) { if ($null -ne $e) { Note ("{0} {1}" -f $e.msg, ($e.data | ConvertTo-Json -Compress)) } }
    if ($null -eq $probe -or $probe.data.outcome -ne 'adopted' -or [int]$probe.data.rootPid -ne $rootPid) { Problem "probeExisting is not {adopted, rootPid: $rootPid}" }
    if ($null -eq $adopted -or [int]$adopted.data.rootPid -ne $rootPid) { Problem "appAdopted is not for root $rootPid" }
    if ($connected.Count -ne 1 -or $connected[0].data.adopted -ne $true) { Problem 'expected one cdpConnected {adopted: true}' }
    if ($mutes.Count -lt 1) { Problem "no audioRuleApplied {rootPid: $rootPid, muted: true}" }
    if ($launches.Count -ne 0) { Problem "appLaunch x$($launches.Count) in the adopting run" }
    if ($rootsAfterAdopt.Count -ne 1 -or $rootsAfterAdopt[0] -ne $rootPid) { Problem "profile roots after adoption [$($rootsAfterAdopt -join ',')], expected [$rootPid]" }
    $adoptedWindow = if ($null -ne $adopted) { [IntPtr]([int64]$adopted.data.hwnd) } else { $window }
    [void](Wait-For -Seconds 5 -Condition { $native::IsIconic($adoptedWindow) })
    $shot = Save-Evidence -Stage 'after-adoption' -Window $adoptedWindow -Extra @{ rootPid = $rootPid; rootsAfterAdopt = $rootsAfterAdopt; shellForeground = $shellAfterAdopt; probe = $probe.data; adopted = $adopted.data; mutes = @($mutes | ForEach-Object { $_.data }) }
    Note ("after adoption: shell foreground {0} (waited {1} ms), iconic={2} visible={3}, roots [{4}]" -f $shellAfterAdopt.ok, $shellAfterAdopt.waitedMs, $shot.iconic, $shot.visible, ($rootsAfterAdopt -join ','))
    if (-not $shellAfterAdopt.ok) { Problem 'the shell is not foreground within 5 s of the adoption' }
    if (-not ($shot.iconic -and $shot.visible)) { Problem 'adopted Edge window is not IsIconic and IsWindowVisible' }

    # 4. Synthetic wake: RenewSession on the adopted session.
    Note '=== 4. synthetic resume ==='
    $second.Refresh()
    $msWindow = [IntPtr]$second.MainWindowHandle
    if (-not $native::PostMessage($msWindow, $WM_POWERBROADCAST, [IntPtr]$PBT_APMRESUMEAUTOMATIC, [IntPtr]::Zero)) { throw "PostMessage(WM_POWERBROADCAST) failed (Win32: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error()))." }
    $resumedAt = [DateTimeOffset]::UtcNow
    Add-Jsonl -Record ([ordered]@{ utc = $resumedAt.ToString('o'); stage = 'synthetic-resume'; hwnd = $msWindow.ToInt64(); source = 'PBT_APMRESUMEAUTOMATIC'; synthetic = $true })
    if (-not (Wait-Log -App $second -Msg 'systemResumed' -Seconds 10)) { Problem 'no systemResumed within 10s' }
    if (-not (Wait-Log -App $second -Msg 'renewSession' -Seconds 10 -Where { $_.data.outcome -eq 'renewing' })) { Problem 'no renewSession {renewing} within 10s' }
    if (-not (Wait-Log -App $second -Msg 'cdpReconnected' -Seconds 35 -Where { $_.data.trigger -eq 'renewSession' })) { Problem 'no cdpReconnected {trigger: renewSession} within 35s' }
    $entries = @(Get-RunEntries -ProcessId $second.Id)
    foreach ($msg in @('systemResumed', 'renewSession', 'cdpReconnected')) {
        foreach ($e in @($entries | Where-Object { $_.msg -eq $msg })) { Note ("{0} {1}" -f $e.msg, ($e.data | ConvertTo-Json -Compress)) }
    }
    $shellAfterRenew = Test-ShellForeground -App $second -Seconds 2
    $rootsAfterRenew = Get-ProfileRoots
    $lost = @($entries | Where-Object { $_.msg -in @('cdpLost', 'cdpClosed', 'processExited') })
    $shot = Save-Evidence -Stage 'after-renew' -Window $adoptedWindow -Extra @{ rootsAfterRenew = $rootsAfterRenew; shellForeground = $shellAfterRenew; renew = @($entries | Where-Object { $_.msg -eq 'renewSession' } | ForEach-Object { $_.data }); reconnected = @($entries | Where-Object { $_.msg -eq 'cdpReconnected' } | ForEach-Object { $_.data }) }
    Note ("after renew: shell foreground {0}, roots [{1}], iconic={2}" -f $shellAfterRenew.ok, ($rootsAfterRenew -join ','), $shot.iconic)
    if ($rootsAfterRenew.Count -ne 1 -or $rootsAfterRenew[0] -ne $rootPid) { Problem "profile roots after renew [$($rootsAfterRenew -join ',')], expected [$rootPid]" }
    if (-not $shellAfterRenew.ok) { Problem 'the shell is not foreground after the synthetic resume' }
    if ($lost.Count -ne 0) { Problem ('unexpected ' + (($lost | ForEach-Object { $_.msg }) -join ', ')) }

    # 5. The bridge after renew: OK brings Edge back, a reload gives a hello on the renewed session.
    Note '=== 5. OK and reload ==='
    $hellosBefore = @(Get-RunEntries -ProcessId $second.Id -Msg 'pageHello').Count
    Send-Ok -App $second
    $edgeFront = Wait-For -Seconds 10 -Condition { (Get-Foreground).hwnd -eq $adoptedWindow.ToInt64() }
    Note ("Edge foreground after OK: {0} ({1})" -f $edgeFront, ((Get-Foreground) | ConvertTo-Json -Compress))
    if (-not $edgeFront) { Problem 'Edge window is not foreground after OK on the tile' }
    Start-Sleep -Seconds 1
    Note 'injected F5'
    Send-Key -Vk 0x74
    $reloaded = Wait-For -Seconds $HelloSeconds -Condition { @(Get-RunEntries -ProcessId $second.Id -Msg 'pageHello').Count -gt $hellosBefore }
    $key = 'F5'
    if (-not $reloaded) {
        Note 'no pageHello after F5; injected Ctrl+R'
        $key = 'Ctrl+R'
        Set-Front -Handle $adoptedWindow
        Start-Sleep -Milliseconds 500
        Send-Chord -Modifier 0x11 -Vk 0x52
        $reloaded = Wait-For -Seconds $HelloSeconds -Condition { @(Get-RunEntries -ProcessId $second.Id -Msg 'pageHello').Count -gt $hellosBefore }
    }
    $hellos = @(Get-RunEntries -ProcessId $second.Id -Msg 'pageHello')
    Note ("pageHello x{0} (before reload {1}) after {2}" -f $hellos.Count, $hellosBefore, $key)
    if (-not $reloaded) { Problem 'no new pageHello after the reload' }
    $shot = Save-Evidence -Stage 'after-reload' -Window $adoptedWindow -Extra @{ reloadKey = $key; pageHello = @($hellos | ForEach-Object { $_.data }) }
    if ($shot.foreground.hwnd -ne $adoptedWindow.ToInt64()) { Problem 'Edge window is not foreground after the reload' }

    $entries = @(Get-RunEntries -ProcessId $second.Id)
    Assert-Levels -Stage 'adopting run' -Entries $entries
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'summary'; rootPid = $rootPid; launchesInAdoptingRun = @($entries | Where-Object { $_.msg -eq 'appLaunch' }).Count; problems = @($problems) })
}
catch {
    $problems.Add('run aborted: ' + $_.Exception.Message)
}
finally {
    foreach ($app in @($first, $second)) {
        if ($null -ne $app -and -not $app.HasExited) {
            [void]$app.CloseMainWindow()
            if (-not $app.WaitForExit(8000)) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
        }
    }
    if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) { Get-Process -Name 'MediaStation' | Stop-Process -Force -ErrorAction SilentlyContinue }
    try { Remove-TestProfile } catch { $problems.Add('cleanup: ' + $_.Exception.Message) }
    if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }
}

Write-Host ''
Write-Host ('Evidence: ' + $evidenceDir)
if ($problems.Count -gt 0) {
    Write-Host ('FAIL ({0} problems)' -f $problems.Count) -ForegroundColor Red
    $problems | ForEach-Object { Write-Host ('  - ' + $_) }
    exit 1
}

Write-Host 'PASS: the live Edge was adopted after a crash without a second launch, a synthetic wake renewed its CDP session, and the bridge said hello after a reload.' -ForegroundColor Green
