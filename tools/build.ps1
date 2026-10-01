$ErrorActionPreference = 'Stop'
$petProjectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $petProjectRoot
try {
    dotnet run --project tests/Hajimi.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Behavior and asset tests failed.' }
    & (Join-Path $PSScriptRoot 'package.ps1')
    Write-Host 'Tests and standalone publish completed.'
} finally { Pop-Location }
