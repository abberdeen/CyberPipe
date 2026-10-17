<#
.SYNOPSIS
    Starts RabbitMQ Server as an independent detached background process on Windows.
#>

# Auto-detect latest RabbitMQ sbin directory across User and System locations
$rabbitSbin = (Get-ChildItem -Path "$HOME\RabbitMQ\*\sbin", "C:\Program Files\RabbitMQ Server\*\sbin" -ErrorAction SilentlyContinue |
    Sort-Object { [version]($_.Parent.Name -replace '^[^\d]*', '') } -Descending |
    Select-Object -First 1).FullName

if (-not $rabbitSbin) {
    Write-Error "RabbitMQ sbin directory not found under $HOME\RabbitMQ or C:\Program Files\RabbitMQ Server\"
    exit 1
}

# Verify ERLANG_HOME or rabbitmq-env-conf.bat
$confBat = "$env:APPDATA\RabbitMQ\rabbitmq-env-conf.bat"
if (-not (Test-Path $confBat)) {
    Write-Warning "rabbitmq-env-conf.bat not found at $confBat. Checking ERLANG_HOME..."
}

# Check if already running
$erlProc = Get-Process -Name "erl" -ErrorAction SilentlyContinue
if ($erlProc) {
    Write-Host "RabbitMQ (erl.exe) is already running with PID $($erlProc.Id)."
    & "$rabbitSbin\rabbitmqctl.bat" ping
    exit 0
}

Write-Host "Launching RabbitMQ via WMI detached process from: $rabbitSbin..."
$cmd = "cmd.exe /c `"`"$rabbitSbin\rabbitmq-server.bat`"`""
$proc = [wmiclass]"Win32_Process"
$res = $proc.Create($cmd, $rabbitSbin, $null)

if ($res.ReturnValue -ne 0) {
    Write-Error "Failed to spawn process via WMI. ReturnValue: $($res.ReturnValue)"
    exit 1
}

Write-Host "Process spawned with PID $($res.ProcessId). Waiting for startup (up to 30s)..."
$started = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    $output = cmd.exe /c "`"$rabbitSbin\rabbitmqctl.bat`" ping 2>nul"
    if ($LASTEXITCODE -eq 0 -and $output -match "Ping succeeded") {
        $started = $true
        break
    }
}

if ($started) {
    Write-Host "RabbitMQ started successfully!" -ForegroundColor Green
    Write-Host "AMQP port: 5672"
    Write-Host "Management Web UI: http://localhost:15672/ (guest / guest)"
} else {
    Write-Warning "RabbitMQ is still starting up or encountered an issue. Check logs at: $env:APPDATA\RabbitMQ\log\"
}
