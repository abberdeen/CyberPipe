[CmdletBinding()]
param(
    [string]$ServiceName = "CyberPipe",
    [string]$DisplayName = "CyberPipe Background Service",
    [string]$InstallPath = "$env:ProgramFiles\CyberPipe\Service",
    [string]$ServiceAccount = "NT AUTHORITY\LocalService",
    [switch]$DelayedAuto = $true,
    [switch]$StartAfterInstall = $true
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

Write-Host "=== Installing CyberPipe Windows Service ===" -ForegroundColor Cyan
Write-Host "Service Name   : $ServiceName"
Write-Host "Install Path   : $InstallPath"
Write-Host "Service Account: $ServiceAccount"

# 1. Stop and remove existing service if present
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Existing service '$ServiceName' found. Stopping and removing..." -ForegroundColor Yellow
    if ($existing.Status -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 1
}

# 2. Publish service binaries to InstallPath
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir

Write-Host "Publishing CyberPipe.Service project to $InstallPath..." -ForegroundColor Cyan
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

dotnet publish "$projectDir\CyberPipe.Service.csproj" -c Release -o $InstallPath --no-self-contained

$exePath = Join-Path $InstallPath "CyberPipe.Service.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found at expected path: $exePath"
    exit 1
}

# 3. Register Windows Service
Write-Host "Registering service '$ServiceName' with sc.exe..." -ForegroundColor Cyan
$startType = if ($DelayedAuto) { "delayed-auto" } else { "auto" }

$createResult = sc.exe create $ServiceName binPath= "`"$exePath`"" start= $startType obj= "`"$ServiceAccount`"" DisplayName= "`"$DisplayName`""
Write-Host $createResult

# Configure service description
sc.exe description $ServiceName "CyberPipe Local AI Assistant background service (TASK-020)."

# Configure failure recovery (restart service after 5 seconds on crash)
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000//

# 4. Grant LocalService read/execute access to install directory if needed
try {
    $acl = Get-Acl $InstallPath
    $permission = "$ServiceAccount", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow"
    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
    $acl.AddAccessRule($accessRule)
    Set-Acl $InstallPath $acl
} catch {
    Write-Warning "Failed setting ACL on $InstallPath for $ServiceAccount: $_"
}

# 5. Start service
if ($StartAfterInstall) {
    Write-Host "Starting service '$ServiceName'..." -ForegroundColor Cyan
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2
    $status = Get-Service -Name $ServiceName
    Write-Host "Service '$ServiceName' status: $($status.Status)" -ForegroundColor Green
}

Write-Host "=== Installation complete ===" -ForegroundColor Green
