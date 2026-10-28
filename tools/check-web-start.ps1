<#
.SYNOPSIS
    Bug 5.14 check: OK on a web tile opens Edge (dev harness) on a clean profile and when the profile is already running.

.DESCRIPTION
    Starts MediaStation with MEDIASTATION_DEV_HARNESS=1 and a generated `apps.json` with one web tile (the real file is
    copied and put back in `finally`). Two passes, each with a fresh MediaStation:
      clean    - no browser process holds `profiles\<id>`; an injected OK must give one `appWindowAttached` (no
                 `devHarnessStartFailed`), then Home (window IsIconic and visible) and OK (restored, still the same
                 root process, no new browser process for the profile).
      leftover - a browser with `profiles\<id>` is started by the script first (the second `msedge.exe` of a held profile
                 forwards the request and exits with code 0 after ~120 ms, the 5.14 failure); an injected OK must adopt
                 it (`appWindowAttached adopted=true`, same rootPid, no extra process).
    The verdict is the application log: no `warn`/`error` except `appIconMissing` and `devHarnessEnabled`.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-web-start.ps1
#>
param(
    [string]$AppId = 'netflix',
    [ValidateRange(1, 20)][int]$Cycles = 2,
    [string]$MediaStation = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [string]$DiagnosticUrl = 'https://www.netflix.com/'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe'
}

if (-not ('WebStart.Native' -as [type])) {
    Add-Type -Namespace WebStart -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
'@
}

$native = [WebStart.Native]
$problems = New-Object System.Collections.Generic.List[string]
$allowedWarnings = @('appIconMissing', 'devHarnessEnabled')
$profileDir = Join-Path $env:LOCALAPPDATA ('MediaStation\profiles\' + $AppId)
$appsPath = Join-Path (Join-Path $env:LOCALAPPDATA 'MediaStation\config') 'apps.json'
$backupPath = $appsPath + '.restore-bak'

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

function Get-WindowCommands {
    param([int]$ProcessId, [string]$Type)
    return @(Get-RunEntries -ProcessId $ProcessId -Msg 'windowCommand' | Where-Object { $_.data.type -eq $Type })
}

function Get-ProfileRoots {
    # Main (non --type=) browser processes carrying the profile argument.
    return @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine -notmatch '--type=' -and $_.CommandLine.Replace('"', '') -like "*--user-data-dir=$profileDir*" } |
        ForEach-Object { [int]$_.ProcessId })
}

function Stop-Profile {
    foreach ($root in Get-ProfileRoots) { & taskkill.exe /PID $root /T /F 2>&1 | Out-Null }
    [void](Wait-For -Seconds 10 -Condition { (Get-ProfileRoots).Count -eq 0 })
}

function Invoke-Pass {
    param([string]$Pass)

    Stop-Profile
    $preRoot = 0

    $app = Start-Process -FilePath $MediaStation -PassThru
    try {
        Note ("${Pass}: MediaStation pid {0}" -f $app.Id)
        if (-not (Wait-For -Seconds 30 -Condition { $app.Refresh(); $app.MainWindowHandle -ne 0 })) { throw 'MediaStation window did not appear within 30s.' }
        if (-not (Wait-For -Seconds 30 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'snapshotPublished').Count -ge 1 })) { throw 'No snapshotPublished within 30s.' }

        # Started after MediaStation is up, so the startup probe does not adopt it and OK takes the start path.
        if ($Pass -eq 'leftover') {
            $edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
            $held = Start-Process -FilePath $edge -ArgumentList @("--user-data-dir=$profileDir", '--no-first-run', '--no-default-browser-check', '--kiosk', 'about:blank') -PassThru
            $preRoot = $held.Id
            if (-not (Wait-For -Seconds 15 -Condition { $held.Refresh(); $held.MainWindowHandle -ne 0 })) { $problems.Add("${Pass}: leftover browser never showed a window") }
            Note ("${Pass}: leftover browser root pid $preRoot")
        }

        $shell = [IntPtr]$app.MainWindowHandle
        $managed = @(Get-RunEntries -ProcessId $app.Id -Msg 'managedShell') | Select-Object -Last 1
        if ($null -ne $managed) { $shell = [IntPtr]([int64]$managed.data.hwnd) }

        Start-Sleep -Seconds 2
        Set-Front -Handle $shell
        Start-Sleep -Milliseconds 800
        Note "$Pass`: injected OK"
        Send-Key -Vk 0x0D

        $attachedOk = Wait-For -Seconds 30 -Condition { @(Get-RunEntries -ProcessId $app.Id -Msg 'appWindowAttached').Count -ge 1 }
        if (-not $attachedOk) {
            $failed = @(Get-RunEntries -ProcessId $app.Id | Where-Object { $_.msg -in @('devHarnessStartFailed', 'appLaunchStage') })
            foreach ($f in $failed) { Note ('  log {0}: {1}' -f $f.msg, ($f.data | ConvertTo-Json -Compress -Depth 4)) }
            throw 'No appWindowAttached within 30s.'
        }

        $attached = @(Get-RunEntries -ProcessId $app.Id -Msg 'appWindowAttached')[0]
        $window = [IntPtr]([int64]$attached.data.hwnd)
        Note ("$Pass`: appWindowAttached hwnd=0x{0:X} rootPid={1} adopted={2}" -f $window.ToInt64(), $attached.data.rootPid, $attached.data.adopted)
        if ($Pass -eq 'leftover') {
            if ($attached.data.adopted -ne $true) { $problems.Add("$Pass`: attach was not an adoption") }
            if ([int]$attached.data.rootPid -ne $preRoot) { $problems.Add("$Pass`: rootPid $($attached.data.rootPid) is not the leftover root $preRoot") }
        }

        Start-Sleep -Seconds 3
        $rootsAfterStart = @(Get-ProfileRoots)
        if ($rootsAfterStart.Count -ne 1) { $problems.Add("$Pass`: $($rootsAfterStart.Count) browser roots for the profile after start, expected 1") }

        for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
            $minBefore = @(Get-WindowCommands -ProcessId $app.Id -Type 'MinimizeWindow').Count
            $actBefore = @(Get-WindowCommands -ProcessId $app.Id -Type 'ActivateWindow').Count
            Send-Key -Vk 0x24 -Extended $true
            $homeDone = Wait-For -Seconds 10 -Condition { @(Get-WindowCommands -ProcessId $app.Id -Type 'MinimizeWindow').Count -gt $minBefore }
            Start-Sleep -Seconds 2
            $iconic = $native::IsIconic($window); $visible = $native::IsWindowVisible($window)
            Note ("$Pass cycle ${cycle}: Home logged=$homeDone IsIconic=$iconic IsWindowVisible=$visible")
            if (-not $homeDone) { $problems.Add("$Pass cycle ${cycle}: Home gave no MinimizeWindow") }
            if (-not ($iconic -and $visible)) { $problems.Add("$Pass cycle ${cycle}: window not minimized and visible") }

            Set-Front -Handle $shell
            Start-Sleep -Milliseconds 800
            Send-Key -Vk 0x0D
            $okDone = Wait-For -Seconds 10 -Condition { @(Get-WindowCommands -ProcessId $app.Id -Type 'ActivateWindow').Count -gt $actBefore }
            Start-Sleep -Seconds 2
            $iconic = $native::IsIconic($window); $onTop = ($native::GetForegroundWindow() -eq $window)
            Note ("$Pass cycle ${cycle}: OK logged=$okDone IsIconic=$iconic onTop=$onTop")
            if (-not $okDone) { $problems.Add("$Pass cycle ${cycle}: OK gave no ActivateWindow") }
            if ($iconic -or -not $onTop) { $problems.Add("$Pass cycle ${cycle}: window not restored on top") }
        }

        $roots = @(Get-ProfileRoots)
        if ($roots.Count -ne 1 -or $roots[0] -ne [int]$attached.data.rootPid) { $problems.Add("$Pass`: browser roots after the cycles: $($roots -join ','), expected only $($attached.data.rootPid)") }

        $entries = @(Get-RunEntries -ProcessId $app.Id)
        $launches = @($entries | Where-Object { $_.msg -eq 'appLaunch' -and $_.data.appId -eq $AppId })
        if ($launches.Count -lt 1) { $problems.Add("$Pass`: no appLaunch evidence") }
        else {
            $expectedUrl = ($DiagnosticUrl -split '[?#]')[0]
            if (-not ($launches | Where-Object { $_.data.url -eq $expectedUrl })) { $problems.Add("$Pass`: appLaunch URL evidence does not match $expectedUrl") }
        }
        $badLevel = @($entries | Where-Object { $_.level -in @('warn', 'error') -and $allowedWarnings -notcontains $_.msg })
        $count = @($entries | Where-Object { $_.msg -eq 'appWindowAttached' }).Count
        $exited = @($entries | Where-Object { $_.msg -eq 'processExited' }).Count
        Note ("$Pass`: log appWindowAttached=$count processExited=$exited warnOrError=$($badLevel.Count)")
        if ($count -ne 1) { $problems.Add("$Pass`: appWindowAttached x$count, expected 1") }
        if ($exited -ne 0) { $problems.Add("$Pass`: processExited x$exited, expected 0") }
        foreach ($entry in $badLevel) { $problems.Add("$Pass`: log $($entry.level): $($entry.msg)") }
    }
    catch {
        $problems.Add(('{0}: run aborted: {1}' -f $Pass, $_.Exception.Message))
    }
    finally {
        [void]$app.CloseMainWindow()
        if (-not $app.WaitForExit(8000)) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
        Stop-Profile
    }
}

if (-not (Test-Path -LiteralPath $MediaStation)) { throw "MediaStation.exe not found: $MediaStation - publish Release first." }
if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'MediaStation is already running; close it and retry.' }
if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }

$tile = [ordered]@{ type = 'web'; id = $AppId; category = 'video'; title = 'Web'; icon = 'web.png'; service = 'netflix'; browser = 'edge'; cdpPort = 9222 }
try {
    Copy-Item -LiteralPath $appsPath -Destination $backupPath -Force
    Set-Content -LiteralPath $appsPath -Value (@{ apps = @($tile) } | ConvertTo-Json -Depth 5) -Encoding UTF8
    $env:MEDIASTATION_DEV_HARNESS = '1'
    $env:MEDIASTATION_HARNESS_URL = $DiagnosticUrl
    Invoke-Pass -Pass 'clean'
    Invoke-Pass -Pass 'leftover'
}
finally {
    Remove-Item Env:\MEDIASTATION_DEV_HARNESS -ErrorAction SilentlyContinue
    Remove-Item Env:\MEDIASTATION_HARNESS_URL -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $appsPath -Force }
}

if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host ('FAIL ({0} problems)' -f $problems.Count) -ForegroundColor Red
    $problems | ForEach-Object { Write-Host ('  - ' + $_) }
    exit 1
}

Write-Host ''
Write-Host 'PASS: web tile starts the browser on a clean profile and adopts a running one; Home/OK keep the same root.' -ForegroundColor Green
