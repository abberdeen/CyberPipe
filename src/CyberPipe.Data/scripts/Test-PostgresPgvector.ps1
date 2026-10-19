<#
.SYNOPSIS
    Проверяет локальную установку PostgreSQL + pgvector для CyberPipe (TASK-010).

.DESCRIPTION
    Через psql выполняет SELECT 1, показывает версию сервера и проверяет, что расширения
    vector и pgcrypto доступны (pg_available_extensions). Пароль запрашивает сам psql
    (или берётся из PGPASSWORD окружения вызывающего); скрипт пароли не хранит и не печатает.

.EXAMPLE
    ./Test-PostgresPgvector.ps1 -HostName localhost -Port 5432 -User postgres
#>
[CmdletBinding()]
param(
    [string]$HostName = "localhost",
    [int]$Port = 5432,
    [string]$User = "postgres",
    [string]$Database = "postgres",
    [string]$PsqlPath = "psql"
)

$ErrorActionPreference = "Stop"

$psql = Get-Command $PsqlPath -ErrorAction SilentlyContinue
if (-not $psql) {
    Write-Error "psql not found ('$PsqlPath'). Install PostgreSQL or pass -PsqlPath 'C:\Program Files\PostgreSQL\16\bin\psql.exe'."
    exit 2
}

Write-Host "Checking PostgreSQL at $HostName`:$Port (user $User, database $Database)..."
$version = & $psql.Source -h $HostName -p $Port -U $User -d $Database -t -A -c "SELECT 1; SELECT current_setting('server_version');"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot connect (exit $LASTEXITCODE). Check that the PostgreSQL service is running: Get-Service postgresql*"
    exit 1
}
Write-Host "Server version: $($version[-1])"

$extensions = & $psql.Source -h $HostName -p $Port -U $User -d $Database -t -A -c "SELECT name FROM pg_available_extensions WHERE name IN ('vector','pgcrypto') ORDER BY name;"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Extension check failed (exit $LASTEXITCODE)."
    exit 1
}

foreach ($name in @("pgcrypto", "vector")) {
    if ($extensions -contains $name) {
        Write-Host "OK: extension '$name' is available."
    }
    else {
        Write-Warning "MISSING: extension '$name' is not available on this server."
        if ($name -eq "vector") {
            Write-Warning "Install pgvector (see src/CyberPipe.Data/README.md, variant A step 2) and restart the PostgreSQL service."
        }
    }
}

if ($extensions -contains "vector") {
    Write-Host "pgvector check passed."
    exit 0
}

exit 1
