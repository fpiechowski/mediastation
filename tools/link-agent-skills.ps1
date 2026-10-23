# Recreates .claude/skills junctions pointing at the tracked .agents/skills (junctions hold absolute paths, so they are not committed).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root '.agents\skills'
$dst = Join-Path $root '.claude\skills'
New-Item -ItemType Directory -Force $dst | Out-Null
foreach ($skill in Get-ChildItem $src -Directory) {
    $link = Join-Path $dst $skill.Name
    if (Test-Path $link) { continue }
    New-Item -ItemType Junction -Path $link -Target $skill.FullName | Out-Null
}
Write-Host "Linked $((Get-ChildItem $dst).Count) skills into $dst"
