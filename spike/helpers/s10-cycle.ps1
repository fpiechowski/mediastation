# S10 helper: before each Home press, bring the next target to the foreground; log what was on top.
param([string]$Log, [string]$Targets, [string]$Out)
. "$PSScriptRoot\..\win\_common.ps1"
Add-MsType 'MsWin' 'WinTools.cs'
Add-Type -Namespace S10 -Name Kb -MemberDefinition '[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);'
$homeCount = { (Select-String -LiteralPath $Log -Pattern 'HOME \+300ms' -ErrorAction SilentlyContinue | Measure-Object).Count }
foreach ($t in $Targets.Split(',')) {
    if ($t -eq 'desktop') { (New-Object -ComObject Shell.Application).MinimizeAll(); Start-Sleep 1; $desc = 'desktop (MinimizeAll)' }
    else {
        $h = [MsWin]::TopWindows([uint32]$t, $false) | Select-Object -First 1
        [S10.Kb]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [S10.Kb]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)  # Alt unlocks SetForegroundWindow
        $ok = [MsWin]::SetForegroundWindow($h)
        $desc = "pid=$t ok=$ok `"$([MsWin]::Title($h))`""
    }
    $n = & $homeCount
    Add-Content -LiteralPath $Out -Value ("{0} TARGET {1}; czekam na Home" -f [DateTime]::UtcNow.ToString('o'), $desc) -Encoding UTF8
    $deadline = (Get-Date).AddSeconds(180)
    while ((& $homeCount) -le $n -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    Start-Sleep -Milliseconds 700
    $fg = [MsWin]::GetForegroundWindow()
    Add-Content -LiteralPath $Out -Value ("{0} po Home: fg=`"{1}`" pid={2}" -f [DateTime]::UtcNow.ToString('o'), [MsWin]::Title($fg), [MsWin]::ProcessIdOf($fg)) -Encoding UTF8
    & powershell.exe -ExecutionPolicy Bypass -File "$PSScriptRoot\..\win\screenshot.ps1" -Name "S10-after-home-$($t)" | Out-Null
    Start-Sleep 2
}
Add-Content -LiteralPath $Out -Value "KONIEC" -Encoding UTF8
