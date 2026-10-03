Write-Host "Starting MemoryGuard live execution test..."

$exePath = "C:\memory-cleaner\artifacts\portable\MemoryGuard.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "MemoryGuard.exe not found at $exePath"
    exit 1
}

$proc = Start-Process -FilePath $exePath -ArgumentList "--minimized" -PassThru
Start-Sleep -Seconds 4

# Check process state
$proc.Refresh()
$ws = $proc.WorkingSet64 / (1024 * 1024)
$priv = $proc.PrivateMemorySize64 / (1024 * 1024)

Write-Host "Process ID: $($proc.Id)"
Write-Host "Process Name: $($proc.ProcessName)"
Write-Host "Working Set (RAM): $([Math]::Round($ws, 1)) MB"
Write-Host "Private Bytes: $([Math]::Round($priv, 1)) MB"

# Check log file created
$logDir = [System.IO.Path]::Combine($env:LOCALAPPDATA, "MemoryGuard", "logs")
Write-Host "Checking log directory: $logDir"
if (Test-Path $logDir) {
    $latestLog = Get-ChildItem $logDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestLog) {
        Write-Host "Latest Log: $($latestLog.FullName)"
        Write-Host "=== RECENT LOG ENTRIES ==="
        Get-Content $latestLog.FullName | Select-Object -Last 10
        Write-Host "=========================="
    }
}

# Clean shutdown
Stop-Process -Id $proc.Id -Force
Write-Host "Live execution test successfully completed!"
