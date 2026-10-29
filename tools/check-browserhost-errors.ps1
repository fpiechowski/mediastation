<#
.SYNOPSIS
    Story 6.4 check: BrowserHost logs the browser version and tells its start errors apart (AD-13), live on this machine.

.DESCRIPTION
    Uses a throwaway web tile `browserhost-check` (service `-Service`, default netflix; cdpPort 9237) with its own profile
    `profiles\browserhost-check`, deleted before and after the run (never `profiles\netflix`). The real `apps.json` is
    copied and put back in `finally`. MediaStation is started with `__COMPAT_LAYER=DetectorsAppHealth`, so every run
    proves that BrowserHost strips it from Edge (otherwise Edge relaunches itself and the launched root exits at once);
    the variable is removed from the script's own environment right after, so the Edge started in D runs without it.

    Each scenario starts the published MediaStation (harness off), injects OK on the shell, asserts the log, then closes
    MediaStation and the profile's browser tree:
      A   `Last Version` = 155.0.4283.45: `browserVersionChanged {from 155.0.4283.45, to <exe FileVersion>}` before
          `appLaunch`, `cdpConnected.browserVersion` = to, no `browserVersionMismatch`, `pageHello`, no `processExited`
          and the `appWindowAttached` rootPid still alive (A, A2 and B alike); screenshot.
      A2  rerun on the same profile: `browserVersion` with version = previous and no change entry.
      B   `Last Version` = garbage: `browserVersion.previous` = unknown, no change entry.
      C   a TcpListener on 127.0.0.1:9237: `browserStartFailed {portInUse}`, `commandFailed {PortInUse}`, no `appLaunch`,
          no browser on the profile.
      D   an Edge started by this script on the profile after the startup probe logged `probeExisting {notRunning}`
          (an Edge already there at startup is adopted since story 6.5): `browserStartFailed {profileInUse}`,
          `commandFailed {ProfileInUse}`, no `appLaunch`, the profile's browser processes unchanged.
      E   `devtools.remote_debugging.allowed=false` in the profile's `Local State`: `appWindowAttached`, then
          (>= cdpConnectTimeout after `appLaunch`) `cdpUnavailable` with `browserVersion` and `commandFailed
          {CdpUnavailable}`, no `processExited`; the Edge root alive, its window IsIconic and IsWindowVisible, the shell
          foreground and `audioRuleApplied {rootPid, muted: true}`; screenshot.
    Evidence (`measurements.jsonl`, screenshots) goes to `logs\browserhost-errors-evidence-<ts>`. Exit code 0 = PASS.

.PARAMETER Service
    The `service` of the generated tile (default `netflix`); the published `page\drivers\<service>\manifest.json` must
    exist. The e2e suite (`check-browserhost-e2e.ps1`) passes its test-only sample driver here.

.PARAMETER EvidenceDir
    The evidence folder (default `logs\browserhost-errors-evidence-<ts>`).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-errors.ps1
#>
param(
    [string]$MediaStation = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [ValidateRange(10, 120)][int]$HelloSeconds = 45,
    [string]$Service = 'netflix',
    [string]$EvidenceDir = ''
)

$ErrorActionPreference = 'Stop'

$AppId = 'browserhost-check'
$CdpPort = 9237
$CompatLayer = 'DetectorsAppHealth'

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe'
}

if (-not ('BrowserErrors.Native' -as [type])) {
    Add-Type -Namespace BrowserErrors -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
[DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
[DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
[StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint flags; }
'@
}

$native = [BrowserErrors.Native]
$problems = New-Object System.Collections.Generic.List[string]
$baseAllowed = @('appIconMissing')
$profilesRoot = Join-Path $env:LOCALAPPDATA 'MediaStation\profiles'
$profileDir = Join-Path $profilesRoot $AppId
$evidenceDir = if ([string]::IsNullOrWhiteSpace($EvidenceDir)) { Join-Path $LogsDir ('browserhost-errors-evidence-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) } else { $EvidenceDir }
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

function Problem { param([string]$Scenario, [string]$Message) $problems.Add("${Scenario}: $Message"); Note "  PROBLEM ${Scenario}: $Message" }

function Send-Key {
    param([byte]$Vk)
    $native::keybd_event($Vk, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    $native::keybd_event($Vk, 0, 2, [UIntPtr]::Zero)
}

function Set-Front {
    param([IntPtr]$Handle)
    if ($native::IsIconic($Handle)) { [void]$native::ShowWindow($Handle, 9) }
    $native::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    $native::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [void]$native::SetForegroundWindow($Handle)
}

function Wait-For {
    param([scriptblock]$Condition, [int]$Seconds)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 200
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
        for ($i = $startAt; $i -lt $lines.Count; $i++) {
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

function Index-Of {
    param([object[]]$Entries, [string]$Msg)
    for ($i = 0; $i -lt $Entries.Count; $i++) { if ($Entries[$i].msg -eq $Msg) { return $i } }
    return -1
}

function Get-ForegroundPid {
    $window = $native::GetForegroundWindow()
    [uint32]$owner = 0
    [void]$native::GetWindowThreadProcessId($window, [ref]$owner)
    return [ordered]@{ hwnd = $window.ToInt64(); pid = [int]$owner }
}

function Get-Geometry {
    param([IntPtr]$Window)
    $rect = New-Object BrowserErrors.Native+RECT
    if (-not $native::IsWindow($Window) -or -not $native::GetWindowRect($Window, [ref]$rect)) { return $null }
    $monitor = $native::MonitorFromWindow($Window, 2)
    $info = New-Object BrowserErrors.Native+MONITORINFO
    $info.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($info)
    if (-not $native::GetMonitorInfo($monitor, [ref]$info)) { return $null }
    $covers = ($rect.left -le $info.rcMonitor.left -and $rect.top -le $info.rcMonitor.top -and
        $rect.right -ge $info.rcMonitor.right -and $rect.bottom -ge $info.rcMonitor.bottom)
    return [ordered]@{ hwnd = $Window.ToInt64(); left = $rect.left; top = $rect.top; right = $rect.right; bottom = $rect.bottom; coversMonitor = $covers }
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
    $record = [ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = $Stage; screenshot = $png; geometry = (Get-Geometry -Window $Window); foreground = (Get-ForegroundPid); iconic = $native::IsIconic($Window); visible = $native::IsWindowVisible($Window) }
    foreach ($key in $Extra.Keys) { $record[$key] = $Extra[$key] }
    Add-Jsonl -Record $record
    return $record
}

function Get-ProfileProcesses {
    return @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.Replace('"', '') -like "*--user-data-dir=$profileDir*" })
}

function Stop-Profile {
    foreach ($root in @(Get-ProfileProcesses | Where-Object { $_.CommandLine -notmatch '--type=' })) { & { $ErrorActionPreference = 'Continue'; & taskkill.exe /PID $root.ProcessId /T /F 2>&1 | Out-Null } }
    [void](Wait-For -Seconds 10 -Condition { (Get-ProfileProcesses).Count -eq 0 })
}

function Remove-TestProfile {
    Stop-Profile
    if (Test-Path -LiteralPath $profileDir) {
        [void](Wait-For -Seconds 10 -Condition { Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue; -not (Test-Path -LiteralPath $profileDir) })
        if (Test-Path -LiteralPath $profileDir) { throw "Could not delete $profileDir." }
    }
}

function Test-LockHeld {
    $lock = Join-Path $profileDir 'lockfile'
    try { $fs = [System.IO.File]::Open($lock, 'Open', 'Write', 'ReadWrite, Delete'); $fs.Dispose(); return $false }
    catch [System.IO.FileNotFoundException] { return $false }
    catch [System.IO.DirectoryNotFoundException] { return $false }
    catch { return $true }
}

function Get-FileVersionText {
    param([string]$Path)
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    return '{0}.{1}.{2}.{3}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart
}

function Write-TextFile {
    param([string]$Path, [string]$Text)
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force)
    [System.IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Start-Run {
    # Only MediaStation gets the compat layer: the launcher must strip it before starting Edge.
    $env:__COMPAT_LAYER = $CompatLayer
    try { $app = Start-Process -FilePath $MediaStation -PassThru }
    finally { Remove-Item Env:\__COMPAT_LAYER -ErrorAction SilentlyContinue }
    Note ("MediaStation pid {0} (__COMPAT_LAYER={1})" -f $app.Id, $CompatLayer)
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'mediastation-start'; pid = $app.Id; compatLayerGivenToMediaStation = $CompatLayer })
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

function Stop-Run {
    param($App)
    if ($null -ne $App) {
        [void]$App.CloseMainWindow()
        if (-not $App.WaitForExit(8000)) { Stop-Process -Id $App.Id -Force -ErrorAction SilentlyContinue }
    }
    Stop-Profile
}

function Assert-Levels {
    param([string]$Scenario, [object[]]$Entries, [string[]]$Allowed)
    $all = $baseAllowed + $Allowed
    foreach ($entry in @($Entries | Where-Object { $_.level -in @('warn', 'error') -and $all -notcontains $_.msg })) {
        Problem $Scenario ("unexpected log {0}: {1} {2}" -f $entry.level, $entry.msg, ($entry.data | ConvertTo-Json -Compress -Depth 4))
    }
}

function Wait-Log {
    param($App, [string]$Msg, [int]$Seconds)
    return Wait-For -Seconds $Seconds -Condition { @(Get-RunEntries -ProcessId $App.Id -Msg $Msg).Count -ge 1 }
}

# Scenarios A, A2, B: a normal start whose version entries are checked against the exe's FileVersion.
function Invoke-VersionScenario {
    param([string]$Scenario, [string]$LastVersion, [string]$ExpectPrevious, [bool]$ExpectChanged, [bool]$Screenshot)
    Note "=== $Scenario ==="
    # Empty = keep whatever the previous run's browser wrote (A2).
    if (-not [string]::IsNullOrEmpty($LastVersion)) { Write-TextFile -Path (Join-Path $profileDir 'Last Version') -Text $LastVersion }
    $app = $null
    try {
        $app = Start-Run
        Send-Ok -App $app
        if (-not (Wait-Log -App $app -Msg 'appWindowAttached' -Seconds 30)) { Problem $Scenario 'no appWindowAttached within 30s'; return }
        if (-not (Wait-Log -App $app -Msg 'cdpConnected' -Seconds 20)) { Problem $Scenario 'no cdpConnected within 20s' }
        if (-not (Wait-Log -App $app -Msg 'pageHello' -Seconds $HelloSeconds)) { Problem $Scenario "no pageHello within ${HelloSeconds}s" }

        $entries = @(Get-RunEntries -ProcessId $app.Id)
        $launch = $entries | Where-Object { $_.msg -eq 'appLaunch' } | Select-Object -First 1
        $version = $entries | Where-Object { $_.msg -eq 'browserVersion' } | Select-Object -First 1
        $changed = @($entries | Where-Object { $_.msg -eq 'browserVersionChanged' })
        $connected = $entries | Where-Object { $_.msg -eq 'cdpConnected' } | Select-Object -First 1
        $mismatch = @($entries | Where-Object { $_.msg -eq 'browserVersionMismatch' })
        if ($null -eq $launch -or $null -eq $version) { Problem $Scenario 'missing appLaunch or browserVersion'; return }

        $exe = if ($launch.data.commandLine -match '^"([^"]+)"') { $Matches[1] } else { '' }
        $exeVersion = Get-FileVersionText -Path $exe
        Note ("browserVersion {0}; exe {1} FileVersion {2}" -f ($version.data | ConvertTo-Json -Compress), $exe, $exeVersion)
        if ($version.data.version -ne $exeVersion) { Problem $Scenario "browserVersion.version=$($version.data.version), exe FileVersion $exeVersion" }
        if ($version.data.previous -ne $ExpectPrevious) { Problem $Scenario "browserVersion.previous=$($version.data.previous), expected $ExpectPrevious" }
        if ((Index-Of $entries 'browserVersion') -gt (Index-Of $entries 'appLaunch')) { Problem $Scenario 'browserVersion is not before appLaunch' }
        if ($ExpectChanged) {
            if ($changed.Count -ne 1) { Problem $Scenario "browserVersionChanged x$($changed.Count), expected 1" }
            else {
                Note ('browserVersionChanged ' + ($changed[0].data | ConvertTo-Json -Compress))
                if ($changed[0].data.from -ne $LastVersion) { Problem $Scenario "browserVersionChanged.from=$($changed[0].data.from)" }
                if ($changed[0].data.to -ne $exeVersion) { Problem $Scenario "browserVersionChanged.to=$($changed[0].data.to), exe FileVersion $exeVersion" }
                if ((Index-Of $entries 'browserVersionChanged') -gt (Index-Of $entries 'appLaunch')) { Problem $Scenario 'browserVersionChanged is not before appLaunch' }
            }
        }
        elseif ($changed.Count -ne 0) { Problem $Scenario "unexpected browserVersionChanged $($changed[0].data | ConvertTo-Json -Compress)" }
        if ($null -ne $connected) {
            Note ('cdpConnected browserVersion=' + $connected.data.browserVersion)
            if ($connected.data.browserVersion -ne $exeVersion) { Problem $Scenario "cdpConnected.browserVersion=$($connected.data.browserVersion), expected $exeVersion" }
        }
        if ($mismatch.Count -ne 0) { Problem $Scenario 'browserVersionMismatch logged' }

        # MediaStation runs with __COMPAT_LAYER: an unstripped Edge would relaunch itself and the root would exit.
        $attachedEntry = $entries | Where-Object { $_.msg -eq 'appWindowAttached' } | Select-Object -First 1
        $rootPid = if ($null -ne $attachedEntry) { [int]$attachedEntry.data.rootPid } else { 0 }
        $rootAlive = $rootPid -gt 0 -and $null -ne (Get-Process -Id $rootPid -ErrorAction SilentlyContinue)
        $exitedCount = @($entries | Where-Object { $_.msg -eq 'processExited' }).Count
        Note ("rootPid {0} alive={1}, processExited x{2}" -f $rootPid, $rootAlive, $exitedCount)
        if (-not $rootAlive) { Problem $Scenario "Edge root $rootPid from appWindowAttached is not alive" }
        if ($exitedCount -ne 0) { Problem $Scenario "processExited x$exitedCount, expected 0" }

        $record = [ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = $Scenario; lastVersionWritten = $LastVersion; exe = $exe; exeFileVersion = $exeVersion; browserVersion = $version.data; browserVersionChanged = @($changed | ForEach-Object { $_.data }); cdpConnected = $connected.data; rootPid = $rootPid; rootAlive = $rootAlive; processExited = $exitedCount; compatLayerGivenToMediaStation = $CompatLayer; pageHello = @($entries | Where-Object { $_.msg -eq 'pageHello' } | ForEach-Object { $_.data }) }
        Add-Jsonl -Record $record

        if ($Screenshot) {
            $attached = $entries | Where-Object { $_.msg -eq 'appWindowAttached' } | Select-Object -First 1
            $window = [IntPtr]([int64]$attached.data.hwnd)
            [void](Wait-For -Seconds 10 -Condition { $native::GetForegroundWindow() -eq $window })
            Start-Sleep -Seconds 2
            $shot = Save-Evidence -Stage "$Scenario-after-start" -Window $window
            Note ("after start: foreground pid={0} covers={1} iconic={2}" -f $shot.foreground.pid, $shot.geometry.coversMonitor, $shot.iconic)
            if ($null -eq $shot.geometry -or -not $shot.geometry.coversMonitor) { Problem $Scenario 'Edge window does not cover its monitor' }
        }

        Assert-Levels -Scenario $Scenario -Entries @(Get-RunEntries -ProcessId $app.Id) -Allowed @()
    }
    finally { Stop-Run -App $app }
}

# Scenarios C and D: a held resource gives a distinguishable CommandFailed and nothing is launched.
function Invoke-HeldScenario {
    param([string]$Scenario, [string]$Reason, [string]$ErrorCode, [scriptblock]$ProcessSnapshot, [scriptblock]$AfterProbe = $null)
    $app = $null
    try {
        $app = Start-Run
        # Story 6.5: the startup probe adopts a live browser on the profile, so a held profile is set up only after
        # the probe found nothing; the scenario then checks the start path's own ProfileInUse.
        if (-not (Wait-Log -App $app -Msg 'probeExisting' -Seconds 20)) { Problem $Scenario 'no probeExisting within 20s'; return }
        $probe = @(Get-RunEntries -ProcessId $app.Id -Msg 'probeExisting')[0]
        if ($probe.data.outcome -ne 'notRunning') { Problem $Scenario "probeExisting outcome $($probe.data.outcome), expected notRunning" }
        if ($null -ne $AfterProbe) { & $AfterProbe }
        $before = & $ProcessSnapshot
        Send-Ok -App $app
        if (-not (Wait-Log -App $app -Msg 'commandFailed' -Seconds 20)) { Problem $Scenario 'no commandFailed within 20s'; return }
        Start-Sleep -Seconds 3
        $entries = @(Get-RunEntries -ProcessId $app.Id)
        $failed = @($entries | Where-Object { $_.msg -eq 'commandFailed' })
        $startFailed = @($entries | Where-Object { $_.msg -eq 'browserStartFailed' })
        foreach ($f in $startFailed) { Note ('browserStartFailed ' + ($f.data | ConvertTo-Json -Compress)) }
        foreach ($f in $failed) { Note ('commandFailed ' + ($f.data | ConvertTo-Json -Compress)) }
        if ($startFailed.Count -ne 1 -or $startFailed[0].data.reason -ne $Reason) { Problem $Scenario "expected one browserStartFailed {reason: $Reason}" }
        if ($failed.Count -ne 1 -or $failed[0].data.errorCode -cne $ErrorCode) { Problem $Scenario "expected one commandFailed {errorCode: $ErrorCode}" }
        foreach ($msg in @('appLaunch', 'browserVersion', 'appWindowAttached')) {
            if (@($entries | Where-Object { $_.msg -eq $msg }).Count -ne 0) { Problem $Scenario "unexpected $msg" }
        }
        $after = & $ProcessSnapshot
        Note ("profile processes before={0} after={1}" -f ($before -join ','), ($after -join ','))
        if (($before -join ',') -ne ($after -join ',')) { Problem $Scenario "profile processes changed: before [$($before -join ',')] after [$($after -join ',')]" }
        Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = $Scenario; browserStartFailed = @($startFailed | ForEach-Object { $_.data }); commandFailed = @($failed | ForEach-Object { $_.data }); profileProcessesBefore = $before; profileProcessesAfter = $after })
        Assert-Levels -Scenario $Scenario -Entries $entries -Allowed @('browserStartFailed', 'commandFailed')
    }
    finally { Stop-Run -App $app }
}

function Get-ProfilePids { return @(Get-ProfileProcesses | ForEach-Object { [int]$_.ProcessId } | Sort-Object) }

if (-not (Test-Path -LiteralPath $MediaStation)) { throw "MediaStation.exe not found: $MediaStation - publish Release first." }
foreach ($asset in @('page\runtime.js', "page\drivers\$Service\manifest.json")) {
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $MediaStation) $asset))) { throw "Publish has no $asset next to MediaStation.exe." }
}
if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'MediaStation is already running; close it and retry.' }
if (@(Get-NetTCPConnection -LocalPort $CdpPort -State Listen -ErrorAction SilentlyContinue).Count -gt 0) { throw "Port $CdpPort is already listening; free it and retry." }
if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }

$cdpConnectTimeout = 15
try {
    $settings = Get-Content -LiteralPath (Join-Path $configDir 'settings.json') -Raw | ConvertFrom-Json
    if ($null -ne $settings.cdpConnectTimeout) { $cdpConnectTimeout = [int]$settings.cdpConnectTimeout }
} catch { }

$edgeExe = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\Application\msedge.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($null -eq $edgeExe) { throw 'msedge.exe not found.' }

[void](New-Item -ItemType Directory -Path $evidenceDir -Force)
$machine = [ordered]@{ computer = $env:COMPUTERNAME; board = (Get-CimInstance Win32_BaseBoard).Product; model = (Get-CimInstance Win32_ComputerSystem).Model; edge = $edgeExe; edgeFileVersion = (Get-FileVersionText -Path $edgeExe); cdpConnectTimeout = $cdpConnectTimeout; compatLayerInherited = [Environment]::GetEnvironmentVariable('__COMPAT_LAYER') }
Note ('machine ' + ($machine | ConvertTo-Json -Compress))
Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'machine'; machine = $machine })

$tile = [ordered]@{ type = 'web'; id = $AppId; category = 'video'; title = 'BrowserHost check'; icon = 'netflix.png'; service = $Service; browser = 'edge'; cdpPort = $CdpPort }
$listener = $null
$foreignEdge = $null
try {
    Copy-Item -LiteralPath $appsPath -Destination $backupPath -Force
    Set-Content -LiteralPath $appsPath -Value (@{ apps = @($tile) } | ConvertTo-Json -Depth 5) -Encoding UTF8
    Remove-Item Env:\MEDIASTATION_DEV_HARNESS -ErrorAction SilentlyContinue
    Remove-Item Env:\MEDIASTATION_HARNESS_URL -ErrorAction SilentlyContinue
    # Start-Run sets it only for MediaStation; the script itself (and the Edge it starts in D) runs without it.
    Remove-Item Env:\__COMPAT_LAYER -ErrorAction SilentlyContinue
    Remove-TestProfile

    Invoke-VersionScenario -Scenario 'A' -LastVersion '155.0.4283.45' -ExpectPrevious '155.0.4283.45' -ExpectChanged $true -Screenshot $true
    $written = if (Test-Path -LiteralPath (Join-Path $profileDir 'Last Version')) { (Get-Content -LiteralPath (Join-Path $profileDir 'Last Version') -Raw).Trim() } else { $null }
    Note "Last Version after A: $written"
    if ([string]::IsNullOrEmpty($written)) { Problem 'A2' 'Edge did not write Last Version in A' }
    Invoke-VersionScenario -Scenario 'A2' -LastVersion '' -ExpectPrevious $written -ExpectChanged $false -Screenshot $false
    Invoke-VersionScenario -Scenario 'B' -LastVersion 'garbage' -ExpectPrevious 'unknown' -ExpectChanged $false -Screenshot $false

    Note '=== C ==='
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $CdpPort)
    $listener.Start()
    Invoke-HeldScenario -Scenario 'C' -Reason 'portInUse' -ErrorCode 'portInUse' -ProcessSnapshot { Get-ProfilePids }
    $listener.Stop(); $listener = $null

    Note '=== D ==='
    $startForeignEdge = {
        $script:foreignEdge = Start-Process -FilePath $edgeExe -ArgumentList @("--user-data-dir=`"$profileDir`"", '--no-first-run', '--no-default-browser-check', 'about:blank') -PassThru
        if (-not (Wait-For -Seconds 20 -Condition { Test-LockHeld })) { Problem 'D' 'the script-started Edge did not take the profile lockfile within 20s' }
        # Let the child set settle so the before/after comparison reflects BrowserHost, not Edge's own startup.
        $stable = Get-ProfilePids
        for ($i = 0; $i -lt 30; $i++) {
            Start-Sleep -Seconds 1
            $now = Get-ProfilePids
            if (($now -join ',') -eq ($stable -join ',') -and $i -ge 4) { break }
            $stable = $now
        }
        Note ("script Edge pid {0}, profile processes {1}" -f $script:foreignEdge.Id, ($stable -join ','))
    }
    Invoke-HeldScenario -Scenario 'D' -Reason 'profileInUse' -ErrorCode 'profileInUse' -ProcessSnapshot { Get-ProfilePids } -AfterProbe $startForeignEdge
    $foreignEdge = $null

    Note '=== E ==='
    Remove-TestProfile
    Write-TextFile -Path (Join-Path $profileDir 'Local State') -Text '{"devtools":{"remote_debugging":{"allowed":false}}}'
    $app = $null
    try {
        $app = Start-Run
        Send-Ok -App $app
        if (-not (Wait-Log -App $app -Msg 'appWindowAttached' -Seconds 30)) { Problem 'E' 'no appWindowAttached within 30s' }
        if (-not (Wait-Log -App $app -Msg 'commandFailed' -Seconds ($cdpConnectTimeout + 20))) { Problem 'E' 'no commandFailed in time' }
        [void](Wait-Log -App $app -Msg 'audioRuleApplied' -Seconds 5)
        Start-Sleep -Seconds 2
        $entries = @(Get-RunEntries -ProcessId $app.Id)
        $launch = $entries | Where-Object { $_.msg -eq 'appLaunch' } | Select-Object -First 1
        $attached = $entries | Where-Object { $_.msg -eq 'appWindowAttached' } | Select-Object -First 1
        $unavailable = @($entries | Where-Object { $_.msg -eq 'cdpUnavailable' })
        $failed = @($entries | Where-Object { $_.msg -eq 'commandFailed' })
        $exited = @($entries | Where-Object { $_.msg -eq 'processExited' })
        $version = $entries | Where-Object { $_.msg -eq 'browserVersion' } | Select-Object -First 1
        if ($null -eq $launch -or $null -eq $attached) { throw 'E: missing appLaunch or appWindowAttached.' }
        $rootPid = [int]$attached.data.rootPid
        $window = [IntPtr]([int64]$attached.data.hwnd)
        $mutes = @($entries | Where-Object { $_.msg -eq 'audioRuleApplied' -and [int]$_.data.rootPid -eq $rootPid -and $_.data.muted -eq $true })

        if ($unavailable.Count -ne 1) { Problem 'E' "cdpUnavailable x$($unavailable.Count), expected 1" }
        else {
            Note ('cdpUnavailable ' + ($unavailable[0].data | ConvertTo-Json -Compress))
            $elapsed = ((Get-Ts $unavailable[0]) - (Get-Ts $launch)).TotalSeconds
            Note ("cdpUnavailable {0:N3}s after appLaunch (cdpConnectTimeout {1}s)" -f $elapsed, $cdpConnectTimeout)
            if ($elapsed -lt $cdpConnectTimeout - 0.5) { Problem 'E' "cdpUnavailable only ${elapsed}s after appLaunch" }
            if ([string]::IsNullOrEmpty([string]$unavailable[0].data.browserVersion) -or $unavailable[0].data.browserVersion -eq 'unknown') { Problem 'E' 'cdpUnavailable has no browserVersion' }
            elseif ($null -ne $version -and $unavailable[0].data.browserVersion -ne $version.data.version) { Problem 'E' "cdpUnavailable.browserVersion=$($unavailable[0].data.browserVersion), browserVersion.version=$($version.data.version)" }
            if ((Index-Of $entries 'appWindowAttached') -gt (Index-Of $entries 'cdpUnavailable')) { Problem 'E' 'appWindowAttached is not before cdpUnavailable' }
        }
        if ($failed.Count -ne 1 -or $failed[0].data.errorCode -cne 'cdpUnavailable') { Problem 'E' ('expected one commandFailed {errorCode: cdpUnavailable}, got ' + (@($failed | ForEach-Object { $_.data.errorCode }) -join ',')) }
        if ($exited.Count -ne 0) { Problem 'E' "processExited x$($exited.Count), expected 0" }
        if ($mutes.Count -lt 1) { Problem 'E' "no audioRuleApplied {rootPid: $rootPid, muted: true}" }

        $rootAlive = $null -ne (Get-Process -Id $rootPid -ErrorAction SilentlyContinue)
        [void](Wait-For -Seconds 5 -Condition { $native::IsIconic($window) })
        $shell = Save-Evidence -Stage 'E-after-cdp-unavailable' -Window $window -Extra @{ rootPid = $rootPid; rootAlive = $rootAlive }
        Note ("after E: rootAlive={0} iconic={1} visible={2} foreground pid={3} (MediaStation {4})" -f $rootAlive, $shell.iconic, $shell.visible, $shell.foreground.pid, $app.Id)
        if (-not $rootAlive) { Problem 'E' "Edge root $rootPid is not alive" }
        if (-not ($shell.iconic -and $shell.visible)) { Problem 'E' 'Edge window is not IsIconic and IsWindowVisible' }
        if ($shell.foreground.pid -ne $app.Id) { Problem 'E' 'the shell is not foreground' }
        Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'E'; browserVersion = $version.data; cdpUnavailable = @($unavailable | ForEach-Object { $_.data }); commandFailed = @($failed | ForEach-Object { $_.data }); mutes = @($mutes | ForEach-Object { $_.data }); processExited = $exited.Count })
        Assert-Levels -Scenario 'E' -Entries $entries -Allowed @('cdpUnavailable', 'commandFailed')
    }
    finally { Stop-Run -App $app }
}
catch {
    $problems.Add('run aborted: ' + $_.Exception.Message)
}
finally {
    if ($null -ne $listener) { $listener.Stop() }
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

Write-Host 'PASS: version logged and confirmed (A, A2, B), PortInUse (C), ProfileInUse (D) and CdpUnavailable without ProcessExited (E).' -ForegroundColor Green
