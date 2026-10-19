[CmdletBinding()]
param(
    [string]$ServiceName = "CyberPipe",
    [string]$InstallPath = "$env:ProgramFiles\CyberPipe\Service",
    [switch]$RemoveBinaries = $false
)

$ErrorActionPreference = "Stop"

function Test-Administrator {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    Write-Error "This script requires Administrator privileges. Please run PowerShell as Administrator."
    exit 1
}

Write-Host "=== Uninstalling CyberPipe Windows Service ===" -ForegroundColor Cyan

# 1. Stop service if running
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq 'Running') {
        Write-Host "Stopping service '$ServiceName'..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    # 2. Delete service registration
    Write-Host "Deleting service registration '$ServiceName'..." -ForegroundColor Yellow
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 1
    Write-Host "Service '$ServiceName' unregistered." -ForegroundColor Green
} else {
    Write-Host "Service '$ServiceName' was not registered." -ForegroundColor Yellow
}

# 3. Optionally remove binaries (never touches database or user appdata)
if ($RemoveBinaries -and (Test-Path $InstallPath)) {
    Write-Host "Removing published binaries from $InstallPath..." -ForegroundColor Yellow
    Remove-Item -Path $InstallPath -Recurse -Force
    Write-Host "Binaries removed." -ForegroundColor Green
}

Write-Host "=== Uninstallation complete ===" -ForegroundColor Green
Write-Host "Note: PostgreSQL database and user data in %APPDATA%\CyberPipe were NOT modified." -ForegroundColor Cyan
