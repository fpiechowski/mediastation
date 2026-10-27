param([string]$Name)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);' -Name C -Namespace W61m
[W61m.C]::SetCursorPos(960, 300); Start-Sleep -Milliseconds 400
[W61m.C]::SetCursorPos(960, 0); Start-Sleep -Milliseconds 300; [W61m.C]::SetCursorPos(962, 1); Start-Sleep -Milliseconds 1800
$b = New-Object System.Drawing.Bitmap 1920, 160
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen(0, 0, 0, 0, $b.Size)
$f = Join-Path (Resolve-Path logs) ("61-topedge-$Name.png"); $b.Save($f); [W61m.C]::SetCursorPos(960, 700); Write-Output $f
