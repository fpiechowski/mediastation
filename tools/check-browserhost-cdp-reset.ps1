<#
.SYNOPSIS
    DW-55 check (epic-browserhost acceptance): resets MediaStation's live CDP TCP connection with an elevated
    SetTcpEntry DELETE_TCB and shows that the document already loaded can still reach the host afterwards.

.DESCRIPTION
    Runs against a MediaStation that is already running with the web tile `-AppId` open on `-Port`. It starts nothing,
    closes nothing and injects no input:
      1. finds the one established TCP connection from MediaStation to 127.0.0.1:<port>, and the one `page` target of
         `/json/list`;
      2. reads the document's identity (`performance.timeOrigin`, `location.href`) over a separate short CDP connection;
      3. deletes both ends of MediaStation's connection in an elevated child process (one UAC prompt, the only elevated
         step): `SetTcpEntry` with `MIB_TCP_STATE_DELETE_TCB`; the old connection must disappear from the TCP table;
      4. waits for `cdpClosed`, then `cdpReconnected {trigger: cdpClosed}` for `-AppId`, within `-ReconnectSeconds`;
      5. reads the document's identity again: the same `timeOrigin` and the same target prove that the page never
         reloaded;
      6. calls the binding from that document with a v1 `hello` carrying `driverVersion` = `-Marker`, the same call the
         document's PageRuntime makes (the runtime has no later message of its own until epic-netflix-dpad, per the 6.8
         plan), and waits for `pageHello {driverVersion: <marker>}`, which the host logs only for a
         `Runtime.bindingCalled` on its current session.
    The two extra CDP connections use `Runtime.evaluate` only, never `Runtime.enable`, and this script is not part of
    the product. Evidence (`measurements.jsonl`) goes to `logs\browserhost-cdp-reset-evidence-<ts>`.

    Exit code: 0 = PASS, 1 = FAIL (printed as `VERDICT PASS` / `VERDICT FAIL`), 2 = could not run (no app, UAC declined,
    more than one candidate connection).

.PARAMETER SelfTest
    Runs without MediaStation: the MIB_TCPROW layout, the port and address byte order, and a loopback socket pair found
    in the TCP table. Not elevated, SetTcpEntry must refuse and the socket must survive. Elevated (`-Elevate`), the
    reset must break the socket at once. Exit code 0 = PASS.

.PARAMETER Elevate
    With -SelfTest: reruns the self-test in an elevated process (one UAC prompt), which runs the real reset.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-cdp-reset.ps1 -AppId netflix -Port 9222

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-cdp-reset.ps1 -SelfTest -Elevate
#>
param(
    [string]$AppId = 'netflix',
    [string]$Service = 'netflix',
    [ValidateRange(1, 65535)][int]$Port = 9222,
    [string]$Marker = 'dw55-probe',
    [ValidateRange(5, 600)][int]$ReconnectSeconds = 30,
    [string]$LogsDir = (Join-Path $env:LOCALAPPDATA 'MediaStation\logs'),
    [string]$EvidenceDir = '',
    [switch]$SelfTest,
    [switch]$Elevate,
    # Internal: the elevated child's work.
    [int]$ResetLocalPort = 0,
    [string]$ResultFile = ''
)

$ErrorActionPreference = 'Stop'

# Started from PowerShell 7, Windows PowerShell inherits a PSModulePath without its own modules (NetTCPIP).
$env:PSModulePath = (@([Environment]::GetEnvironmentVariable('PSModulePath', 'Machine'), [Environment]::GetEnvironmentVariable('PSModulePath', 'User')) | Where-Object { $_ }) -join ';'

if (-not ('BrowserCdpReset.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
namespace BrowserCdpReset {
    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_TCPROW {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
    }
    public static class Native {
        [DllImport("iphlpapi.dll")] public static extern uint SetTcpEntry(ref MIB_TCPROW row);
    }
}
'@
}

$MIB_TCP_STATE_DELETE_TCB = 12
$ERROR_NOT_FOUND = 1168
$ERROR_MR_MID_NOT_FOUND = 317
$utf8 = New-Object System.Text.UTF8Encoding $false

function Note { param([string]$Message) Write-Host ('{0} {1}' -f [DateTimeOffset]::UtcNow.ToString('HH:mm:ss.fff'), $Message) }

function ConvertTo-NetworkPort { param([int]$Value) return [uint32]((($Value -band 0xFF) -shl 8) -bor (($Value -shr 8) -band 0xFF)) }

function Get-LoopbackAddress { return [uint32][BitConverter]::ToUInt32([System.Net.IPAddress]::Loopback.GetAddressBytes(), 0) }

function Remove-TcpRow {
    param([int]$LocalPort, [int]$RemotePort)
    $row = New-Object BrowserCdpReset.MIB_TCPROW
    $row.dwState = [uint32]$MIB_TCP_STATE_DELETE_TCB
    $row.dwLocalAddr = Get-LoopbackAddress
    $row.dwLocalPort = ConvertTo-NetworkPort -Value $LocalPort
    $row.dwRemoteAddr = Get-LoopbackAddress
    $row.dwRemotePort = ConvertTo-NetworkPort -Value $RemotePort
    return [uint32][BrowserCdpReset.Native]::SetTcpEntry([ref]$row)
}

function Test-Elevated {
    $principal = New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Start-Elevated {
    # ShellExecute returns once the user answered; a declined or timed-out prompt throws Win32Exception 1223.
    param([string[]]$Arguments)
    $list = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath)) + $Arguments
    return Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList ($list -join ' ') -PassThru -Wait
}

function Get-OwnedConnections {
    param([int]$OwnerPid, [int]$RemotePort)
    return @(Get-NetTCPConnection -OwningProcess $OwnerPid -State Established -ErrorAction SilentlyContinue |
        Where-Object { $_.RemotePort -eq $RemotePort -and $_.RemoteAddress -eq '127.0.0.1' -and $_.LocalAddress -eq '127.0.0.1' })
}

# --- The elevated child: deletes both ends of one loopback connection and writes what SetTcpEntry said. ---
if ($ResetLocalPort -gt 0) {
    $hostCode = Remove-TcpRow -LocalPort $ResetLocalPort -RemotePort $Port
    # The peer's row is usually gone with the host's: the deletion sends a reset.
    $peerCode = Remove-TcpRow -LocalPort $Port -RemotePort $ResetLocalPort
    [System.IO.File]::WriteAllText($ResultFile, (([ordered]@{ hostCode = $hostCode; peerCode = $peerCode; elevated = (Test-Elevated) }) | ConvertTo-Json -Compress), $utf8)
    exit 0
}

# --- Self-test. ---
if ($SelfTest) {
    if ($Elevate -and -not (Test-Elevated)) {
        $output = Join-Path $env:TEMP ('cdp-reset-selftest-{0}.txt' -f [Guid]::NewGuid().ToString('N'))
        try { $child = Start-Elevated -Arguments @('-SelfTest', '-ResultFile', ('"{0}"' -f $output)) }
        catch { Write-Host "VERDICT could not run: elevation refused ($($_.Exception.Message))"; exit 2 }
        if (Test-Path -LiteralPath $output) { Get-Content -LiteralPath $output -Encoding UTF8 | Write-Host; Remove-Item -LiteralPath $output -Force }
        Write-Host ('Elevated self-test exit {0}' -f $child.ExitCode)
        exit $child.ExitCode
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $failed = 0
    $say = { param([string]$Line) $lines.Add($Line); Write-Host $Line }
    $assert = { param([bool]$Condition, [string]$Name) if ($Condition) { & $say "ok   $Name" } else { & $say "FAIL $Name"; $script:failed++ } }

    & $assert ([System.Runtime.InteropServices.Marshal]::SizeOf([type][BrowserCdpReset.MIB_TCPROW]) -eq 20) 'MIB_TCPROW is 20 bytes'
    & $assert ((ConvertTo-NetworkPort -Value 9222) -eq 0x0624) 'port 9222 in network byte order'
    & $assert ((Get-LoopbackAddress) -eq 0x0100007F) '127.0.0.1 as the row stores it'

    $listener = New-Object System.Net.Sockets.TcpListener ([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $client = New-Object System.Net.Sockets.TcpClient
    $server = $null
    try {
        $listenPort = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
        $client.Connect([System.Net.IPAddress]::Loopback, $listenPort)
        $server = $listener.AcceptTcpClient()
        $clientPort = ([System.Net.IPEndPoint]$client.Client.LocalEndPoint).Port
        $found = @(Get-OwnedConnections -OwnerPid $PID -RemotePort $listenPort)
        & $assert ($found.Count -eq 1 -and $found[0].LocalPort -eq $clientPort) 'the socket pair is found in the TCP table'

        $code = Remove-TcpRow -LocalPort $clientPort -RemotePort $listenPort
        & $say ("SetTcpEntry returned {0}, elevated={1}" -f $code, (Test-Elevated))
        $client.Client.ReceiveTimeout = 3000
        $buffer = New-Object byte[] 1
        $outcome = 'alive'
        try {
            if ($client.Client.Receive($buffer) -eq 0) { $outcome = 'closed' }
        }
        catch [System.Net.Sockets.SocketException] {
            $outcome = if ($_.Exception.SocketErrorCode -eq [System.Net.Sockets.SocketError]::TimedOut) { 'alive' } else { [string]$_.Exception.SocketErrorCode }
        }
        & $say "socket after the call: $outcome"
        if (Test-Elevated) {
            & $assert ($code -eq 0) 'elevated: SetTcpEntry succeeds'
            & $assert ($outcome -ne 'alive') 'elevated: the reset breaks the socket'
        }
        else {
            & $assert ($code -ne 0) 'not elevated: SetTcpEntry refuses'
            & $assert ($outcome -eq 'alive') 'not elevated: the socket survives'
        }
    }
    finally {
        $client.Close()
        if ($null -ne $server) { $server.Close() }
        $listener.Stop()
    }

    & $say ($(if ($failed -eq 0) { 'VERDICT PASS' } else { 'VERDICT FAIL' }))
    if ($ResultFile) { [System.IO.File]::WriteAllLines($ResultFile, $lines, $utf8) }
    if ($failed -eq 0) { exit 0 } else { exit 1 }
}

# --- The live check. ---
# An unexpected error says nothing about the host, so it is "could not run", not a failed check.
trap { Write-Host "VERDICT could not run: $($_.Exception.Message)"; exit 2 }
$cdpBase = "http://127.0.0.1:$Port"
$evidenceDir = if ([string]::IsNullOrWhiteSpace($EvidenceDir)) { Join-Path $LogsDir ('browserhost-cdp-reset-evidence-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) } else { $EvidenceDir }
$evidenceJsonl = Join-Path $evidenceDir 'measurements.jsonl'
$problems = New-Object System.Collections.Generic.List[string]

function Add-Jsonl {
    param([object]$Record)
    [System.IO.File]::AppendAllText($evidenceJsonl, ($Record | ConvertTo-Json -Compress -Depth 8) + "`n", $utf8)
}

function Problem { param([string]$Message) $problems.Add($Message); Note "  PROBLEM: $Message" }

function Stop-CouldNotRun { param([string]$Reason) Write-Host "VERDICT could not run: $Reason"; exit 2 }

function Get-PageTargets {
    $client = New-Object System.Net.WebClient
    $client.Proxy = $null
    try { $json = $client.DownloadString("$cdpBase/json/list") } finally { $client.Dispose() }
    return @(($json | ConvertFrom-Json) | Where-Object { $_.type -eq 'page' -and -not ([string]$_.url).StartsWith('devtools://') })
}

function Invoke-PageEvaluate {
    # One short-lived CDP connection and one Runtime.evaluate; the reply with id 1 is the only message without Runtime.enable.
    param([string]$WebSocketUrl, [string]$Expression)
    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $socket.Options.Proxy = $null
    $cts = New-Object System.Threading.CancellationTokenSource 10000
    try {
        # GetResult on these tasks writes a VoidTaskResult to the pipeline, which would become part of the return value.
        [void]$socket.ConnectAsync([Uri]$WebSocketUrl, $cts.Token).GetAwaiter().GetResult()
        $request = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $Expression; returnByValue = $true } } | ConvertTo-Json -Compress -Depth 5
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($request)
        [void]$socket.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
        $buffer = New-Object byte[] 65536
        $text = New-Object System.Text.StringBuilder
        do {
            $received = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $cts.Token).GetAwaiter().GetResult()
            [void]$text.Append([System.Text.Encoding]::UTF8.GetString($buffer, 0, $received.Count))
        } while (-not $received.EndOfMessage)
        $reply = $text.ToString() | ConvertFrom-Json
        if ($null -eq $reply.result -or $null -ne $reply.result.exceptionDetails) { throw ('Runtime.evaluate failed: ' + $text.ToString()) }
        return $reply.result.result.value
    }
    finally { $socket.Abort(); $socket.Dispose(); $cts.Dispose() }
}

function Get-DocumentIdentity {
    param([string]$WebSocketUrl)
    return (Invoke-PageEvaluate -WebSocketUrl $WebSocketUrl -Expression 'JSON.stringify({timeOrigin:performance.timeOrigin,href:location.href,binding:typeof window.__msBridge,webdriver:navigator.webdriver===true})') | ConvertFrom-Json
}

function Get-LogEntries {
    # Entries with `ts` at or after $Since from the newest log, whose file MediaStation keeps open.
    param([DateTimeOffset]$Since, [string]$Msg)
    $path = Get-ChildItem -LiteralPath $LogsDir -Filter 'mediastation-*.jsonl' | Sort-Object Name | Select-Object -Last 1
    $stream = [System.IO.File]::Open($path.FullName, 'Open', 'Read', 'ReadWrite')
    try { $text = (New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)).ReadToEnd() } finally { $stream.Dispose() }
    $entries = @()
    foreach ($line in ($text -split "`n")) {
        if (-not $line.Contains('"msg":"' + $Msg + '"')) { continue }
        try { $entry = $line | ConvertFrom-Json } catch { continue }
        $ts = if ($entry.ts -is [DateTime]) { [DateTimeOffset]::new([DateTime]::SpecifyKind($entry.ts, 'Utc')) } else { [DateTimeOffset]::Parse([string]$entry.ts, [Globalization.CultureInfo]::InvariantCulture) }
        if ($ts -ge $Since -and $entry.data.appId -eq $AppId) { $entries += $entry }
    }
    return $entries
}

function Wait-LogEntry {
    param([DateTimeOffset]$Since, [string]$Msg, [double]$Seconds, [scriptblock]$Where = { $true })
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    do {
        $found = @(Get-LogEntries -Since $Since -Msg $Msg | Where-Object $Where)
        if ($found.Count -ge 1) { return $found[0] }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $null
}

$apps = @(Get-Process -Name 'MediaStation' -ErrorAction SilentlyContinue)
if ($apps.Count -ne 1) { Stop-CouldNotRun "expected one MediaStation process, found $($apps.Count)." }
$appPid = $apps[0].Id
$connections = @(Get-OwnedConnections -OwnerPid $appPid -RemotePort $Port)
if ($connections.Count -ne 1) { Stop-CouldNotRun "expected one connection from MediaStation to 127.0.0.1:$Port, found $($connections.Count)." }
$localPort = [int]$connections[0].LocalPort
$targets = @(Get-PageTargets)
if ($targets.Count -ne 1) { Stop-CouldNotRun "expected one page target, found $($targets.Count)." }
$target = $targets[0]

[void](New-Item -ItemType Directory -Path $evidenceDir -Force)
$machine = [ordered]@{ computer = $env:COMPUTERNAME; board = (Get-CimInstance Win32_BaseBoard).Product; model = (Get-CimInstance Win32_ComputerSystem).Model }
Note ('machine ' + ($machine | ConvertTo-Json -Compress))
Note ("MediaStation pid {0}, connection 127.0.0.1:{1} -> 127.0.0.1:{2}, target {3} {4}" -f $appPid, $localPort, $Port, $target.id, $target.url)

$before = Get-DocumentIdentity -WebSocketUrl $target.webSocketDebuggerUrl
Note ("document before: timeOrigin {0}, binding {1}, webdriver {2}, {3}" -f $before.timeOrigin, $before.binding, $before.webdriver, $before.href)
Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'before'; machine = $machine; appPid = $appPid; localPort = $localPort; targetId = $target.id; document = $before })

$resultPath = Join-Path $env:TEMP ('cdp-reset-{0}.json' -f [Guid]::NewGuid().ToString('N'))
Note 'asking for elevation (UAC) to reset the connection'
try { [void](Start-Elevated -Arguments @('-Port', $Port, '-ResetLocalPort', $localPort, '-ResultFile', ('"{0}"' -f $resultPath))) }
catch { Stop-CouldNotRun "elevation refused ($($_.Exception.Message))." }
# The window for the host's reaction starts just before the reset, once the UAC answer is in.
$resetAt = [DateTimeOffset]::UtcNow.AddSeconds(-2)
if (-not (Test-Path -LiteralPath $resultPath)) { Stop-CouldNotRun 'the elevated step wrote no result.' }
$reset = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
Remove-Item -LiteralPath $resultPath -Force
Note ("SetTcpEntry DELETE_TCB: host row {0}, peer row {1}, elevated {2}" -f $reset.hostCode, $reset.peerCode, $reset.elevated)
Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'reset'; result = $reset })
if ($reset.hostCode -ne 0) { Problem "SetTcpEntry on MediaStation's row returned $($reset.hostCode)." }
# With the host's row deleted, the reset has already taken the browser's: SetTcpEntry answers 1168 or 317 for the missing row.
if ($reset.hostCode -eq 0 -and $reset.peerCode -notin @(0, $ERROR_NOT_FOUND, $ERROR_MR_MID_NOT_FOUND)) { Problem "SetTcpEntry on the browser's row returned $($reset.peerCode)." }
if (@(Get-OwnedConnections -OwnerPid $appPid -RemotePort $Port | Where-Object { $_.LocalPort -eq $localPort }).Count -ne 0) { Problem "The connection from port $localPort is still established after the reset." }

if ($problems.Count -eq 0) {
    $closed = Wait-LogEntry -Since $resetAt -Msg 'cdpClosed' -Seconds 5
    if ($null -eq $closed) { Problem 'No cdpClosed within 5 s of the reset.' }
    $reconnected = Wait-LogEntry -Since $resetAt -Msg 'cdpReconnected' -Seconds $ReconnectSeconds -Where { $_.data.trigger -eq 'cdpClosed' }
    if ($null -eq $reconnected) { Problem "No cdpReconnected {trigger: cdpClosed} within $ReconnectSeconds s." }
    else { Note ("cdpReconnected after {0} ms, attempts {1}" -f $reconnected.data.elapsedMs, $reconnected.data.attempts) }
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'reconnect'; cdpClosed = $closed; cdpReconnected = $reconnected })
}

if ($problems.Count -eq 0) {
    $after = @(Get-PageTargets)
    if ($after.Count -ne 1 -or $after[0].id -ne $target.id) { Problem 'The page target changed across the reset.' }
    $document = Get-DocumentIdentity -WebSocketUrl $target.webSocketDebuggerUrl
    Note ("document after: timeOrigin {0}, binding {1}, webdriver {2}, {3}" -f $document.timeOrigin, $document.binding, $document.webdriver, $document.href)
    if ($document.timeOrigin -ne $before.timeOrigin) { Problem 'timeOrigin changed: the document was reloaded, so this says nothing about the loaded document.' }
    if ($document.binding -ne 'function') { Problem "window.__msBridge is '$($document.binding)' in the loaded document." }

    $beforeCall = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $expression = "(function(){window.__msBridge(JSON.stringify({v:1,type:'hello',id:null,payload:{service:'$Service',driverVersion:'$Marker',webdriver:navigator.webdriver===true}}));return 'sent';})()"
    $sent = Invoke-PageEvaluate -WebSocketUrl $target.webSocketDebuggerUrl -Expression $expression
    Note "binding call from the loaded document: $sent"
    $hello = Wait-LogEntry -Since $beforeCall -Msg 'pageHello' -Seconds 5 -Where { $_.data.driverVersion -eq $Marker }
    if ($null -eq $hello) { Problem "No pageHello {driverVersion: $Marker} within 5 s: the host's new session did not get Runtime.bindingCalled." }
    elseif ($hello.data.webdriver -ne $false) { Problem 'pageHello reports webdriver true.' }
    else { Note ("pageHello from the loaded document on the new session: {0}" -f ($hello | ConvertTo-Json -Compress -Depth 4)) }
    Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'binding'; document = $document; pageHello = $hello })
}

Add-Jsonl -Record ([ordered]@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); stage = 'verdict'; problems = @($problems) })
Note "evidence: $evidenceDir"
if ($problems.Count -eq 0) {
    Write-Host 'VERDICT PASS'
    exit 0
}
foreach ($problem in $problems) { Write-Host "FAIL: $problem" }
Write-Host 'VERDICT FAIL'
exit 1
