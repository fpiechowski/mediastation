#Requires -PSEdition Desktop
<#
S9: list Windows audio sessions and mute/unmute a whole browser process tree.

  audio-mute.ps1 -Action list   [-ProfileDir edge-netflix]
  audio-mute.ps1 -Action mute   -ProfileDir edge-netflix [-Watch]
  audio-mute.ps1 -Action mute   -RootPid 1234 [-NoTree]
  audio-mute.ps1 -Action unmute -ProcessName msedge
  audio-mute.ps1 -Action monitor [-ProfileDir edge-netflix] -DurationSec 60   # session list every -IntervalSec

Long-running modes (mute/unmute -Watch, monitor) stop after -DurationSec or when the stop file appears
(default logsaudio-mute.stop); without -DurationSec they run until the stop file or Ctrl+C.

-ProfileDir: profile name under spike/profiles or a full path; the browser root is the process
whose command line contains this path and has no --type= switch.
Mute uses ISimpleAudioVolume::SetMute; where a session does not expose it (seen on Windows 11 build 26300)
it falls back to IChannelAudioVolume with all channels at 0 (unmute: 1.0). The log shows via=mute|channel.
-NoTree mutes only sessions owned by the root PIDs themselves (negative control: Chromium plays
audio from a separate audio-service process, so this should NOT silence the browser).
#>
param(
    [ValidateSet('list', 'mute', 'unmute', 'monitor')][string]$Action = 'list',
    [string]$ProfileDir,
    [int]$RootPid,
    [string]$ProcessName,
    [switch]$NoTree,
    [switch]$Watch,
    [int]$IntervalSec = 2,
    [int]$DurationSec = 0,
    [string]$StopFile,
    [string]$LogFile
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"
Add-MsType 'MsAudio.Mixer' 'AudioMute.cs'
Start-MsRun -Label "S9-audio-$Action" -LogFile $LogFile -StopFile $StopFile -DurationSec $DurationSec -Script 'audio-mute'

function Get-Procs { Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine }

function Get-Roots($procs) {
    $roots = @()
    if ($RootPid) { $roots += $RootPid }
    if ($ProfileDir) {
        $dir = if ([IO.Path]::IsPathRooted($ProfileDir)) { $ProfileDir } else { Join-Path $MsRoot "profiles\$ProfileDir" }
        $roots += $procs | Where-Object {
            $_.CommandLine -and $_.CommandLine.IndexOf($dir, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $_.CommandLine -notmatch '--type='
        } | ForEach-Object { [int]$_.ProcessId }
    }
    if ($ProcessName) { $roots += $procs | Where-Object { $_.Name -ieq "$ProcessName.exe" } | ForEach-Object { [int]$_.ProcessId } }
    , $roots
}

function Get-Tree($procs, [int[]]$roots) {
    $set = New-Object 'System.Collections.Generic.HashSet[int]'
    $queue = New-Object System.Collections.Queue
    foreach ($r in $roots) { if ($set.Add($r)) { $queue.Enqueue($r) } }
    if (-not $NoTree) {
        while ($queue.Count) {
            $parent = $queue.Dequeue()
            foreach ($child in $procs | Where-Object { $_.ParentProcessId -eq $parent }) {
                if ($set.Add([int]$child.ProcessId)) { $queue.Enqueue([int]$child.ProcessId) }
            }
        }
    }
    , $set
}

function Show-Sessions($procs, $tree) {
    $states = @{ 0 = 'inactive'; 1 = 'active'; 2 = 'expired' }
    foreach ($s in [MsAudio.Mixer]::Sessions()) {
        $name = ($procs | Where-Object { $_.ProcessId -eq $s.Pid } | Select-Object -First 1).Name
        $inTree = [bool]($tree -and $tree.Contains([int]$s.Pid))
        Write-MsLog ('session pid={0,-6} {1,-18} {2,-8} muted={3,-5} vol={4:N2} via={8,-7} target={5,-5} "{6}"{7}' -f `
                $s.Pid, $name, $states[$s.State], $s.Muted, $s.Volume, $inTree, $s.DisplayName, $(if ($s.Error) { " ERROR: $($s.Error)" } else { '' }), $s.Method)
    }
}

function Set-TreeMute($tree, [bool]$mute) {
    $changed = [MsAudio.Mixer]::SetMute([int[]]@($tree), $mute)
    foreach ($s in $changed) { Write-MsLog "SetMute($mute) pid=$($s.Pid) via=$($s.Method)" }
    $changed.Count
}

$procs = Get-Procs
$roots = Get-Roots $procs
$tree = if ($roots.Count) { Get-Tree $procs $roots } else { $null }
if ($tree) { Write-MsLog ("roots: {0}; target PIDs ({1}): {2}" -f ($roots -join ','), $tree.Count, (($tree | Sort-Object) -join ',')) }

if ($Action -eq 'list') {
    Show-Sessions $procs $tree
    return
}
if ($Action -eq 'monitor') {
    do {
        Write-MsLog '--- sessions'
        Show-Sessions $procs $tree
        Start-Sleep -Seconds $IntervalSec
        $procs = Get-Procs
        $roots = Get-Roots $procs
        $tree = if ($roots.Count) { Get-Tree $procs $roots } else { $null }
    } until (Test-MsStop)
    Write-MsLog 'stop'
    return
}
if (-not $tree) { throw 'Podaj -ProfileDir, -RootPid albo -ProcessName' }

$mute = $Action -eq 'mute'
$n = Set-TreeMute $tree $mute
Write-MsLog "changed $n session(s)"
Show-Sessions $procs $tree

# Chromium may create new sessions later (new audio stream, audio service restart); -Watch re-applies.
while ($Watch -and -not (Test-MsStop)) {
    Start-Sleep -Seconds $IntervalSec
    $procs = Get-Procs
    $tree = Get-Tree $procs (Get-Roots $procs)
    $n = Set-TreeMute $tree $mute
    if ($n) { Write-MsLog "watch: changed $n new session(s)" }
}
Write-MsLog 'stop'
