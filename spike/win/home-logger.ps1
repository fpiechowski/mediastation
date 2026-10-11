#Requires -PSEdition Desktop
<#
S10: what does the G20S Pro remote send for Home, and can MediaStation take the foreground after it?
On UM760 the remote uses the USB 2.4G dongle, not Bluetooth. G20S Pro and Pro BT send different codes,
so the result is valid for the dongle only.

Every source is always logged, unfiltered, with the foreground process (name:PID "title"):
  LL       WH_KEYBOARD_LL: vk, scan code, flags
  RAW-KBD  Raw Input keyboard (RIDEV_INPUTSINK): vk, make code, flags, device
  RAW-HID  Raw Input HID Consumer Control 0x0C/0x01 and System Control 0x01/0x80: raw report bytes, device
           (AC Home is usually consumer usage 0x0223 and may have no VK at all)
  WM_APPCOMMAND / HSHELL_APPCOMMAND: app command id
The -Home* parameters only decide which event counts as Home (marked "<== HOME", triggers the foreground
attempt); they never filter the log.

  home-logger.ps1 [-DurationSec 120] [-StopFile path] [-LogFile path]
  home-logger.ps1 -Foreground attach|alt            # SetForegroundWindow variant (default plain)
  home-logger.ps1 -Swallow                          # LL hook eats the Home VK (does the browser still react?)
  home-logger.ps1 -HomeVk 0x24 -HomeHidUsage 0x223  # codes that count as Home

Stops on window close, after -DurationSec, or when the stop file appears (default logs\home-logger.stop).
#>
param(
    [int]$HomeVk = 0xAC,
    [int]$HomeAppCommand = 7,
    [int]$HomeHidUsage = 0x223,
    [switch]$Swallow,
    [ValidateSet('plain', 'attach', 'alt')][string]$Foreground = 'plain',
    [int]$DurationSec = 0,
    [string]$StopFile,
    [string]$LogFile
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"
Add-MsType 'MsHomeLogger' 'HomeLogger.cs' @('System.Windows.Forms', 'System.Drawing')
Start-MsRun -Label "S10-home-$Foreground" -LogFile $LogFile -StopFile $StopFile -DurationSec $DurationSec -Script 'home-logger'
$log = $global:MsLogFile

[System.Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object MsHomeLogger -ArgumentList $log, $HomeVk, $HomeAppCommand, $HomeHidUsage, $Swallow.IsPresent, $Foreground, $DurationSec, $global:MsStopFile
[System.Windows.Forms.Application]::Run($form)
