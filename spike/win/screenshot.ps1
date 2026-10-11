#Requires -PSEdition Desktop
<#
Full-screen evidence (S10 foreground, S11 Moonlight, anything that is not a browser page).

  screenshot.ps1 [-Name S10-edge-home] [-DelaySec 3]

Saves logs\<date>-<Name>.png of the whole virtual screen and prints FILE=<path>. -DelaySec gives the person
time to bring the right window up. DRM video and HDR content may come out black or washed out.
#>
param(
    [string]$Name = 'screen',
    [int]$DelaySec = 0
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

if ($DelaySec -gt 0) { Start-Sleep -Seconds $DelaySec }
New-Item -ItemType Directory -Force -Path $MsLogs | Out-Null
$file = Join-Path $MsLogs ('{0}-{1}.png' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ($Name -replace '[^\w.-]+', '_'))
$bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
Write-Host "FILE=$file"
