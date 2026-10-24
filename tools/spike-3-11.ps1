<#
.SYNOPSIS
    Spike 3.11: why does WH_KEYBOARD_LL stay silent when the shell is on top? Runs the experiment variants,
    injects Home/Back/OK with SendInput while the target window is in the foreground and appends one result row
    per (variant, key) to a markdown table.

.DESCRIPTION
    Variants (each starts its own process; the person touches nothing after "start"):
      baseline      MediaStation default path (HID Consumer Raw Input only; MS_INPUT_TRACE=1)
      hook-probe    tools/hook-probe      empty window without WebView2, same hook (experiment 4)
      hook-probe-raw-kbd / -hid  as above with only the keyboard (0x01/0x06) or only the HID Consumer (0x0C/0x01) registration
      hook-probe-raw  same window plus RegisterRawInputDevices RIDEV_INPUTSINK (isolates Raw Input from WebView2)

    The MediaStation path runs with only MS_INPUT_TRACE=1: `keyEnter` is logged BEFORE the policy, so a
    `keyEnter {vk, transition: down}` means the hook callback ran. It also records the active Raw Input registration.
    A row is: variant, what was on top, key, hook "down" yes/no, number of `action` entries of the expected kind,
    whether a new Edge window appeared.

    Keys are injected with keybd_event/SendInput, so they carry LLKHF_INJECTED and never come from a real
    remote: the HID Consumer / physical Raw Input path (UM760, G20S Pro) is not reproduced here. Default keys
    follow the keyboard (wildcard) row of input.json: Home = VK_HOME 0x24, Back = Esc 0x1B, OK = Enter 0x0D.

    The script takes the foreground by itself, so tell the person up front, then wait -StartDelaySeconds
    ("start") before the first injection. Do not use the keyboard or switch windows during the run.

    -SelfTest scores in-memory log entries and probe lines without any desktop or hardware.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/spike-3-11.ps1 -SelfTest

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/spike-3-11.ps1 -Variants baseline,baseline,baseline
#>
param(
    [string[]]$Variants = @('baseline', 'hook-probe', 'hook-probe-raw', 'hook-probe-raw-kbd', 'hook-probe-raw-hid'),
    [int]$Presses = 1,
    [string]$MediaStation = '',
    [string]$HookProbe = '',
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [string]$Out = '',
    [int]$StartDelaySeconds = 10,
    [int]$SettleSeconds = 2,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($MediaStation)) {
    $MediaStation = Join-Path $repoRoot 'src\MediaStation.App\bin\Release\net10.0-windows\MediaStation.exe'
}

if ([string]::IsNullOrWhiteSpace($HookProbe)) {
    $HookProbe = Join-Path $repoRoot 'tools\hook-probe\bin\Release\net10.0-windows\hook-probe.exe'
}

if ([string]::IsNullOrWhiteSpace($Out)) {
    $Out = Join-Path $LogsDir ('spike-3-11-{0}.md' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}

$Variants = @($Variants | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_.Trim() })

# name -> virtual key and the action kind the policy should emit for it.
$Keys = @(
    [pscustomobject]@{ Name = 'Home'; Vk = 0x24; Kind = 'home' },
    [pscustomobject]@{ Name = 'Back'; Vk = 0x1B; Kind = 'back' },
    [pscustomobject]@{ Name = 'OK'; Vk = 0x0D; Kind = 'ok' }
)

$MediaStationVariants = @('baseline')

# ---------------------------------------------------------------------------
# Scoring (shared by the live run and -SelfTest)
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

function Read-JsonLines {
    param([string[]]$Path)

    $entries = @()
    foreach ($file in $Path) {
        if (-not (Test-Path -LiteralPath $file)) {
            continue
        }

        # FileShare.ReadWrite: the writer keeps appending while we read.
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
                # Half-written line: the next read gets it.
            }
        }
    }

    return $entries
}

function Get-AppLogPaths {
    return @(Get-ChildItem -LiteralPath $LogsDir -Filter 'mediastation-*.jsonl' -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -ExpandProperty FullName)
}

function Get-HookDownCount {
    # Application log: `keyEnter` precedes the policy, so it proves the callback ran.
    param([object[]]$Entries, [int]$Vk, [DateTimeOffset]$From, [DateTimeOffset]$To)

    $tag = '0x{0:X2}' -f $Vk
    return @($Entries | Where-Object {
            $_.msg -eq 'keyEnter' -and $_.data.vk -eq $tag -and "$($_.data.transition)" -match '^(down|Down|0)$' -and
            (ConvertTo-Utc ([string]$_.ts)) -ge $From -and (ConvertTo-Utc ([string]$_.ts)) -lt $To
        }).Count
}

function Get-ProbeDownCount {
    # hook-probe log: `down` lines carry `vk` as a number.
    param([object[]]$Entries, [int]$Vk, [DateTimeOffset]$From, [DateTimeOffset]$To)

    return @($Entries | Where-Object {
            $_.event -eq 'down' -and [int]$_.vk -eq $Vk -and
            (ConvertTo-Utc ([string]$_.ts)) -ge $From -and (ConvertTo-Utc ([string]$_.ts)) -lt $To
        }).Count
}

function Get-ActionCount {
    param([object[]]$Entries, [string]$Kind, [DateTimeOffset]$From, [DateTimeOffset]$To)

    return @($Entries | Where-Object {
            $_.msg -eq 'action' -and $_.data.kind -eq $Kind -and
            (ConvertTo-Utc ([string]$_.ts)) -ge $From -and (ConvertTo-Utc ([string]$_.ts)) -lt $To
        }).Count
}

function Get-RunEntries {
    # Entries after the last `start {pid}` of this process.
    param([object[]]$Entries, [int]$ProcessId)

    $startIndex = -1
    for ($i = 0; $i -lt $Entries.Count; $i++) {
        if ($Entries[$i].msg -eq 'start' -and $Entries[$i].data.pid -eq $ProcessId) {
            $startIndex = $i
        }
    }

    if ($startIndex -lt 0) {
        return @()
    }

    return @($Entries | Select-Object -Skip ($startIndex + 1))
}

function Format-Row {
    param(
        [string]$Variant,
        [string]$OnTop,
        [string]$Key,
        [int]$Down,
        [int]$Actions,
        [int]$NewEdge,
        [string]$Note = ''
    )

    $downText = if ($Down -gt 0) { 'tak' } else { 'nie' }
    return ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} |' -f $Variant, $OnTop, $Key, $downText, $Actions, $NewEdge, $Note)
}

$TableHeader = @(
    '| wariant | na wierzchu | klawisz | hook "down" | wpisy action | nowe okna Edge | uwagi |',
    '|---|---|---|---|---|---|---|'
)

function Invoke-SelfTest {
    $problems = New-Object System.Collections.Generic.List[string]
    $t0 = [DateTimeOffset]::Parse('2026-10-24T12:00:00Z')
    $from = $t0.AddSeconds(1)
    $to = $t0.AddSeconds(4)

    $app = @(
        '{"ts":"2026-10-24T12:00:00.000Z","msg":"start","data":{"pid":7}}',
        '{"ts":"2026-10-24T12:00:00.500Z","msg":"keyEnter","data":{"vk":"0x24","transition":"down"}}',
        '{"ts":"2026-10-24T12:00:02.000Z","msg":"keyEnter","data":{"vk":"0x24","transition":"down"}}',
        '{"ts":"2026-10-24T12:00:02.010Z","msg":"keyEnter","data":{"vk":"0x24","transition":"up"}}',
        '{"ts":"2026-10-24T12:00:02.020Z","msg":"action","data":{"kind":"home","phase":"press"}}',
        '{"ts":"2026-10-24T12:00:03.000Z","msg":"keyEnter","data":{"vk":"0x1B","transition":"down"}}',
        '{"ts":"2026-10-24T12:00:05.000Z","msg":"action","data":{"kind":"home","phase":"press"}}'
    ) | ForEach-Object { $_ | ConvertFrom-Json }
    $probe = @(
        '{"ts":"2026-10-24T12:00:02.000Z","event":"down","vk":36,"injected":true}',
        '{"ts":"2026-10-24T12:00:02.100Z","event":"up","vk":36,"injected":true}'
    ) | ForEach-Object { $_ | ConvertFrom-Json }

    function Expect {
        param([string]$Name, $Actual, $Expected)
        Write-Host ("SelfTest '{0}': {1} - expected {2}" -f $Name, $Actual, $Expected)
        if ("$Actual" -ne "$Expected") {
            $problems.Add(("SelfTest '{0}': got '{1}' instead of '{2}'" -f $Name, $Actual, $Expected))
        }
    }

    Expect 'hook down Home in window' (Get-HookDownCount -Entries $app -Vk 0x24 -From $from -To $to) 1
    Expect 'hook down Back in window' (Get-HookDownCount -Entries $app -Vk 0x1B -From $from -To $to) 1
    Expect 'hook down OK (none)' (Get-HookDownCount -Entries $app -Vk 0x0D -From $from -To $to) 0
    Expect 'action home in window' (Get-ActionCount -Entries $app -Kind 'home' -From $from -To $to) 1
    Expect 'probe down' (Get-ProbeDownCount -Entries $probe -Vk 0x24 -From $from -To $to) 1
    Expect 'run entries after start' @(Get-RunEntries -Entries $app -ProcessId 7).Count 6
    Expect 'run entries unknown pid' @(Get-RunEntries -Entries $app -ProcessId 8).Count 0
    Expect 'row yes' (Format-Row -Variant 'v' -OnTop 'shell' -Key 'Home' -Down 1 -Actions 1 -NewEdge 0) '| v | shell | Home | tak | 1 | 0 |  |'
    Expect 'row no' (Format-Row -Variant 'v' -OnTop 'shell' -Key 'Home' -Down 0 -Actions 0 -NewEdge 1 -Note 'x') '| v | shell | Home | nie | 0 | 1 | x |'

    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "FAIL: $_" }
        return 1
    }

    Write-Host 'SelfTest passed'
    return 0
}

if ($SelfTest) {
    exit (Invoke-SelfTest)
}

# ---------------------------------------------------------------------------
# Live run
# ---------------------------------------------------------------------------

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Spike311 {
    public static class Native {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    }
}
'@

function Note {
    param([string]$Message)

    Add-Content -LiteralPath $Out -Value $Message -Encoding UTF8
    Write-Host $Message
}

function Wait-For {
    param([scriptblock]$Condition, [string]$What, [int]$Seconds = 10)

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

    if ([Spike311.Native]::IsIconic($Handle)) {
        [void][Spike311.Native]::ShowWindow($Handle, 9)
    }

    # Alt unlock: Windows lets a process take focus right after it received an Alt press.
    [Spike311.Native]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [Spike311.Native]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [void][Spike311.Native]::SetForegroundWindow($Handle)
    Start-Sleep -Milliseconds 500
    return [Spike311.Native]::GetForegroundWindow() -eq $Handle
}

function Send-Key {
    param([int]$Vk)

    [Spike311.Native]::keybd_event([byte]$Vk, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Spike311.Native]::keybd_event([byte]$Vk, 0, 2, [UIntPtr]::Zero)
}

function Get-EdgeWindowIds {
    # Visible top-level windows of `msedge` (not msedgewebview2): `pid:handle`.
    return @(Get-Process -Name 'msedge' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } |
        ForEach-Object { '{0}:{1}' -f $_.Id, $_.MainWindowHandle })
}

function Invoke-Presses {
    # Injects every key -Presses times and writes one row per key. `ReadHook` returns [hook down count, action count].
    param([string]$Variant, [string]$OnTop, [IntPtr]$Handle, [scriptblock]$ReadCounts)

    foreach ($key in $Keys) {
        $down = 0
        $actions = 0
        $edge = 0
        $note = ''
        for ($i = 1; $i -le $Presses; $i++) {
            $front = $false
            for ($try = 1; $try -le 4 -and -not $front; $try++) {
                $front = Set-FrontWindow -Handle $Handle
            }

            if (-not $front) {
                # Never inject into a window that is not the target: Enter/Esc would hit whatever else is on top.
                $note = 'target not foreground: key not sent'
                continue
            }

            $edgeBefore = @(Get-EdgeWindowIds)

            # Log `ts` is truncated to milliseconds, so the window opens slightly early.
            $from = [DateTimeOffset]::UtcNow.AddMilliseconds(-50)
            Send-Key -Vk $key.Vk
            Start-Sleep -Seconds $SettleSeconds
            $to = [DateTimeOffset]::UtcNow
            $counts = & $ReadCounts $key $from $to
            $down += $counts[0]
            $actions += $counts[1]
            $edge += @(Get-EdgeWindowIds | Where-Object { $edgeBefore -notcontains $_ }).Count
        }

        Note (Format-Row -Variant $Variant -OnTop $OnTop -Key $key.Name -Down $down -Actions $actions -NewEdge $edge -Note $note)
    }
}

function Invoke-MediaStationVariant {
    param([string]$Variant)

    $names = @('MS_INPUT_TRACE')
    $saved = @{}
    foreach ($name in $names) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }

    $app = $null
    try {
        $env:MS_INPUT_TRACE = '1'
        $app = Start-Process -FilePath $MediaStation -PassThru
        if (-not (Wait-For -What 'MediaStation window' -Seconds 30 -Condition { $app.Refresh(); $app.MainWindowHandle -ne 0 })) {
            Note (Format-Row -Variant $Variant -OnTop 'shell' -Key '*' -Down 0 -Actions 0 -NewEdge 0 -Note 'brak okna shella (instanceRejected?)')
            return
        }

        $appPid = $app.Id
        if (-not (Wait-For -What 'snapshotPublished' -Seconds 30 -Condition {
                    @(Get-RunEntries -Entries (Read-JsonLines -Path (Get-AppLogPaths)) -ProcessId $appPid | Where-Object { $_.msg -eq 'snapshotPublished' }).Count -ge 1
                })) {
            Note (Format-Row -Variant $Variant -OnTop 'shell' -Key '*' -Down 0 -Actions 0 -NewEdge 0 -Note 'brak snapshotPublished w 30 s')
            return
        }

        Start-Sleep -Seconds 1
        $run = @(Get-RunEntries -Entries (Read-JsonLines -Path (Get-AppLogPaths)) -ProcessId $appPid)
        $registrationEntries = @($run | Where-Object { $_.msg -eq 'rawInputRegistration' })
        $registration = if ($registrationEntries.Count -gt 0) { $registrationEntries[-1].data } else { $null }
        $hubErrors = @($run | Where-Object { $_.msg -eq 'inputHubError' } | ForEach-Object { $_.data.operation })
        $appLogPath = Get-AppLogPaths | ForEach-Object { Get-Item -LiteralPath $_ } |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -ExpandProperty FullName -First 1
        Note ('<!-- {0}: pid {1}; appLog={2}; rawInputRegistration entries={3}; keyboard={4}; hidConsumer={5}; success={6}; inputHubError={7} -->' -f `
            $Variant, $appPid, $appLogPath, $registrationEntries.Count, $registration.keyboard, $registration.hidConsumer, $registration.success, ($hubErrors -join ','))
        if ($registrationEntries.Count -ne 1 -or $registration.keyboard -ne $false -or $registration.hidConsumer -ne $true -or $registration.success -ne $true) {
            Note ('<!-- UWAGA: {0} nie potwierdzil rejestracji HID Consumer bez Raw Input klawiatury. -->' -f $Variant)
        }

        $handle = $app.MainWindowHandle
        Invoke-Presses -Variant $Variant -OnTop 'shell MediaStation (WebView2)' -Handle $handle -ReadCounts {
            param($key, $from, $to)
            $entries = @(Get-RunEntries -Entries (Read-JsonLines -Path (Get-AppLogPaths)) -ProcessId $appPid)
            return @(
                (Get-HookDownCount -Entries $entries -Vk $key.Vk -From $from -To $to),
                (Get-ActionCount -Entries $entries -Kind $key.Kind -From $from -To $to))
        }

        [void]$app.CloseMainWindow()
        if (-not $app.WaitForExit(8000)) {
            Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
        }
    }
    finally {
        if ($null -ne $app -and -not $app.HasExited) {
            Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
        }

        foreach ($name in $names) {
            [Environment]::SetEnvironmentVariable($name, $saved[$name])
        }
    }
}

function Invoke-ProbeVariant {
    param([string]$Variant = 'hook-probe', [string]$OnTop = 'puste okno bez WebView2', [string[]]$ExtraArgs = @())

    $log = Join-Path $LogsDir ('hook-probe-{0}.jsonl' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    $seconds = 12 + ($Keys.Count * $Presses * ($SettleSeconds + 2))
    $probe = Start-Process -FilePath $HookProbe -ArgumentList (@('--log', $log, '--seconds', $seconds) + $ExtraArgs) -PassThru
    try {
        if (-not (Wait-For -What 'hook-probe window' -Seconds 15 -Condition { $probe.Refresh(); $probe.MainWindowHandle -ne 0 -or $probe.HasExited })) {
            Note (Format-Row -Variant $Variant -OnTop 'puste okno' -Key '*' -Down 0 -Actions 0 -NewEdge 0 -Note 'brak okna')
            return
        }

        if ($probe.HasExited) {
            Note (Format-Row -Variant $Variant -OnTop 'puste okno' -Key '*' -Down 0 -Actions 0 -NewEdge 0 -Note ('kod wyjscia {0} (brak hooka)' -f $probe.ExitCode))
            return
        }

        Start-Sleep -Seconds 1
        Invoke-Presses -Variant $Variant -OnTop $OnTop -Handle $probe.MainWindowHandle -ReadCounts {
            param($key, $from, $to)
            $entries = @(Read-JsonLines -Path @($log))
            return @((Get-ProbeDownCount -Entries $entries -Vk $key.Vk -From $from -To $to), 0)
        }

        [void]$probe.WaitForExit(($seconds + 5) * 1000)
        if ($probe.HasExited -and $probe.ExitCode -ne 0) {
            Note ('<!-- hook-probe exit code {0} -->' -f $probe.ExitCode)
        }
    }
    finally {
        if (-not $probe.HasExited) {
            Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue
        }
    }

    Note ('<!-- hook-probe log: {0} -->' -f $log)
}

New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null
Set-Content -LiteralPath $Out -Value '' -Encoding UTF8

Write-Host '=== Spike 3.11 ==='
Write-Host ('Variants: {0}; presses per key: {1}' -f ($Variants -join ', '), $Presses)
Write-Host 'Instruction: after "start" the script launches and closes windows and injects Home/Back/OK on its own.'
Write-Host 'Do not touch the keyboard, mouse or windows until it prints "done". Close MediaStation first if it runs.'
for ($left = $StartDelaySeconds; $left -gt 0; $left--) {
    Write-Host ('start in {0} s...' -f $left)
    Start-Sleep -Seconds 1
}

Write-Host 'start'

if (@(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'MediaStation is already running (single instance); close it and retry.'
}

Note ('# Spike 3.11 wyniki ({0:u}, {1})' -f [DateTime]::UtcNow, $env:COMPUTERNAME)
Note 'Wejscie syntetyczne (SendInput, LLKHF_INJECTED); brak pilota i HID Consumer.'
Note ''
$TableHeader | ForEach-Object { Note $_ }

foreach ($variant in $Variants) {
    if ($variant -like 'hook-probe*') {
        if (-not (Test-Path -LiteralPath $HookProbe)) {
            throw "hook-probe.exe not found: $HookProbe - build tools/hook-probe (Release) first."
        }

        if ($variant -eq 'hook-probe-raw') {
            Invoke-ProbeVariant -Variant $variant -OnTop 'puste okno + Raw Input (kbd+HID)' -ExtraArgs @('--rawinput')
        }
        elseif ($variant -eq 'hook-probe-raw-kbd') {
            Invoke-ProbeVariant -Variant $variant -OnTop 'puste okno + Raw Input (tylko kbd)' -ExtraArgs @('--raw-kbd')
        }
        elseif ($variant -eq 'hook-probe-raw-hid') {
            Invoke-ProbeVariant -Variant $variant -OnTop 'puste okno + Raw Input (tylko HID)' -ExtraArgs @('--raw-hid')
        }
        else {
            Invoke-ProbeVariant
        }
    }
    elseif ($MediaStationVariants -contains $variant) {
        if (-not (Test-Path -LiteralPath $MediaStation)) {
            throw "MediaStation.exe not found: $MediaStation - build src/MediaStation.App (Release) first."
        }

        Invoke-MediaStationVariant -Variant $variant
    }
    else {
        throw "Unknown variant '$variant'."
    }
}

Write-Host "done - results: $Out"
