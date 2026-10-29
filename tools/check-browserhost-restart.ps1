<#
.SYNOPSIS
    Story 6.7 check: J4, a Failed browser tile comes back with one OK (AppMenu "Uruchom ponownie"), live on this machine.

.DESCRIPTION
    Uses a throwaway web tile `browserhost-check` (service netflix, cdpPort 9237) with its own profile
    `profiles\browserhost-check`, deleted before and after the run (never `profiles\netflix`). The real `apps.json` is
    copied and put back in `finally`. The published MediaStation runs with the harness off.

    The tile fails deterministically while its process stays alive: the published `page\runtime.js` is patched to
    `ENVELOPE_VERSION = 2`, so every `hello` gives `pageMessageRejected {reason: version}`, `PageVersionRejected` and
    `Failed` (story 6.6). The file is backed up next to itself, restored before P5 and again in `finally`, and its hash is
    checked against the original.

      P1  OK on the tile: `appLaunch` (root R1), `pageVersionRejectedHandled {state: failed}`, `appStateChanged`
          notRunning>starting>foreground>failed; the shell foreground within 5 s, Edge IsIconic. Screenshot.
      P2  OK opens the AppMenu (screenshot, no state change), OK again: `appStateChanged` failed>stopping (RestartApp),
          stopping>starting, starting>foreground, foreground>failed in this order; root R2 != R1, R1 gone, exactly one
          root; no `intentIgnored`; the shell foreground.
      P3  OK (AppMenu, screenshot), Down, OK ("Zamknij"): failed>stopping (CloseApp), stopping>notRunning, no browser
          process of the profile left.
      P4  OK on the NotRunning tile: Failed again as in P1, root R3.
      P5  Runtime restored (hash = backup), then OK, OK: failed>stopping (RestartApp), stopping>starting,
          starting>foreground; root R4 with R3 gone; `pageHello {webdriver: false}` after R4's `appLaunch`; the
          foreground window belongs to R4's tree and is not iconic; no `>failed` within 5 s. Screenshot.
      P6  Home: foreground>background, the shell foreground, Edge IsIconic and IsWindowVisible. Screenshot.
    Only `appIconMissing` and `pageMessageRejected {reason: version}` may appear as warn/error. Long-press OK is never
    used. Evidence (`measurements.jsonl` with root PIDs and the `appStateChanged` sequence, screenshots) goes to
    `logs\browserhost-restart-evidence-<ts>`. Exit code 0 = PASS.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-restart.ps1
#>
param(
    [string]$MediaStation = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [ValidateRange(10, 120)][int]$HelloSeconds = 45
)

$ErrorActionPreference = 'Stop'

$AppId = 'browserhost-check'
$CdpPort = 9237
$VersionLine1 = 'var ENVELOPE_VERSION = 1;'
$VersionLine2 = 'var ENVELOPE_VERSION = 2;'

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe'
}

if (-not ('BrowserRestart.Native' -as [type])) {
    Add-Type -Namespace BrowserRestart -Name Native -MemberDefinition @'
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
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
[DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
[DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
[StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint flags; }
'@
}

$native = [BrowserRestart.Native]
# Physical pixels: without DPI awareness a scaled TV output gives logical metrics and the screenshot shows only a corner.
[void]$native::SetProcessDPIAware()
$problems = New-Object System.Collections.Generic.List[string]
$profilesRoot = Join-Path $env:LOCALAPPDATA 'MediaStation\profiles'
$profileDir = Join-Path $profilesRoot $AppId
$evidenceDir = Join-Path $LogsDir ('browserhost-restart-evidence-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$evidenceJsonl = Join-Path $evidenceDir 'measurements.jsonl'
$configDir = Join-Path $env:LOCALAPPDATA 'MediaStation\config'
$appsPath = Join-Path $configDir 'apps.json'
$backupPath = $appsPath + '.restore-bak'
$runtimePath = Join-Path (Split-Path -Parent $MediaStation) 'page\runtime.js'
$runtimeBackup = $runtimePath + '.restore-bak'
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
        for ($i = $startAt; $i -lt $lines.Count; $i++) {
            if ($i -gt $startAt -and $lines[$i] -match '"msg":"start".*"pid":(\d+)' -and [int]$Matches[1] -ne $ProcessId) { break }
            if ($lines[$i].Trim().Length -gt 0 -and ($Msg.Length -eq 0 -or $lines[$i].Contains('"msg":"' + $Msg + '"'))) {
                try { $entries += ($lines[$i] | ConvertFrom-Json) } catch { }
            }
        }
    }
    return $entries
}

# Phase marker: the number of the run's entries so far; Get-Since returns only what the phase added.
function Get-Mark { param($App) return @(Get-RunEntries -ProcessId $App.Id).Count }

function Get-Since {
    param($App, [int]$Mark, [string]$Msg = '')
    $all = @(Get-RunEntries -ProcessId $App.Id)
    $out = @()
    for ($i = $Mark; $i -lt $all.Count; $i++) {
        if ($Msg.Length -eq 0 -or $all[$i].msg -eq $Msg) { $out += $all[$i] }
    }
    return $out
}

function Index-Of {
    param([object[]]$Entries, [string]$Msg)
    for ($i = 0; $i -lt $Entries.Count; $i++) { if ($Entries[$i].msg -eq $Msg) { return $i } }
    return -1
}

# The tile's `appStateChanged` entries of the phase as `from>to:cause`.
function Get-Transitions {
    param($App, [int]$Mark)
    return @(Get-Since -App $App -Mark $Mark -Msg 'appStateChanged' |
        Where-Object { $_.data.appId -eq $AppId } |
        ForEach-Object { '{0}>{1}:{2}' -f $_.data.from, $_.data.to, $_.data.cause })
}

function Get-Foreground {
    $window = $native::GetForegroundWindow()
    [uint32]$owner = 0
    [void]$native::GetWindowThreadProcessId($window, [ref]$owner)
    return [ordered]@{ hwnd = $window.ToInt64(); pid = [int]$owner }
}

function Get-Geometry {
    param([IntPtr]$Window)
    $rect = New-Object BrowserRestart.Native+RECT
    if (-not $native::IsWindow($Window) -or -not $native::GetWindowRect($Window, [ref]$rect)) { return $null }
    $monitor = $native::MonitorFromWindow($Window, 2)
    $info = New-Object BrowserRestart.Native+MONITORINFO
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
    $record = [ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = $Stage; screenshot = $png; foreground = (Get-Foreground) }
    if ($Window -ne [IntPtr]::Zero) {
        $record.iconic = $native::IsIconic($Window)
        $record.visible = $native::IsWindowVisible($Window)
        $record.geometry = Get-Geometry -Window $Window
    }
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

function Test-Alive { param([int]$ProcessId) return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) }

# The process or one of its ancestors (up to 16 levels) is the root.
function Test-InTree {
    param([int]$ProcessId, [int]$Root)
    $current = $ProcessId
    for ($depth = 0; $depth -lt 16 -and $current -gt 0; $depth++) {
        if ($current -eq $Root) { return $true }
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$current"
        if ($null -eq $process) { return $false }
        $current = [int]$process.ParentProcessId
    }
    return $false
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

# OK on the shell: brings the shell window to the front first (it already is after Failed), then a short Enter press.
function Send-Ok {
    param($App)
    Start-Sleep -Seconds 2
    Set-Front -Handle (Get-ShellHandle -App $App)
    Start-Sleep -Milliseconds 800
    Note 'injected OK'
    Send-Key -Vk 0x0D
}

# A key inside the open AppMenu: the shell is foreground, so no Set-Front (its Alt tap) between menu keys.
function Send-MenuKey {
    param([byte]$Vk, [bool]$Extended = $false, [string]$Name)
    Start-Sleep -Milliseconds 700
    Note "injected $Name"
    Send-Key -Vk $Vk -Extended $Extended
}

function Wait-Since {
    param($App, [int]$Mark, [string]$Msg, [double]$Seconds, [scriptblock]$Where = { $true })
    return Wait-For -Seconds $Seconds -Condition { @(Get-Since -App $App -Mark $Mark -Msg $Msg | Where-Object $Where).Count -ge 1 }
}

function Wait-State {
    param($App, [int]$Mark, [string]$To, [double]$Seconds)
    return Wait-Since -App $App -Mark $Mark -Msg 'appStateChanged' -Seconds $Seconds -Where { $_.data.appId -eq $AppId -and $_.data.to -eq $To }
}

function Test-ShellForeground {
    param($App, [double]$Seconds)
    $started = [DateTimeOffset]::UtcNow
    $ok = Wait-For -Seconds $Seconds -Condition { (Get-Foreground).pid -eq $App.Id }
    return [ordered]@{ ok = $ok; waitedMs = [int]([DateTimeOffset]::UtcNow - $started).TotalMilliseconds }
}

# Compares the phase's `from>to` sequence (cause stripped) with the expected one.
function Assert-Sequence {
    param([string]$Phase, [string[]]$Transitions, [string[]]$Expected)
    $actual = @($Transitions | ForEach-Object { ($_ -split ':')[0] })
    if (($actual -join ' ') -ne ($Expected -join ' ')) {
        Problem ("{0}: appStateChanged [{1}], expected [{2}]" -f $Phase, ($Transitions -join ', '), ($Expected -join ', '))
    }
}

function Assert-Cause {
    param([string]$Phase, [string[]]$Transitions, [string]$Transition, [string]$Cause)
    if (@($Transitions | Where-Object { $_ -eq "${Transition}:${Cause}" }).Count -ne 1) {
        Problem ("{0}: no {1} with cause {2} in [{3}]" -f $Phase, $Transition, $Cause, ($Transitions -join ', '))
    }
}

# The launch of a phase: `appLaunch` pid and the attached root and window.
function Get-Launch {
    param($App, [int]$Mark)
    $attached = Get-Since -App $App -Mark $Mark -Msg 'appWindowAttached' | Select-Object -Last 1
    $launch = Get-Since -App $App -Mark $Mark -Msg 'appLaunch' | Select-Object -Last 1
    if ($null -eq $attached -or $null -eq $launch) { return $null }
    return [ordered]@{ launchPid = [int]$launch.data.pid; root = [int]$attached.data.rootPid; hwnd = [int64]$attached.data.hwnd }
}

function Set-RuntimeVersion {
    param([string]$From, [string]$To)
    $text = [System.IO.File]::ReadAllText($runtimePath, $utf8)
    $count = ([regex]::Matches($text, [regex]::Escape($From))).Count
    if ($count -ne 1) { throw "page\runtime.js has $count lines '$From', expected exactly one." }
    [System.IO.File]::WriteAllText($runtimePath, $text.Replace($From, $To), $utf8)
}

function Restore-Runtime {
    if (Test-Path -LiteralPath $runtimeBackup) { Move-Item -LiteralPath $runtimeBackup -Destination $runtimePath -Force }
}

# Fails P1/P4-style: OK on a launchable tile, the page's v:2 hello fails it while the root stays alive.
function Invoke-FailingLaunch {
    param($App, [string]$Phase)
    $mark = Get-Mark -App $App
    Send-Ok -App $App
    if (-not (Wait-State -App $App -Mark $mark -To 'failed' -Seconds ($HelloSeconds + 15))) { throw "${Phase}: the tile did not reach failed" }
    $launch = Get-Launch -App $App -Mark $mark
    if ($null -eq $launch) { throw "${Phase}: no appLaunch/appWindowAttached" }
    $window = [IntPtr]$launch.hwnd
    $shell = Test-ShellForeground -App $App -Seconds 5
    $iconic = Wait-For -Seconds 5 -Condition { $native::IsIconic($window) }
    $transitions = Get-Transitions -App $App -Mark $mark
    $handled = @(Get-Since -App $App -Mark $mark -Msg 'pageVersionRejectedHandled')
    $roots = Get-ProfileRoots
    Note ("{0}: root {1}, appStateChanged [{2}], shell foreground {3} ({4} ms), iconic {5}" -f $Phase, $launch.root, ($transitions -join ', '), $shell.ok, $shell.waitedMs, $iconic)
    Assert-Sequence -Phase $Phase -Transitions $transitions -Expected @('notRunning>starting', 'starting>foreground', 'foreground>failed')
    Assert-Cause -Phase $Phase -Transitions $transitions -Transition 'foreground>failed' -Cause 'PageVersionRejected'
    if ($handled.Count -lt 1 -or $handled[0].data.state -ne 'failed') { Problem "${Phase}: pageVersionRejectedHandled is not {state: failed}" }
    if (-not $shell.ok) { Problem "${Phase}: the shell is not foreground within 5 s" }
    if (-not $iconic) { Problem "${Phase}: the Edge window is not IsIconic" }
    if ($roots.Count -ne 1 -or $roots[0] -ne $launch.root) { Problem "${Phase}: profile roots [$($roots -join ',')], expected [$($launch.root)]" }
    [void](Save-Evidence -Stage ($Phase.ToLowerInvariant() + '-failed') -Window $window -Extra @{ launch = $launch; roots = $roots; transitions = $transitions; shellForeground = $shell; pageVersionRejectedHandled = @($handled | ForEach-Object { $_.data }) })
    return $launch
}

# OK on the Failed tile opens the AppMenu with no state change; the screenshot shows "Uruchom ponownie" focused.
function Open-AppMenu {
    param($App, [string]$Stage)
    $mark = Get-Mark -App $App
    Send-Ok -App $App
    Start-Sleep -Milliseconds 1200
    $changes = Get-Transitions -App $App -Mark $mark
    if ($changes.Count -ne 0) { Problem "${Stage}: OK on the Failed tile changed the state [$($changes -join ', ')] instead of opening the AppMenu" }
    [void](Save-Evidence -Stage $Stage -Window ([IntPtr]::Zero))
}

if (-not (Test-Path -LiteralPath $MediaStation)) { throw "MediaStation.exe not found: $MediaStation - publish Release first." }
foreach ($asset in @('page\runtime.js', 'page\drivers\netflix\manifest.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $MediaStation) $asset))) { throw "Publish has no $asset next to MediaStation.exe." }
}
if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'MediaStation is already running; close it and retry.' }
if (@(Get-NetTCPConnection -LocalPort $CdpPort -State Listen -ErrorAction SilentlyContinue).Count -gt 0) { throw "Port $CdpPort is already listening; free it and retry." }
if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }
Restore-Runtime

[void](New-Item -ItemType Directory -Path $evidenceDir -Force)
$machine = [ordered]@{ computer = $env:COMPUTERNAME; board = (Get-CimInstance Win32_BaseBoard).Product; model = (Get-CimInstance Win32_ComputerSystem).Model; mediaStation = $MediaStation }
Note ('machine ' + ($machine | ConvertTo-Json -Compress))
Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'machine'; machine = $machine })

$runtimeHash = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash
$tile = [ordered]@{ type = 'web'; id = $AppId; category = 'video'; title = 'BrowserHost check'; icon = 'netflix.png'; service = 'netflix'; browser = 'edge'; cdpPort = $CdpPort }
$app = $null
$roots = [ordered]@{}
try {
    Copy-Item -LiteralPath $appsPath -Destination $backupPath -Force
    Set-Content -LiteralPath $appsPath -Value (@{ apps = @($tile) } | ConvertTo-Json -Depth 5) -Encoding UTF8
    Remove-Item Env:\MEDIASTATION_DEV_HARNESS -ErrorAction SilentlyContinue
    Remove-Item Env:\MEDIASTATION_HARNESS_URL -ErrorAction SilentlyContinue
    Remove-Item Env:\__COMPAT_LAYER -ErrorAction SilentlyContinue
    Remove-TestProfile

    Copy-Item -LiteralPath $runtimePath -Destination $runtimeBackup -Force
    Set-RuntimeVersion -From $VersionLine1 -To $VersionLine2
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'runtime-patched'; runtime = $runtimePath; originalSha256 = $runtimeHash; patchedSha256 = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash })
    Note 'runtime.js patched to ENVELOPE_VERSION = 2'

    $app = Start-Run

    # P1: the first launch fails (the page speaks v:2), the process stays alive.
    Note '=== P1. failure on the first plan ==='
    $r1 = Invoke-FailingLaunch -App $app -Phase 'P1'
    $roots.R1 = $r1.root

    # P2: AppMenu, Uruchom ponownie; the restarted app fails again.
    Note '=== P2. restart that fails again ==='
    Open-AppMenu -App $app -Stage 'p2-appmenu'
    $mark = Get-Mark -App $app
    Send-MenuKey -Vk 0x0D -Name 'OK (Uruchom ponownie)'
    if (-not (Wait-Since -App $app -Mark $mark -Msg 'appStateChanged' -Seconds ($HelloSeconds + 20) -Where { $_.data.appId -eq $AppId -and $_.data.from -eq 'foreground' -and $_.data.to -eq 'failed' })) { Problem 'P2: no foreground>failed after the restart' }
    $r2 = Get-Launch -App $app -Mark $mark
    $transitions = Get-Transitions -App $app -Mark $mark
    $shell = Test-ShellForeground -App $app -Seconds 5
    $current = Get-ProfileRoots
    $ignored = @(Get-Since -App $app -Mark $mark -Msg 'intentIgnored')
    Note ("P2: root {0}, appStateChanged [{1}], roots [{2}], shell foreground {3}" -f $r2.root, ($transitions -join ', '), ($current -join ','), $shell.ok)
    Assert-Sequence -Phase 'P2' -Transitions $transitions -Expected @('failed>stopping', 'stopping>starting', 'starting>foreground', 'foreground>failed')
    Assert-Cause -Phase 'P2' -Transitions $transitions -Transition 'failed>stopping' -Cause 'RestartApp'
    if ($null -eq $r2) { Problem 'P2: no new launch' }
    else {
        $roots.R2 = $r2.root
        if ($r2.root -eq $r1.root) { Problem 'P2: the restart reused root R1' }
        if (Test-Alive -ProcessId $r1.root) { Problem "P2: the old root R1 $($r1.root) is still alive" }
        if ($current.Count -ne 1 -or $current[0] -ne $r2.root) { Problem "P2: profile roots [$($current -join ',')], expected exactly [$($r2.root)]" }
    }
    if ($ignored.Count -ne 0) { Problem "P2: intentIgnored x$($ignored.Count)" }
    if (-not $shell.ok) { Problem 'P2: the shell is not foreground after the second failure' }
    $p2Window = if ($null -ne $r2) { [IntPtr]$r2.hwnd } else { [IntPtr]::Zero }
    [void](Save-Evidence -Stage 'p2-failed-again' -Window $p2Window -Extra @{ launch = $r2; roots = $current; transitions = $transitions; shellForeground = $shell })

    # P3: AppMenu, Down, Zamknij.
    Note '=== P3. Zamknij ==='
    Open-AppMenu -App $app -Stage 'p3-appmenu'
    $mark = Get-Mark -App $app
    Send-MenuKey -Vk 0x28 -Extended $true -Name 'Down'
    Send-MenuKey -Vk 0x0D -Name 'OK (Zamknij)'
    if (-not (Wait-State -App $app -Mark $mark -To 'notRunning' -Seconds 20)) { Problem 'P3: the tile did not reach notRunning' }
    $gone = Wait-For -Seconds 10 -Condition { (Get-ProfileProcesses).Count -eq 0 }
    $transitions = Get-Transitions -App $app -Mark $mark
    Note ("P3: appStateChanged [{0}], profile processes gone {1}" -f ($transitions -join ', '), $gone)
    Assert-Sequence -Phase 'P3' -Transitions $transitions -Expected @('failed>stopping', 'stopping>notRunning')
    Assert-Cause -Phase 'P3' -Transitions $transitions -Transition 'failed>stopping' -Cause 'CloseApp'
    if (-not $gone) { Problem "P3: profile processes left: $(@(Get-ProfileProcesses | ForEach-Object { $_.ProcessId }) -join ',')" }
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'p3-closed'; transitions = $transitions; profileProcessesGone = $gone })

    # P4: OK on the NotRunning tile fails again, as in P1.
    Note '=== P4. launch from notRunning ==='
    $r3 = Invoke-FailingLaunch -App $app -Phase 'P4'
    $roots.R3 = $r3.root

    # P5: the runtime is repaired, so the restart brings the tile to the foreground.
    Note '=== P5. restart after the repair ==='
    Restore-Runtime
    $restoredHash = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash
    Note ("runtime.js restored, hash equal {0}" -f ($restoredHash -eq $runtimeHash))
    if ($restoredHash -ne $runtimeHash) { throw 'P5: restored runtime.js differs from the backup' }
    Open-AppMenu -App $app -Stage 'p5-appmenu'
    $mark = Get-Mark -App $app
    Send-MenuKey -Vk 0x0D -Name 'OK (Uruchom ponownie)'
    if (-not (Wait-State -App $app -Mark $mark -To 'foreground' -Seconds 30)) { Problem 'P5: the tile did not reach foreground' }
    $r4 = Get-Launch -App $app -Mark $mark
    $hello = Wait-Since -App $app -Mark $mark -Msg 'pageHello' -Seconds $HelloSeconds -Where { $_.data.webdriver -eq $false }
    $window = if ($null -ne $r4) { [IntPtr]$r4.hwnd } else { [IntPtr]::Zero }
    $edgeFront = Wait-For -Seconds 10 -Condition { $null -ne $r4 -and (Test-InTree -ProcessId (Get-Foreground).pid -Root $r4.root) }
    Start-Sleep -Seconds 5
    $since = @(Get-Since -App $app -Mark $mark)
    $transitions = Get-Transitions -App $app -Mark $mark
    $current = Get-ProfileRoots
    Note ("P5: root {0}, appStateChanged [{1}], pageHello {2}, Edge foreground {3}, roots [{4}]" -f $r4.root, ($transitions -join ', '), $hello, $edgeFront, ($current -join ','))
    Assert-Sequence -Phase 'P5' -Transitions $transitions -Expected @('failed>stopping', 'stopping>starting', 'starting>foreground')
    Assert-Cause -Phase 'P5' -Transitions $transitions -Transition 'failed>stopping' -Cause 'RestartApp'
    if ($null -eq $r4) { Problem 'P5: no new launch' }
    else {
        $roots.R4 = $r4.root
        if ($r4.root -eq $r3.root) { Problem 'P5: the restart reused root R3' }
        if (Test-Alive -ProcessId $r3.root) { Problem "P5: the old root R3 $($r3.root) is still alive" }
        if ($current.Count -ne 1 -or $current[0] -ne $r4.root) { Problem "P5: profile roots [$($current -join ',')], expected exactly [$($r4.root)]" }
        if ($native::IsIconic($window)) { Problem 'P5: the Edge window is iconic' }
    }
    if (-not $hello) { Problem 'P5: no pageHello {webdriver: false}' }
    elseif ((Index-Of -Entries $since -Msg 'pageHello') -lt (Index-Of -Entries $since -Msg 'appLaunch')) { Problem "P5: pageHello before R4's appLaunch" }
    if (-not $edgeFront) { Problem "P5: the foreground window is not in R4's tree ($((Get-Foreground) | ConvertTo-Json -Compress))" }
    if (@($transitions | Where-Object { $_ -like '*>failed:*' }).Count -ne 0) { Problem 'P5: the tile failed again after the repair' }
    if (@($since | Where-Object { $_.msg -eq 'pageMessageRejected' }).Count -ne 0) { Problem 'P5: pageMessageRejected after the repair' }
    [void](Save-Evidence -Stage 'p5-foreground' -Window $window -Extra @{ launch = $r4; roots = $current; transitions = $transitions; pageHello = @($since | Where-Object { $_.msg -eq 'pageHello' } | ForEach-Object { $_.data }) })

    # P6: Home sends the restarted app to the background.
    Note '=== P6. Home ==='
    $mark = Get-Mark -App $app
    Note 'injected Home'
    Send-Key -Vk 0x24 -Extended $true
    if (-not (Wait-State -App $app -Mark $mark -To 'background' -Seconds 10)) { Problem 'P6: the tile did not reach background' }
    $shell = Test-ShellForeground -App $app -Seconds 5
    $iconic = Wait-For -Seconds 5 -Condition { $native::IsIconic($window) }
    $transitions = Get-Transitions -App $app -Mark $mark
    $shot = Save-Evidence -Stage 'p6-home' -Window $window -Extra @{ transitions = $transitions; shellForeground = $shell }
    Note ("P6: appStateChanged [{0}], shell foreground {1}, iconic {2}, visible {3}" -f ($transitions -join ', '), $shell.ok, $shot.iconic, $shot.visible)
    Assert-Sequence -Phase 'P6' -Transitions $transitions -Expected @('foreground>background')
    if (-not $shell.ok) { Problem 'P6: the shell is not foreground after Home' }
    if (-not ($iconic -and $shot.visible)) { Problem 'P6: the Edge window is not IsIconic and IsWindowVisible' }

    # The whole run: only the expected warnings, and the full appStateChanged sequence for the record.
    $entries = @(Get-RunEntries -ProcessId $app.Id)
    foreach ($entry in @($entries | Where-Object { $_.level -in @('warn', 'error') })) {
        $expected = $entry.msg -eq 'appIconMissing' -or ($entry.msg -eq 'pageMessageRejected' -and $entry.data.reason -eq 'version')
        if (-not $expected) { Problem ("unexpected log {0}: {1} {2}" -f $entry.level, $entry.msg, ($entry.data | ConvertTo-Json -Compress -Depth 4)) }
    }
    $all = @(Get-Transitions -App $app -Mark 0)
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'summary'; roots = $roots; appStateChanged = $all; problems = @($problems) })
}
catch {
    $problems.Add('run aborted: ' + $_.Exception.Message)
}
finally {
    if ($null -ne $app -and -not $app.HasExited) {
        [void]$app.CloseMainWindow()
        if (-not $app.WaitForExit(8000)) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    }
    if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) { Get-Process -Name 'MediaStation' | Stop-Process -Force -ErrorAction SilentlyContinue }
    try { Remove-TestProfile } catch { $problems.Add('cleanup: ' + $_.Exception.Message) }
    if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }
    try {
        Restore-Runtime
        $finalHash = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash
        if ($finalHash -ne $runtimeHash) { $problems.Add("cleanup: page\runtime.js hash $finalHash differs from the original $runtimeHash") }
        Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'runtime-restored'; sha256 = $finalHash; equal = ($finalHash -eq $runtimeHash) })
    } catch { $problems.Add('cleanup runtime: ' + $_.Exception.Message) }
}

Write-Host ''
Write-Host ('Evidence: ' + $evidenceDir)
if ($problems.Count -gt 0) {
    Write-Host ('FAIL ({0} problems)' -f $problems.Count) -ForegroundColor Red
    $problems | ForEach-Object { Write-Host ('  - ' + $_) }
    exit 1
}

Write-Host 'PASS: J4 - a Failed tile restarted with one OK from the AppMenu, a failed restart landed in Failed again, Zamknij closed it, and after the repair the restart brought a new root to the foreground.' -ForegroundColor Green
