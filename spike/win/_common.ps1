# Shared helpers for the spike/win scripts (dot-sourced).
$MsRoot = Split-Path $PSScriptRoot -Parent
$MsLogs = Join-Path $MsRoot 'logs'

function New-MsLog([string]$Label) {
    New-Item -ItemType Directory -Force -Path $MsLogs | Out-Null
    $global:MsLogFile = Join-Path $MsLogs ('{0}-{1}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $Label)
    $global:MsLogFile
}

# UTC timestamps, same format as the Node harness, so logs from S8-S11 can be correlated.
function Write-MsLog([string]$Message) {
    $line = '{0} {1}' -f [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ'), $Message
    Write-Host $line
    if ($global:MsLogFile) { Add-Content -LiteralPath $global:MsLogFile -Value $line -Encoding UTF8 }
}

# Long-running scripts: stop after -DurationSec or when the stop file appears (default logs\<script>.stop).
# Prints LOG=, STOP= and PID= first so an agent that starts the script in the background knows what to read.
function Start-MsRun([string]$Label, [string]$LogFile, [string]$StopFile, [int]$DurationSec, [string]$Script) {
    if ($LogFile) {
        New-Item -ItemType Directory -Force -Path (Split-Path $LogFile -Parent) | Out-Null
        $global:MsLogFile = $LogFile
    } else {
        New-MsLog $Label | Out-Null
    }
    if (-not $StopFile) { $StopFile = Join-Path $MsLogs "$Script.stop" }
    Remove-Item -LiteralPath $StopFile -ErrorAction SilentlyContinue
    $global:MsStopFile = $StopFile
    $global:MsDeadline = if ($DurationSec -gt 0) { (Get-Date).AddSeconds($DurationSec) } else { [DateTime]::MaxValue }
    Write-Host "LOG=$($global:MsLogFile)"
    Write-Host "STOP=$StopFile"
    Write-Host "PID=$PID"
    Write-MsLog ("start pid={0} duration={1} stop-file={2}" -f $PID, $(if ($DurationSec -gt 0) { "$DurationSec s" } else { 'bez limitu' }), $StopFile)
}

function Test-MsStop {
    (Test-Path -LiteralPath $global:MsStopFile) -or ((Get-Date) -ge $global:MsDeadline)
}

function Add-MsType([string]$TypeName, [string]$File, [string[]]$Refs) {
    if ($TypeName -as [type]) { return }
    $params = @{ Path = Join-Path $PSScriptRoot $File }
    if ($Refs) { $params.ReferencedAssemblies = $Refs }
    Add-Type @params
}
