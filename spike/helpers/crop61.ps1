param([string]$In, [string]$Out, [double]$Top = 0.66, [double]$Height = 0.2, [double]$Width = 0.6)
Add-Type -AssemblyName System.Drawing
$i = [System.Drawing.Image]::FromFile((Resolve-Path $In))
$b = New-Object System.Drawing.Bitmap 900, 300
$g = [System.Drawing.Graphics]::FromImage($b)
$g.DrawImage($i, [System.Drawing.Rectangle]::new(0, 0, 900, 300), [System.Drawing.Rectangle]::new(0, [int]($i.Height * $Top), [int]($i.Width * $Width), [int]($i.Height * $Height)), 'Pixel')
$b.Save((Join-Path (Get-Location) $Out)); $i.Dispose()
