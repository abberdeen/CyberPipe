<#
.SYNOPSIS
    Gracefully stops RabbitMQ Server on Windows.
#>

$rabbitSbin = (Get-ChildItem -Path "$HOME\RabbitMQ\*\sbin", "C:\Program Files\RabbitMQ Server\*\sbin" -ErrorAction SilentlyContinue |
    Sort-Object { [version]($_.Parent.Name -replace '^[^\d]*', '') } -Descending |
    Select-Object -First 1).FullName

if ($rabbitSbin -and (Test-Path "$rabbitSbin\rabbitmqctl.bat")) {
    Write-Host "Stopping RabbitMQ node gracefully via rabbitmqctl ($rabbitSbin)..." -ForegroundColor Cyan
    & "$rabbitSbin\rabbitmqctl.bat" stop
} else {
    Write-Host "rabbitmqctl.bat not found. Terminating erl.exe processes..." -ForegroundColor Yellow
    Stop-Process -Name "erl" -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Seconds 2
$remaining = Get-Process erl -ErrorAction SilentlyContinue
if (-not $remaining) {
    Write-Host "RabbitMQ has stopped successfully." -ForegroundColor Green
} else {
    Write-Warning "Process erl.exe still running (PID $($remaining.Id)). Force stopping..."
    Stop-Process -Name "erl" -Force -ErrorAction SilentlyContinue
}
