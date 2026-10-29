<#
.SYNOPSIS
    Story 6.2 check: a web tile starts Edge through the real BrowserHost (kiosk, raw CDP on loopback, page hello).

.DESCRIPTION
    Starts the published MediaStation WITHOUT the dev harness and with a generated `apps.json` holding one web tile
    (the real file is copied and put back in `finally`). Then:
      1. injects OK on the shell: the log must have `appLaunch` (args with `--kiosk` and `--remote-debugging-port=<port>`,
         without `--remote-allow-origins`), `appWindowAttached`, `cdpConnected` and `pageHello` with `webdriver=false`;
         the Edge window must be foreground and cover its monitor;
      2. checks the browser: TCP port <port> listens only on 127.0.0.1 and the WMI command line has no
         `--remote-allow-origins`;
      3. injects Home: the MediaStation shell must be foreground, the Edge window IsIconic and IsWindowVisible (no SW_HIDE).
    Screenshots after start and after Home plus `measurements.jsonl` go to `logs\browserhost-tracer-evidence-<ts>`.
    The verdict also requires no `warn`/`error` in the run except `appIconMissing`. MediaStation and the Edge tree are
    closed at the end. Exit code 0 = PASS.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-tracer.ps1
#>
param(
    [string]$AppId = 'netflix',
    [ValidateRange(9220, 9239)][int]$CdpPort = 9222,
    [string]$MediaStation = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [ValidateRange(10, 120)][int]$HelloSeconds = 45
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe'
}

if (-not ('BrowserTracer.Native' -as [type])) {
    Add-Type -Namespace BrowserTracer -Name Native -MemberDefinition @'
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

$native = [BrowserTracer.Native]
$problems = New-Object System.Collections.Generic.List[string]
$allowedWarnings = @('appIconMissing')
$profileDir = Join-Path $env:LOCALAPPDATA ('MediaStation\profiles\' + $AppId)
$evidenceDir = Join-Path $LogsDir ('browserhost-tracer-evidence-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$evidenceJsonl = Join-Path $evidenceDir 'measurements.jsonl'
$appsPath = Join-Path (Join-Path $env:LOCALAPPDATA 'MediaStation\config') 'apps.json'
$backupPath = $appsPath + '.restore-bak'

function Add-Jsonl {
    param([object]$Record)
    # UTF-8 without BOM: Add-Content in Windows PowerShell writes ANSI and mangles non-ASCII paths.
    [System.IO.File]::AppendAllText($evidenceJsonl, ($Record | ConvertTo-Json -Compress -Depth 8) + "`n", (New-Object System.Text.UTF8Encoding $false))
}

function Note { param([string]$Message) Write-Host ('{0} {1}' -f [DateTimeOffset]::UtcNow.ToString('HH:mm:ss.fff'), $Message) }

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

function Get-ForegroundPid {
    $window = $native::GetForegroundWindow()
    [uint32]$owner = 0
    [void]$native::GetWindowThreadProcessId($window, [ref]$owner)
    return [ordered]@{ hwnd = $window.ToInt64(); pid = [int]$owner }
}

function Get-Geometry {
    param([IntPtr]$Window)
    $rect = New-Object BrowserTracer.Native+RECT
    if (-not $native::IsWindow($Window) -or -not $native::GetWindowRect($Window, [ref]$rect)) { throw "GetWindowRect failed for $Window" }
    $monitor = $native::MonitorFromWindow($Window, 2)
    $info = New-Object BrowserTracer.Native+MONITORINFO
    $info.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($info)
    if (-not $native::GetMonitorInfo($monitor, [ref]$info)) { throw 'GetMonitorInfo failed.' }
    $covers = ($rect.left -le $info.rcMonitor.left -and $rect.top -le $info.rcMonitor.top -and
        $rect.right -ge $info.rcMonitor.right -and $rect.bottom -ge $info.rcMonitor.bottom)
    return [ordered]@{ hwnd = $Window.ToInt64(); left = $rect.left; top = $rect.top; right = $rect.right; bottom = $rect.bottom; monitorLeft = $info.rcMonitor.left; monitorTop = $info.rcMonitor.top; monitorRight = $info.rcMonitor.right; monitorBottom = $info.rcMonitor.bottom; coversMonitor = $covers }
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
    foreach ($root in @(Get-ProfileProcesses | Where-Object { $_.CommandLine -notmatch '--type=' })) { & taskkill.exe /PID $root.ProcessId /T /F 2>&1 | Out-Null }
    [void](Wait-For -Seconds 10 -Condition { (Get-ProfileProcesses).Count -eq 0 })
}

if (-not (Test-Path -LiteralPath $MediaStation)) { throw "MediaStation.exe not found: $MediaStation - publish Release first." }
foreach ($asset in @('page\runtime.js', "page\drivers\$AppId\manifest.json")) {
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $MediaStation) $asset))) { throw "Publish has no $asset next to MediaStation.exe." }
}
if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'MediaStation is already running; close it and retry.' }
if ((Get-ProfileProcesses).Count -gt 0) { throw "A browser already holds $profileDir; close it and retry." }
if (@(Get-NetTCPConnection -LocalPort $CdpPort -State Listen -ErrorAction SilentlyContinue).Count -gt 0) { throw "Port $CdpPort is already listening; free it and retry." }
if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }

[void](New-Item -ItemType Directory -Path $evidenceDir -Force)
$tile = [ordered]@{ type = 'web'; id = $AppId; category = 'video'; title = 'Netflix'; icon = "$AppId.png"; service = 'netflix'; browser = 'edge'; cdpPort = $CdpPort }
$app = $null
try {
    Copy-Item -LiteralPath $appsPath -Destination $backupPath -Force
    Set-Content -LiteralPath $appsPath -Value (@{ apps = @($tile) } | ConvertTo-Json -Depth 5) -Encoding UTF8
    Remove-Item Env:\MEDIASTATION_DEV_HARNESS -ErrorAction SilentlyContinue
    Remove-Item Env:\MEDIASTATION_HARNESS_URL -ErrorAction SilentlyContinue

    $app = Start-Process -FilePath $MediaStation -PassThru
    Note ("MediaStation pid {0}" -f $app.Id)
    if (-not (Wait-For -Seconds 30 -Condition { $app.Refresh(); $app.MainWindowHandle -ne 0 })) { throw 'MediaStation window did not appear within 30s.' }
    if (-not (Wait-For -Seconds 30 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'snapshotPublished').Count -ge 1 })) { throw 'No snapshotPublished within 30s.' }
    if (@(Get-RunEntries -ProcessId $app.Id -Msg 'browserHostEnabled').Count -ne 1) { $problems.Add('no browserHostEnabled entry: BrowserHost is not wired') }
    if (@(Get-RunEntries -ProcessId $app.Id -Msg 'devHarnessEnabled').Count -ne 0) { $problems.Add('dev harness is enabled') }

    $shell = [IntPtr]$app.MainWindowHandle
    $managed = @(Get-RunEntries -ProcessId $app.Id -Msg 'managedShell') | Select-Object -Last 1
    if ($null -ne $managed) { $shell = [IntPtr]([int64]$managed.data.hwnd) }

    Start-Sleep -Seconds 2
    Set-Front -Handle $shell
    Start-Sleep -Milliseconds 800
    Note 'injected OK'
    Send-Key -Vk 0x0D

    if (-not (Wait-For -Seconds 30 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'appWindowAttached').Count -ge 1 })) {
        foreach ($f in @(Get-RunEntries -ProcessId $app.Id | Where-Object { $_.level -in @('warn', 'error') })) { Note ('  log {0}: {1}' -f $f.msg, ($f.data | ConvertTo-Json -Compress -Depth 4)) }
        throw 'No appWindowAttached within 30s.'
    }
    $attached = @(Get-RunEntries -ProcessId $app.Id -Msg 'appWindowAttached')[0]
    $window = [IntPtr]([int64]$attached.data.hwnd)
    $rootPid = [int]$attached.data.rootPid
    Note ("appWindowAttached hwnd=0x{0:X} rootPid={1}" -f $window.ToInt64(), $rootPid)

    $launch = @(Get-RunEntries -ProcessId $app.Id -Msg 'appLaunch')[0]
    if ($null -eq $launch) { $problems.Add('no appLaunch entry') }
    else {
        $launchArgs = @($launch.data.args)
        Note ('appLaunch args: ' + ($launchArgs -join ' '))
        if ($launch.src -ne 'browser') { $problems.Add("appLaunch src is '$($launch.src)', expected 'browser'") }
        if ($launchArgs -notcontains '--kiosk') { $problems.Add('appLaunch args lack --kiosk') }
        if ($launchArgs -notcontains "--remote-debugging-port=$CdpPort") { $problems.Add("appLaunch args lack --remote-debugging-port=$CdpPort") }
        if (@($launchArgs | Where-Object { $_ -like '--remote-allow-origins*' }).Count -gt 0) { $problems.Add('appLaunch args contain --remote-allow-origins') }
    }

    if (-not (Wait-For -Seconds 20 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'cdpConnected').Count -ge 1 })) { $problems.Add('no cdpConnected within 20s') }
    $helloOk = Wait-For -Seconds $HelloSeconds -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'pageHello').Count -ge 1 }
    $hellos = @(Get-RunEntries -ProcessId $app.Id -Msg 'pageHello')
    if (-not $helloOk) { $problems.Add("no pageHello within ${HelloSeconds}s") }
    foreach ($hello in $hellos) {
        Note ('pageHello ' + ($hello.data | ConvertTo-Json -Compress))
        if ($hello.data.webdriver -ne $false) { $problems.Add("pageHello webdriver=$($hello.data.webdriver), expected false") }
        if ($hello.data.service -ne 'netflix') { $problems.Add("pageHello service=$($hello.data.service)") }
    }

    # Foreground after start: ActivateWindow from the Orchestrator, Edge on top covering the monitor.
    $onTop = Wait-For -Seconds 10 -Condition { $native::GetForegroundWindow() -eq $window }
    Start-Sleep -Seconds 2
    $start = Save-Evidence -Stage 'after-start' -Window $window
    Note ("after start: foreground pid={0} onTop={1} covers={2} iconic={3}" -f $start.foreground.pid, $onTop, $start.geometry.coversMonitor, $start.iconic)
    if (-not $onTop) { $problems.Add('Edge window is not foreground after start') }
    if (-not $start.geometry.coversMonitor) { $problems.Add('Edge window does not cover its monitor') }
    if ($start.iconic) { $problems.Add('Edge window is minimized after start') }

    # Loopback-only listener and command line of the running browser.
    $listeners = @(Get-NetTCPConnection -LocalPort $CdpPort -State Listen -ErrorAction SilentlyContinue)
    $addresses = @($listeners | ForEach-Object { $_.LocalAddress } | Sort-Object -Unique)
    $owners = @($listeners | ForEach-Object { [int]$_.OwningProcess } | Sort-Object -Unique)
    Note ("port ${CdpPort} listeners: " + ($addresses -join ',') + ' owners: ' + ($owners -join ','))
    if ($listeners.Count -eq 0) { $problems.Add("nothing listens on port $CdpPort") }
    if (@($addresses | Where-Object { $_ -ne '127.0.0.1' }).Count -gt 0) { $problems.Add("port $CdpPort listens on non-loopback: $($addresses -join ',')") }
    if ($owners.Count -gt 0 -and $owners -notcontains $rootPid) { $problems.Add("port $CdpPort is not owned by the browser root $rootPid") }
    $rootLine = (Get-CimInstance Win32_Process -Filter "ProcessId=$rootPid").CommandLine
    $profileLines = @(Get-ProfileProcesses | ForEach-Object { $_.CommandLine })
    if ($rootLine -notmatch '--kiosk') { $problems.Add('browser root command line lacks --kiosk') }
    if (@($profileLines | Where-Object { $_ -match '--remote-allow-origins' }).Count -gt 0) { $problems.Add('a browser command line contains --remote-allow-origins') }
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'browser'; listenAddresses = $addresses; listenOwners = $owners; rootPid = $rootPid; rootCommandLine = $rootLine })

    # Home: shell on top, Edge minimized but visible (never SW_HIDE).
    Note 'injected Home'
    Send-Key -Vk 0x24 -Extended $true
    $shellOnTop = Wait-For -Seconds 10 -Condition { (Get-ForegroundPid).pid -eq $app.Id }
    [void](Wait-For -Seconds 5 -Condition { $native::IsIconic($window) })
    Start-Sleep -Seconds 2
    $afterHome = Save-Evidence -Stage 'after-home' -Window $window
    Note ("after Home: foreground pid={0} (MediaStation {1}) iconic={2} visible={3}" -f $afterHome.foreground.pid, $app.Id, $afterHome.iconic, $afterHome.visible)
    if (-not $shellOnTop -or $afterHome.foreground.pid -ne $app.Id) { $problems.Add('shell is not foreground after Home') }
    if (-not ($afterHome.iconic -and $afterHome.visible)) { $problems.Add('Edge window is not minimized and visible after Home') }

    $entries = @(Get-RunEntries -ProcessId $app.Id)
    $badLevel = @($entries | Where-Object { $_.level -in @('warn', 'error') -and $allowedWarnings -notcontains $_.msg })
    foreach ($entry in $badLevel) { $problems.Add("log $($entry.level): $($entry.msg) $($entry.data | ConvertTo-Json -Compress -Depth 4)") }
    $exited = @($entries | Where-Object { $_.msg -eq 'processExited' }).Count
    if ($exited -ne 0) { $problems.Add("processExited x$exited, expected 0") }
}
catch {
    $problems.Add('run aborted: ' + $_.Exception.Message)
}
finally {
    if ($null -ne $app) {
        [void]$app.CloseMainWindow()
        if (-not $app.WaitForExit(8000)) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    }
    Stop-Profile
    if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }
}

Write-Host ''
Write-Host ('Evidence: ' + $evidenceDir)
if ($problems.Count -gt 0) {
    Write-Host ('FAIL ({0} problems)' -f $problems.Count) -ForegroundColor Red
    $problems | ForEach-Object { Write-Host ('  - ' + $_) }
    exit 1
}

Write-Host 'PASS: BrowserHost starts Edge in kiosk with loopback CDP, the page says hello (webdriver=false), Home minimizes without SW_HIDE.' -ForegroundColor Green
