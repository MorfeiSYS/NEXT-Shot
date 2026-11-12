# Downloads ffmpeg.exe (win64) into NEXTShot/ffmpeg/ for publish.
$ErrorActionPreference = 'Stop'
$destDir = Join-Path $PSScriptRoot '..\NEXTShot\ffmpeg'
$destExe = Join-Path $destDir 'ffmpeg.exe'
if (Test-Path $destExe) {
    Write-Host "ffmpeg.exe already present: $destExe"
    exit 0
}

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$zip = Join-Path $env:TEMP "nextshot-ffmpeg-win64.zip"
$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip'

Write-Host "Downloading $url ..."
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
$extract = Join-Path $env:TEMP "nextshot-ffmpeg-extract"
if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
New-Item -ItemType Directory -Force -Path $extract | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $extract)

$bin = Get-ChildItem -Path $extract -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
if (-not $bin) { throw 'ffmpeg.exe not found in archive' }

Copy-Item -Path $bin.FullName -Destination $destExe -Force
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Saved $destExe"
