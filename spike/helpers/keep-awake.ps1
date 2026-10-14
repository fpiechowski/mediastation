# Keeps UM760 out of Modern Standby during S8 (ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED); ends with the process.
param([int]$Minutes = 120)
Add-Type -Namespace S8 -Name Power -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
[S8.Power]::SetThreadExecutionState(0x80000003) | Out-Null
Start-Sleep -Seconds ($Minutes * 60)
