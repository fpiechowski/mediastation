$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tools\MediaStation.LogAppendCheck\MediaStation.LogAppendCheck.csproj'

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet run --project $project -c Release --no-build
exit $LASTEXITCODE
