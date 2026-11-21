# Release build NEXTShot (closes running instance if needed).
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$proj = Join-Path $root 'NEXTShot\NEXTShot.csproj'
$out = Join-Path $root 'NEXTShot\bin\Release\net8.0-windows'

. (Join-Path $PSScriptRoot 'Ensure-NEXTShot-NotRunning.ps1')

Write-Host "Building $proj -> $out"
dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "OK: $out"