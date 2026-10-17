<#
.SYNOPSIS
    Displays RabbitMQ processes, ports, credentials and ping status on Windows.
#>

$rabbitSbin = (Get-ChildItem -Path "$HOME\RabbitMQ\*\sbin", "C:\Program Files\RabbitMQ Server\*\sbin" -ErrorAction SilentlyContinue |
    Sort-Object { [version]($_.Parent.Name -replace '^[^\d]*', '') } -Descending |
    Select-Object -First 1).FullName

Write-Host "=== Process Status ===" -ForegroundColor Cyan
$procs = Get-Process erl, epmd -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Select-Object Id, ProcessName, @{Name="RAM_MB"; Expression={[math]::Round($_.WorkingSet64/1MB, 2)}}, Path | Format-Table -AutoSize
} else {
    Write-Host "No active erl.exe or epmd.exe processes found." -ForegroundColor Yellow
}

Write-Host "=== Port Connectivity ===" -ForegroundColor Cyan
$amqp = Test-NetConnection -ComputerName 127.0.0.1 -Port 5672 -WarningAction SilentlyContinue
$mgmt = Test-NetConnection -ComputerName 127.0.0.1 -Port 15672 -WarningAction SilentlyContinue
Write-Host "Port 5672 (AMQP): $(if ($amqp.TcpTestSucceeded) { 'OPEN' } else { 'CLOSED' })"
Write-Host "Port 15672 (Management UI): $(if ($mgmt.TcpTestSucceeded) { 'OPEN' } else { 'CLOSED' })"

if ($rabbitSbin -and (Test-Path "$rabbitSbin\rabbitmqctl.bat")) {
    Write-Host "`n=== Node Diagnostics ($rabbitSbin) ===" -ForegroundColor Cyan
    & "$rabbitSbin\rabbitmqctl.bat" ping

    Write-Host "`n=== Version Info ===" -ForegroundColor Cyan
    & "$rabbitSbin\rabbitmqctl.bat" version

    Write-Host "`n=== Users ===" -ForegroundColor Cyan
    & "$rabbitSbin\rabbitmqctl.bat" list_users
}
