# Publish NEXTShot and compile installer (Inno Setup 6).
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$proj = Join-Path $root 'NEXTShot\NEXTShot.csproj'
$iss = Join-Path $root 'installer\NEXTShot.iss'
$staging = Join-Path $root 'installer\staging'
$outDir = Join-Path $root 'installer\output'
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

if (-not (Test-Path $iscc)) { throw "Inno Setup not found: $iscc" }

$version = '1.0.27'
if (Test-Path (Join-Path $root 'CHANGELOG.md')) {
    $m = Select-String -Path (Join-Path $root 'CHANGELOG.md') -Pattern '^## (\d+\.\d+\.\d+)' | Select-Object -First 1
    if ($m) { $version = $m.Matches[0].Groups[1].Value }
}

Write-Host "Version: $version"

if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force -Path $staging, $outDir | Out-Null

Write-Host "Publishing to $staging ..."
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Compiling installer ..."
& $iscc "/DMyAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Join-Path $outDir "NEXTShot-Setup-$version.exe"
if (-not (Test-Path $setup)) { throw "Expected installer not found: $setup" }
Write-Host "OK: $setup"
