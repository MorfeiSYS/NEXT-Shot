# Завершает NEXTShot, если exe занят (мешает dotnet build/publish).
param(
    [int]$WaitMs = 2000
)

$names = @('NEXTShot')
$stopped = $false
foreach ($name in $names) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        Write-Host "Stopping $name (PID $($p.Id)) ..."
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        $stopped = $true
    }
}

if ($stopped) {
    Start-Sleep -Milliseconds $WaitMs
}