<#
.SYNOPSIS
    Bug 5.8 check: the Moonlight stream keeps running after Home and a return through the tile, every time.

.DESCRIPTION
    Starts MediaStation with MEDIASTATION_DEV_HARNESS=1 and a generated `apps.json` with one Moonlight tile (the real
    file is copied and put back in `finally`). An injected OK opens the tile and the script waits for the single
    `appWindowAttached`. The attached window must be the SDL stream window (class `SDL_app`), not the Qt connecting
    window that Moonlight hides once the stream is up. Then -Cycles times: injected Home (the stream window must end
    up `IsIconic` and visible, the shell on top), injected OK on the tile (the stream window must come back
    not iconic and in the foreground) and two screenshots of the window a few seconds apart.

    The verdict is the application log (`windowCommand` MinimizeWindow/ShowShell after Home and ActivateWindow after
    OK, all `completed`; `appWindowAttached` exactly once; no `warn`/`error` except `appIconMissing` and
    `devHarnessEnabled`; no `processExited`) and the Moonlight client log (`%TEMP%\Moonlight-*.log`, flushed when the
    client closes): one RTSP handshake, one "Starting input stream..." and a non-zero incoming and rendering frame
    rate, i.e. the connection went through the input stream establishment and was not restarted. The screenshots
    (-ShotsDir) are extra evidence; they can match when the host desktop is static.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-moonlight-return.ps1 -MoonlightHost 192.168.0.64
#>
param(
    [Parameter(Mandatory)][string]$MoonlightHost,
    [string]$MoonlightApp = 'Desktop',
    [string]$MoonlightExe = [string]$env:MEDIASTATION_HARNESS_MOONLIGHT,
    [ValidateRange(1, 100)][int]$Cycles = 4,
    [int]$SunshinePort = 47989,
    [string]$MediaStation = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [string]$ShotsDir = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe'
}

if ([string]::IsNullOrWhiteSpace($ShotsDir)) {
    $ShotsDir = Join-Path $LogsDir ('moonlight-return-{0}' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
if (-not ('MoonlightReturn.Native' -as [type])) {
    Add-Type -Namespace MoonlightReturn -Name Native -MemberDefinition @'
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
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

$native = [MoonlightReturn.Native]
$problems = New-Object System.Collections.Generic.List[string]
$allowedWarnings = @('appIconMissing', 'devHarnessEnabled')

function Note {
    param([string]$Message)

    Write-Host ('{0} {1}' -f [DateTimeOffset]::UtcNow.ToString('HH:mm:ss.fff'), $Message)
}

function Send-KeyPress {
    param([int]$VirtualKey, [bool]$Extended = $false)

    [void]$native::SendKey([uint16]$VirtualKey, $Extended, $false)
    Start-Sleep -Milliseconds 60
    [void]$native::SendKey([uint16]$VirtualKey, $Extended, $true)
}

function Set-FrontWindow {
    param([IntPtr]$Handle)

    if ($native::IsIconic($Handle)) {
        [void]$native::ShowWindow($Handle, 9)
    }

    # Alt unlock: Windows lets a process take focus right after it received an Alt press.
    $native::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    $native::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    return $native::SetForegroundWindow($Handle)
}

function Get-WindowClass {
    param([IntPtr]$Handle)

    $text = New-Object System.Text.StringBuilder 256
    [void]$native::GetClassNameW($Handle, $text, 256)
    return $text.ToString()
}

function Wait-For {
    param([scriptblock]$Condition, [int]$Seconds)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Condition) {
            return $true
        }

        Start-Sleep -Milliseconds 200
    }

    # One last look at the deadline: a slow condition must not turn a late success into a timeout.
    return [bool](& $Condition)
}

function Get-RunEntries {
    # Entries of the process `$ProcessId`: everything after its last `start {pid}` in today's application log.
    param([int]$ProcessId, [string]$Msg = '')

    $path = Get-ChildItem -LiteralPath $LogsDir -Filter 'mediastation-*.jsonl' | Sort-Object Name | Select-Object -Last 1
    $stream = [System.IO.File]::Open($path.FullName, 'Open', 'Read', 'ReadWrite')
    try {
        $text = (New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)).ReadToEnd()
    }
    finally {
        $stream.Dispose()
    }

    $lines = $text -split "`n"
    $startAt = -1
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        if ($lines[$i] -match '"msg":"start".*"pid":(\d+)' -and [int]$Matches[1] -eq $ProcessId) {
            $startAt = $i
            break
        }
    }

    $entries = @()
    if ($startAt -ge 0) {
        for ($i = $startAt; $i -lt $lines.Count; $i++) {
            # The `msg` prefilter keeps polling cheap: only the wanted lines go through ConvertFrom-Json.
            if ($lines[$i].Trim().Length -gt 0 -and ($Msg.Length -eq 0 -or $lines[$i].Contains('"msg":"' + $Msg + '"'))) {
                try { $entries += ($lines[$i] | ConvertFrom-Json) } catch { }
            }
        }
    }

    return $entries
}

function Get-WindowCommands {
    param([int]$ProcessId, [string]$Type)

    return @(Get-RunEntries -ProcessId $ProcessId -Msg 'windowCommand' | Where-Object { $_.data.type -eq $Type })
}

function Save-Shot {
    param([IntPtr]$Handle, [string]$Name)

    if ($native::IsIconic($Handle)) {
        # A minimized window has no pixels on screen (rect near -32000): the missing restore is reported by the caller.
        return 'iconic'
    }

    $rect = New-Object MoonlightReturn.Native+RECT
    [void]$native::GetWindowRect($Handle, [ref]$rect)
    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)
    $full = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($full)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $full.Size)
    $small = New-Object System.Drawing.Bitmap $full, 480, 270
    $path = Join-Path $ShotsDir ($Name + '.png')
    $small.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $full.Dispose(); $small.Dispose()
    $sha = [System.Security.Cryptography.SHA1]::Create()
    return [BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($path))).Replace('-', '').Substring(0, 12)
}

function Test-TcpPort {
    param([string]$HostName, [int]$Port)

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        return ($client.ConnectAsync($HostName, $Port).Wait(2000) -and $client.Connected)
    }
    catch { return $false }
    finally { $client.Dispose() }
}

if (-not (Test-Path -LiteralPath $MediaStation)) { throw "MediaStation.exe not found: $MediaStation - publish Release first." }
if ([string]::IsNullOrWhiteSpace($MoonlightExe) -or -not (Test-Path -LiteralPath $MoonlightExe)) { throw 'Set MEDIASTATION_HARNESS_MOONLIGHT (or -MoonlightExe) to Moonlight.exe.' }
if (-not (Test-TcpPort -HostName $MoonlightHost -Port $SunshinePort)) { throw "Sunshine not reachable: ${MoonlightHost}:$SunshinePort" }
if (@(Get-Process -Name 'MediaStation', 'Moonlight' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'MediaStation or Moonlight is already running; close it and retry.' }

New-Item -ItemType Directory -Force -Path $ShotsDir | Out-Null
$appsPath = Join-Path (Join-Path $env:LOCALAPPDATA 'MediaStation\config') 'apps.json'
$backupPath = $appsPath + '.restore-bak'
if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }

$appId = 'moonlight-return'
$tile = [ordered]@{ type = 'moonlight'; id = $appId; category = 'video'; title = 'Return'; icon = 'return.png'; host = $MoonlightHost; app = $MoonlightApp }
$app = $null
$streamWindow = [IntPtr]::Zero
$logsBefore = @(Get-ChildItem -Path $env:TEMP -Filter 'Moonlight-*.log' | ForEach-Object Name)
try {
    Copy-Item -LiteralPath $appsPath -Destination $backupPath -Force
    Set-Content -LiteralPath $appsPath -Value (@{ apps = @($tile) } | ConvertTo-Json -Depth 5) -Encoding UTF8

    $env:MEDIASTATION_DEV_HARNESS = '1'
    $env:MEDIASTATION_HARNESS_MOONLIGHT = $MoonlightExe
    $app = Start-Process -FilePath $MediaStation -PassThru
    Note ('MediaStation pid {0}, tile {1} -> {2}/{3}' -f $app.Id, $appId, $MoonlightHost, $MoonlightApp)

    if (-not (Wait-For -Seconds 30 -Condition { $app.Refresh(); $app.MainWindowHandle -ne 0 })) { throw 'MediaStation window did not appear within 30s.' }
    if (-not (Wait-For -Seconds 30 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'snapshotPublished').Count -ge 1 })) { throw 'No snapshotPublished within 30s.' }

    $shell = [IntPtr]$app.MainWindowHandle
    $managed = @(Get-RunEntries -ProcessId $app.Id -Msg 'managedShell') | Select-Object -Last 1
    if ($null -ne $managed) { $shell = [IntPtr]([int64]$managed.data.hwnd) }

    Start-Sleep -Seconds 2
    [void](Set-FrontWindow -Handle $shell)
    Start-Sleep -Milliseconds 800
    Note 'injected OK (start the stream)'
    Send-KeyPress -VirtualKey 0x0D

    if (-not (Wait-For -Seconds 45 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'appWindowAttached').Count -ge 1 })) {
        throw 'No appWindowAttached within 45s (the stream window never appeared).'
    }

    $attached = @(Get-RunEntries -ProcessId $app.Id -Msg 'appWindowAttached')[0]
    $streamWindow = [IntPtr]([int64]$attached.data.hwnd)
    $class = Get-WindowClass -Handle $streamWindow
    Note ('appWindowAttached hwnd=0x{0:X} class={1} rootPid={2}' -f $streamWindow.ToInt64(), $class, $attached.data.rootPid)
    if ($class -ne 'SDL_app') { throw "attached window class is '$class', expected the SDL stream window 'SDL_app'; the cycles would test the wrong window" }
    Start-Sleep -Seconds 4

    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        $label = "cycle $cycle"
        $minimizeBefore = @(Get-WindowCommands -ProcessId $app.Id -Type 'MinimizeWindow').Count
        $shellBefore = @(Get-WindowCommands -ProcessId $app.Id -Type 'ShowShell').Count
        $activateBefore = @(Get-WindowCommands -ProcessId $app.Id -Type 'ActivateWindow').Count

        Send-KeyPress -VirtualKey 0x24 -Extended $true
        $homeDone = Wait-For -Seconds 10 -Condition {
            @(Get-WindowCommands -ProcessId $app.Id -Type 'MinimizeWindow').Count -gt $minimizeBefore -and
            @(Get-WindowCommands -ProcessId $app.Id -Type 'ShowShell').Count -gt $shellBefore
        }
        Start-Sleep -Seconds 2
        $iconic = $native::IsIconic($streamWindow)
        $visible = $native::IsWindowVisible($streamWindow)
        $shellTop = ($native::GetForegroundWindow() -eq $shell)
        Note ('{0}: Home -> logged={1} IsIconic={2} IsWindowVisible={3} shellOnTop={4}' -f $label, $homeDone, $iconic, $visible, $shellTop)
        if (-not $homeDone) { $problems.Add("$label Home: no MinimizeWindow and ShowShell in the log") }
        if (-not ($iconic -and $visible)) { $problems.Add("$label Home: stream window not minimized and visible (iconic=$iconic visible=$visible)") }
        if (-not $shellTop) { $problems.Add("$label Home: shell not on top") }

        [void](Set-FrontWindow -Handle $shell)
        Start-Sleep -Milliseconds 800
        Send-KeyPress -VirtualKey 0x0D
        $okDone = Wait-For -Seconds 10 -Condition { @(Get-WindowCommands -ProcessId $app.Id -Type 'ActivateWindow').Count -gt $activateBefore }
        Start-Sleep -Seconds 4
        $iconic = $native::IsIconic($streamWindow)
        $onTop = ($native::GetForegroundWindow() -eq $streamWindow)
        $shotA = Save-Shot -Handle $streamWindow -Name ('cycle{0}-a' -f $cycle)
        Start-Sleep -Seconds 4
        $shotB = Save-Shot -Handle $streamWindow -Name ('cycle{0}-b' -f $cycle)
        Note ('{0}: OK -> logged={1} IsIconic={2} streamOnTop={3} shots {4} / {5} ({6})' -f $label, $okDone, $iconic, $onTop, $shotA, $shotB, $(if ($shotA -ne $shotB) { 'differ' } else { 'same' }))
        if (-not $okDone) { $problems.Add("$label OK: no ActivateWindow in the log") }
        if ($iconic -or -not $onTop) { $problems.Add("$label OK: stream window not restored in the foreground (iconic=$iconic onTop=$onTop)") }
        if (-not $native::IsWindow($streamWindow)) { $problems.Add("$label OK: stream window is gone") }
    }

    $entries = @(Get-RunEntries -ProcessId $app.Id)
    $badWindow = @($entries | Where-Object { $_.msg -eq 'windowCommand' -and $_.data.result -ne 'completed' })
    $badLevel = @($entries | Where-Object { $_.level -in @('warn', 'error') -and $allowedWarnings -notcontains $_.msg })
    $attachedCount = @($entries | Where-Object { $_.msg -eq 'appWindowAttached' }).Count
    $exited = @($entries | Where-Object { $_.msg -eq 'processExited' }).Count
    Note ('log: appWindowAttached={0} processExited={1} windowCommandNotCompleted={2} warnOrError={3}' -f $attachedCount, $exited, $badWindow.Count, $badLevel.Count)
    if ($attachedCount -ne 1) { $problems.Add("appWindowAttached x$attachedCount, expected 1") }
    if ($exited -ne 0) { $problems.Add("processExited x$exited, expected 0") }
    if ($badWindow.Count -gt 0) { $problems.Add("$($badWindow.Count) windowCommand entries not completed") }
    foreach ($entry in $badLevel) { $problems.Add("log $($entry.level): $($entry.msg)") }
}
catch {
    $problems.Add(('run aborted: {0}' -f $_.Exception.Message))
}
finally {
    Remove-Item Env:\MEDIASTATION_DEV_HARNESS -ErrorAction SilentlyContinue
    Remove-Item Env:\MEDIASTATION_HARNESS_MOONLIGHT -ErrorAction SilentlyContinue
    # Moonlight flushes its client log when it closes: ask it to quit (WM_CLOSE), then stop what is left.
    foreach ($moon in @(Get-Process -Name 'Moonlight' -ErrorAction SilentlyContinue)) {
        & taskkill.exe /PID $moon.Id 2>&1 | Out-Null
        [void]$moon.WaitForExit(8000)
        if (-not $moon.HasExited) { Stop-Process -Id $moon.Id -Force -ErrorAction SilentlyContinue }
    }

    if ($null -ne $app) {
        [void]$app.CloseMainWindow()
        if (-not $app.WaitForExit(8000)) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    }

    if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }
}

# Moonlight client log of this run: one connection that went through the input stream establishment and carried frames.
Start-Sleep -Seconds 2
$clientLog = Get-ChildItem -Path $env:TEMP -Filter 'Moonlight-*.log' | Where-Object { $logsBefore -notcontains $_.Name } | Sort-Object LastWriteTime | Select-Object -Last 1
if ($null -eq $clientLog) {
    $problems.Add('no Moonlight client log for this run')
}
else {
    $lines = @(Get-Content -LiteralPath $clientLog.FullName)
    $handshakes = @($lines | Where-Object { $_ -match 'Starting RTSP handshake' }).Count
    $inputStarts = @($lines | Where-Object { $_ -match 'Starting input stream\.\.\.' }).Count
    $incoming = ($lines | Where-Object { $_ -match 'Incoming frame rate from network: ([\d.]+) FPS' } | ForEach-Object { [double]$Matches[1] } | Select-Object -First 1)
    $rendering = ($lines | Where-Object { $_ -match 'Rendering frame rate: ([\d.]+) FPS' } | ForEach-Object { [double]$Matches[1] } | Select-Object -First 1)
    Note ('Moonlight log {0}: RTSP handshakes={1} input stream starts={2} incoming={3} FPS rendering={4} FPS' -f $clientLog.Name, $handshakes, $inputStarts, $incoming, $rendering)
    if ($handshakes -ne 1 -or $inputStarts -ne 1) { $problems.Add("Moonlight connected $handshakes time(s) (input stream $inputStarts), expected one uninterrupted connection") }
    if (-not ($incoming -gt 0 -and $rendering -gt 0)) { $problems.Add('Moonlight log shows no incoming/rendered frames') }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "FAIL: $_" }
    Note ('FAILED: {0} problem(s); shots in {1}' -f $problems.Count, $ShotsDir)
    exit 1
}

Note ('OK: {0}/{0} cycles; shots in {1}' -f $Cycles, $ShotsDir)
exit 0
