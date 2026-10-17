<#
.SYNOPSIS
    Downloads and extracts a portable Erlang/OTP installation for RabbitMQ on Windows (Non-Admin).
    Automatically resolves the latest compatible release (e.g. latest 26.x.x for RabbitMQ 3.13).
#>

param(
    [string]$TargetDir = "$HOME\ErlangOTP",
    [string]$MajorVersion = "26",
    [string]$Version = "" # Optional: specify exact version like "26.2.5.21" to override auto-discovery
)

$ErrorActionPreference = "Stop"

$sevenZip = "C:\Program Files\7-Zip\7z.exe"
if (-not (Test-Path $sevenZip)) {
    Write-Error "7-Zip is required at $sevenZip"
    exit 1
}

$downloadUrl = ""
$fileName = ""

# 1. Resolve version and download URL
if ($Version) {
    Write-Host "Using explicitly requested Erlang/OTP version: $Version" -ForegroundColor Cyan
    $fileName = "otp_win64_$Version.exe"
    $downloadUrl = "https://github.com/erlang/otp/releases/download/OTP-$Version/$fileName"
} else {
    Write-Host "Resolving latest Erlang/OTP release for major branch $MajorVersion from GitHub API..." -ForegroundColor Cyan
    try {
        $headers = @{ "User-Agent" = "PowerShell/ErlangInstaller" }
        $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/erlang/otp/releases?per_page=100" -Headers $headers -TimeoutSec 10
        $matching = $releases | Where-Object { $_.tag_name -like "OTP-$MajorVersion*" } | Select-Object -First 1
        if ($matching) {
            $asset = $matching.assets | Where-Object { $_.name -like "otp_win64_${MajorVersion}*.exe" } | Select-Object -First 1
            if ($asset) {
                $downloadUrl = $asset.browser_download_url
                $fileName = $asset.name
                $Version = $matching.tag_name -replace '^OTP-', ''
                Write-Host "Discovered latest release: $matching.tag_name ($fileName)" -ForegroundColor Green
            }
        }
    } catch {
        Write-Warning "GitHub API query failed ($($_.Exception.Message)). Falling back to known release."
    }

    # Fallback if API was unavailable or rate-limited
    if (-not $downloadUrl) {
        $fallbackVersion = "26.2.5.21"
        $fileName = "otp_win64_$fallbackVersion.exe"
        $downloadUrl = "https://github.com/erlang/otp/releases/download/OTP-$fallbackVersion/$fileName"
        $Version = $fallbackVersion
        Write-Host "Using fallback release: $Version" -ForegroundColor Yellow
    }
}

$installerTemp = "$env:TEMP\$fileName"

# 2. Download installer
Write-Host "Downloading $fileName..." -ForegroundColor Cyan
curl.exe -L -o $installerTemp $downloadUrl

if (-not (Test-Path $installerTemp) -or (Get-Item $installerTemp).Length -lt 10000000) {
    Write-Error "Download failed or file is corrupted: $installerTemp"
    exit 1
}

# 3. Extract via 7-Zip
Write-Host "Extracting installer to $TargetDir..." -ForegroundColor Cyan
& $sevenZip x $installerTemp "-o$TargetDir" -y | Out-Null

# 4. Initialize Erlang environment
Write-Host "Initializing Erlang environment via Install.exe..." -ForegroundColor Cyan
& "$TargetDir\Install.exe" -s

# 5. Clean up temporary installer
Write-Host "Cleaning up temporary installer..."
Remove-Item $installerTemp -Force -ErrorAction SilentlyContinue

# 6. Configure RabbitMQ environment override
Write-Host "Configuring RabbitMQ environment override..." -ForegroundColor Cyan
$rabbitBase = "$env:APPDATA\RabbitMQ"
if (-not (Test-Path $rabbitBase)) {
    New-Item -ItemType Directory -Path $rabbitBase -Force | Out-Null
}

$confBat = "$rabbitBase\rabbitmq-env-conf.bat"
Set-Content -Path $confBat -Value "@echo off`r`nset ERLANG_HOME=$TargetDir`r`n" -Encoding ASCII

# 7. Verification
$erlVersion = cmd.exe /c "`"$TargetDir\bin\erl.exe`" -version 2>&1"
Write-Host "Erlang installed and verified: $erlVersion" -ForegroundColor Green
Write-Host "Configuration saved to $confBat" -ForegroundColor Green
