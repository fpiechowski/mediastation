<#
.SYNOPSIS
    Story 6.3 check: a lost CDP page target with a live Edge ends in `cdpLost` -> `Failed` after cdpReconnectTimeout,
    and Home keeps returning to the shell before and after the limit.

.DESCRIPTION
    Starts the published MediaStation WITHOUT the dev harness and with a generated `apps.json` holding one web tile
    (the real file is copied and put back in `finally`). Then:
      1. injects OK on the shell and waits for `appWindowAttached`, `cdpConnected` and `pageHello`;
      2. takes the host's page target from `/json/list` (the only `page` target), opens a second target first
         (`PUT /json/new?about:blank`, fallback browser-level `Target.createTarget`) so Edge stays alive, and closes
         the original one (`/json/close/<id>`);
      3. expects `cdpClosed` (with `timeoutSeconds`), injects Home before the limit: the MediaStation shell must be
         foreground within 2 s;
      4. expects `cdpLost` at cdpReconnectTimeout (+-2 s after `cdpClosed`), `cdpLostHandled` with `state: failed`, no
         `cdpReconnected`, no `processExited`, the browser root still alive; injects Home again: shell foreground
         within 2 s.
    Screenshots before and after the limit plus `measurements.jsonl` go to `logs\browserhost-reconnect-evidence-<ts>`.
    The verdict also requires no `warn`/`error` in the run except `appIconMissing` and `cdpLost`. MediaStation and
    the Edge tree are closed at the end. Exit code 0 = PASS.

    The recovery path (socket drop with the target still there) needs an elevated TCP reset and is covered by the
    BrowserHost unit tests and the connector test on a real loopback socket.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-reconnect.ps1
#>
param(
    [string]$AppId = 'netflix',
    [ValidateRange(9220, 9239)][int]$CdpPort = 9222,
    [string]$MediaStation = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [ValidateRange(10, 120)][int]$HelloSeconds = 45,
    [ValidateRange(5, 600)][int]$ReconnectTimeoutSeconds = 30,
    [ValidateRange(1, 25)][int]$HomeBeforeLimitSeconds = 10
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe'
}

if (-not ('BrowserReconnect.Native' -as [type])) {
    Add-Type -Namespace BrowserReconnect -Name Native -MemberDefinition @'
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
'@
}

$native = [BrowserReconnect.Native]
# Physical pixels: without DPI awareness a scaled TV output gives logical metrics and the screenshot shows only a corner.
[void]$native::SetProcessDPIAware()
$problems = New-Object System.Collections.Generic.List[string]
$allowedWarnings = @('appIconMissing', 'cdpLost')
$profileDir = Join-Path $env:LOCALAPPDATA ('MediaStation\profiles\' + $AppId)
$evidenceDir = Join-Path $LogsDir ('browserhost-reconnect-evidence-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$evidenceJsonl = Join-Path $evidenceDir 'measurements.jsonl'
$appsPath = Join-Path (Join-Path $env:LOCALAPPDATA 'MediaStation\config') 'apps.json'
$backupPath = $appsPath + '.restore-bak'
$cdpBase = "http://127.0.0.1:$CdpPort"

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
    $alive = $native::IsWindow($Window)
    $record = [ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = $Stage; screenshot = $png; foreground = (Get-ForegroundPid); browserWindow = $Window.ToInt64(); browserWindowAlive = $alive; iconic = ($alive -and $native::IsIconic($Window)); visible = ($alive -and $native::IsWindowVisible($Window)) }
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

function New-CdpClient {
    # Without a proxy: CDP lives on loopback only.
    $client = New-Object System.Net.WebClient
    $client.Proxy = $null
    return $client
}

function Get-PageTargets {
    $client = New-CdpClient
    try { $json = $client.DownloadString("$cdpBase/json/list") } finally { $client.Dispose() }
    return @(($json | ConvertFrom-Json) | Where-Object { $_.type -eq 'page' -and -not ([string]$_.url).StartsWith('devtools://') })
}

function Invoke-BrowserCdp {
    # One call on the browser-level target (/json/version), used only when /json/new is not available.
    param([string]$Method, [hashtable]$Params)
    $client = New-CdpClient
    try { $version = $client.DownloadString("$cdpBase/json/version") | ConvertFrom-Json } finally { $client.Dispose() }
    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $socket.Options.Proxy = $null
    $cts = New-Object System.Threading.CancellationTokenSource 10000
    try {
        $socket.ConnectAsync([Uri]$version.webSocketDebuggerUrl, $cts.Token).GetAwaiter().GetResult()
        $bytes = [System.Text.Encoding]::UTF8.GetBytes((@{ id = 1; method = $Method; params = $Params } | ConvertTo-Json -Compress -Depth 5))
        $socket.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
        $buffer = New-Object byte[] 65536
        $result = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $cts.Token).GetAwaiter().GetResult()
        return ([System.Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count) | ConvertFrom-Json)
    }
    finally { $socket.Abort(); $socket.Dispose(); $cts.Dispose() }
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

    # 1. Start the tile and wait for the page's hello.
    Start-Sleep -Seconds 2
    Set-Front -Handle $shell
    Start-Sleep -Milliseconds 800
    Note 'injected OK'
    Send-Key -Vk 0x0D

    if (-not (Wait-For -Seconds 30 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'appWindowAttached').Count -ge 1 })) { throw 'No appWindowAttached within 30s.' }
    $attached = @(Get-RunEntries -ProcessId $app.Id -Msg 'appWindowAttached')[0]
    $window = [IntPtr]([int64]$attached.data.hwnd)
    $rootPid = [int]$attached.data.rootPid
    Note ("appWindowAttached hwnd=0x{0:X} rootPid={1}" -f $window.ToInt64(), $rootPid)
    if (-not (Wait-For -Seconds 20 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'cdpConnected').Count -ge 1 })) { throw 'No cdpConnected within 20s.' }
    if (-not (Wait-For -Seconds $HelloSeconds -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'pageHello').Count -ge 1 })) { throw "No pageHello within ${HelloSeconds}s." }
    Note ('pageHello ' + (@(Get-RunEntries -ProcessId $app.Id -Msg 'pageHello')[0].data | ConvertTo-Json -Compress))

    # 2. The host's target is the only page target; open a second one first so Edge stays alive, then close it.
    $pages = @(Get-PageTargets)
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'targets-before'; targets = @($pages | ForEach-Object { [ordered]@{ id = $_.id; url = $_.url } }) })
    if ($pages.Count -ne 1) { throw "Expected exactly one page target before the drop, found $($pages.Count)." }
    $original = $pages[0]
    Note ("host target id={0} url={1}" -f $original.id, $original.url)

    $opened = 'json/new'
    try {
        $client = New-CdpClient
        try { [void]$client.UploadString("$cdpBase/json/new?about:blank", 'PUT', '') } finally { $client.Dispose() }
    }
    catch {
        Note ("PUT /json/new failed ({0}); falling back to Target.createTarget" -f $_.Exception.Message)
        $opened = 'Target.createTarget'
        $created = Invoke-BrowserCdp -Method 'Target.createTarget' -Params @{ url = 'about:blank' }
        if ($null -eq $created.result.targetId) { throw ('Target.createTarget failed: ' + ($created | ConvertTo-Json -Compress -Depth 5)) }
    }
    if (-not (Wait-For -Seconds 10 -Condition { @(Get-PageTargets | Where-Object { $_.id -ne $original.id }).Count -ge 1 })) { throw 'The second page target did not appear.' }
    Note ("second target opened via $opened")

    $client = New-CdpClient
    try { $closeReply = $client.DownloadString("$cdpBase/json/close/$($original.id)") } finally { $client.Dispose() }
    $closedUtc = [DateTimeOffset]::UtcNow
    Note ("closed host target: $closeReply")
    $after = @(Get-PageTargets)
    Add-Jsonl -Record ([ordered]@{ utc = $closedUtc.ToString('o'); stage = 'target-closed'; opened = $opened; closedId = $original.id; targets = @($after | ForEach-Object { [ordered]@{ id = $_.id; url = $_.url } }) })

    # 3. cdpClosed, then Home before the limit.
    if (-not (Wait-For -Seconds 10 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'cdpClosed').Count -ge 1 })) { throw 'No cdpClosed within 10s of closing the target.' }
    $cdpClosed = @(Get-RunEntries -ProcessId $app.Id -Msg 'cdpClosed')[0]
    $closedTs = [DateTimeOffset]::Parse($cdpClosed.ts)
    Note ('cdpClosed ' + ($cdpClosed.data | ConvertTo-Json -Compress))
    if ([double]$cdpClosed.data.timeoutSeconds -ne $ReconnectTimeoutSeconds) { $problems.Add("cdpClosed timeoutSeconds=$($cdpClosed.data.timeoutSeconds), expected $ReconnectTimeoutSeconds") }

    $homeAt = $closedTs.AddSeconds($HomeBeforeLimitSeconds)
    while ([DateTimeOffset]::UtcNow -lt $homeAt) { Start-Sleep -Milliseconds 100 }
    if (@(Get-RunEntries -ProcessId $app.Id -Msg 'cdpLost').Count -ne 0) { $problems.Add('cdpLost before the Home that should precede the limit') }
    Note 'injected Home (before the limit)'
    $homeSent = [DateTimeOffset]::UtcNow
    Send-Key -Vk 0x24 -Extended $true
    $shellBefore = Wait-For -Seconds 2 -Condition { (Get-ForegroundPid).pid -eq $app.Id }
    $shellBeforeMs = [int]([DateTimeOffset]::UtcNow - $homeSent).TotalMilliseconds
    Start-Sleep -Milliseconds 500
    $before = Save-Evidence -Stage 'before-limit' -Window $window -Extra @{ shellForegroundWithin2s = $shellBefore; shellForegroundMs = $shellBeforeMs }
    Note ("before limit: shell foreground={0} ({1} ms), foreground pid={2}" -f $shellBefore, $shellBeforeMs, $before.foreground.pid)
    if (-not $shellBefore) { $problems.Add('shell is not foreground within 2 s of Home before the limit') }

    # 4. cdpLost at the limit, Failed in the Orchestrator, Edge still alive; Home again.
    if (-not (Wait-For -Seconds ($ReconnectTimeoutSeconds + 10) -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'cdpLostHandled').Count -ge 1 })) { throw 'No cdpLostHandled within the limit + 10 s.' }
    $lost = @(Get-RunEntries -ProcessId $app.Id -Msg 'cdpLost')
    $handled = @(Get-RunEntries -ProcessId $app.Id -Msg 'cdpLostHandled')
    if ($lost.Count -ne 1) { $problems.Add("cdpLost x$($lost.Count), expected 1") }
    if ($handled.Count -ne 1) { $problems.Add("cdpLostHandled x$($handled.Count), expected 1") }
    $lostAfter = if ($lost.Count -ge 1) { ([DateTimeOffset]::Parse($lost[0].ts) - $closedTs).TotalSeconds } else { -1 }
    Note ("cdpLost {0} after {1:N2} s; cdpLostHandled {2}" -f ($lost[0].data | ConvertTo-Json -Compress), $lostAfter, ($handled[0].data | ConvertTo-Json -Compress))
    if ([Math]::Abs($lostAfter - $ReconnectTimeoutSeconds) -gt 2) { $problems.Add(("cdpLost {0:N2} s after cdpClosed, expected {1} +- 2 s" -f $lostAfter, $ReconnectTimeoutSeconds)) }
    if ($handled.Count -ge 1 -and $handled[0].data.state -ne 'failed') { $problems.Add("cdpLostHandled state=$($handled[0].data.state), expected failed") }
    if (@(Get-RunEntries -ProcessId $app.Id -Msg 'cdpReconnected').Count -ne 0) { $problems.Add('cdpReconnected logged although the target is gone') }
    $rootAlive = $null -ne (Get-Process -Id $rootPid -ErrorAction SilentlyContinue)
    if (-not $rootAlive) { $problems.Add('browser root exited; the check needs a live process') }

    Start-Sleep -Seconds 1
    $limit = Save-Evidence -Stage 'after-limit' -Window $window -Extra @{ cdpLostAfterSeconds = $lostAfter; rootAlive = $rootAlive; handledState = $handled[0].data.state }
    Note ("after limit: foreground pid={0} (MediaStation {1}) rootAlive={2}" -f $limit.foreground.pid, $app.Id, $rootAlive)
    if ($limit.foreground.pid -ne $app.Id) { $problems.Add('shell is not foreground after the limit') }

    Note 'injected Home (after the limit)'
    $homeSent = [DateTimeOffset]::UtcNow
    Send-Key -Vk 0x24 -Extended $true
    $shellAfter = Wait-For -Seconds 2 -Condition { (Get-ForegroundPid).pid -eq $app.Id }
    $shellAfterMs = [int]([DateTimeOffset]::UtcNow - $homeSent).TotalMilliseconds
    Start-Sleep -Milliseconds 500
    $afterHome = Save-Evidence -Stage 'after-limit-home' -Window $window -Extra @{ shellForegroundWithin2s = $shellAfter; shellForegroundMs = $shellAfterMs }
    Note ("after limit Home: shell foreground={0} ({1} ms), foreground pid={2}" -f $shellAfter, $shellAfterMs, $afterHome.foreground.pid)
    if (-not $shellAfter) { $problems.Add('shell is not foreground within 2 s of Home after the limit') }

    $entries = @(Get-RunEntries -ProcessId $app.Id)
    $badLevel = @($entries | Where-Object { $_.level -in @('warn', 'error') -and $allowedWarnings -notcontains $_.msg })
    foreach ($entry in $badLevel) { $problems.Add("log $($entry.level): $($entry.msg) $($entry.data | ConvertTo-Json -Compress -Depth 4)") }
    $exited = @($entries | Where-Object { $_.msg -eq 'processExited' }).Count
    if ($exited -ne 0) { $problems.Add("processExited x$exited, expected 0") }
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'log'; cdpClosed = $cdpClosed; cdpLost = $lost; cdpLostHandled = $handled; cdpLostAfterSeconds = $lostAfter })
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

Write-Host 'PASS: a lost page target with a live Edge ends in cdpLost -> Failed at cdpReconnectTimeout; Home shows the shell before and after the limit.' -ForegroundColor Green
