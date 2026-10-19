<#
.SYNOPSIS
    CyberPipe release gates G1-G6 (TASK-093).
.DESCRIPTION
    Runs the full release pipeline: clean Release build (G1), unit tests (G2),
    architecture tests plus forbidden-identifier scan (G3), model catalog
    validation 15+8 (G4), multi-platform publish (G5) and automated smoke checks
    (G6), then writes release-manifest.json with SHA-256 hashes.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File eng/release.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File eng/release.ps1 -SkipPublish
#>
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "artifacts"),
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [switch]$SkipSmoke
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $RepoRoot

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) {
        throw "Step failed: $Name (exit $LASTEXITCODE)."
    }
}

# Version: single source of truth is Directory.Build.props <Version>.
[xml]$props = Get-Content (Join-Path $RepoRoot "Directory.Build.props")
$Version = $props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is missing in Directory.Build.props."
}
Write-Host "CyberPipe release $Version" -ForegroundColor Green

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$validation = [ordered]@{}

# G1: full clean Release build, warnings are errors (Directory.Build.props).
Invoke-Step "G1 build" {
    dotnet build CyberPipe.slnx -c $Configuration --nologo -v q
}
$validation["build"] = "passed"

# G2: full unit test suite.
Invoke-Step "G2 unit tests" {
    dotnet test CyberPipe.slnx -c $Configuration --no-build --nologo
}
$validation["unitTests"] = "passed"

# G3: architecture tests + forbidden identifier scan (single DB, no extra stores).
Invoke-Step "G3 architecture tests" {
    dotnet test tests/CyberPipe.Tests.Architecture -c $Configuration --no-build --nologo
}
$forbidden = @("Hangfire", "RabbitMQ", "Kafka", "MassTransit", "NServiceBus", "Grpc")
$hits = @()
foreach ($token in $forbidden) {
    $found = Get-ChildItem -Path src, tests -Include *.cs, *.csproj -Recurse |
        Select-String -Pattern $token -SimpleMatch -List |
        Select-Object -ExpandProperty Path
    if ($found) {
        $hits += $found | ForEach-Object { "$token in $_" }
    }
}
if ($hits.Count -gt 0) {
    $hits | ForEach-Object { Write-Error $_ }
    throw "Forbidden identifiers found."
}
$sqliteProjects = Get-ChildItem -Path src -Include *.csproj -Recurse |
    Select-String -Pattern "SQLite" -SimpleMatch -List
if ($sqliteProjects) {
    $sqliteProjects | ForEach-Object { Write-Error $_.Path }
    throw "SQLite references found in src."
}
$validation["forbiddenScan"] = "passed"

# G4: model catalogs — shipped files contain exactly 15 local + 8 cloud models.
Invoke-Step "G4 model catalogs" {
    dotnet test tests/CyberPipe.Tests.Unit -c $Configuration --no-build --nologo --filter "FullyQualifiedName~ModelCatalog"
}
$localCount = (Get-Content src/CyberPipe.Models/Catalogs/local-models.json -Raw |
    Select-String -Pattern '"id":' -AllMatches).Matches.Count
$cloudCount = (Get-Content src/CyberPipe.Models/Catalogs/cloud-models.json -Raw |
    Select-String -Pattern '"id":' -AllMatches).Matches.Count
if ($localCount -ne 15 -or $cloudCount -ne 8) {
    throw "Catalog counts are $localCount/$cloudCount, expected 15/8."
}
$validation["catalogCounts"] = "15 local, 8 cloud"
$validation["catalogTests"] = "passed"

$artifacts = @()
if (-not $SkipPublish) {
    # G5: publish all processes, Desktop on three RIDs.
    $publishMatrix = @(
        @{ Project = "src/CyberPipe.Service/CyberPipe.Service.csproj"; Rid = "win-x64" },
        @{ Project = "src/CyberPipe.Cli/CyberPipe.Cli.csproj"; Rid = "win-x64" },
        @{ Project = "src/CyberPipe.Desktop/CyberPipe.Desktop.csproj"; Rid = "win-x64" },
        @{ Project = "src/CyberPipe.Desktop/CyberPipe.Desktop.csproj"; Rid = "linux-x64" },
        @{ Project = "src/CyberPipe.Desktop/CyberPipe.Desktop.csproj"; Rid = "osx-x64" }
    )
    foreach ($entry in $publishMatrix) {
        $name = [IO.Path]::GetFileNameWithoutExtension($entry.Project)
        $target = Join-Path $OutputDir "$name-$($entry.Rid)"
        Invoke-Step "G5 publish $name $($entry.Rid)" {
            dotnet publish $entry.Project -c $Configuration -r $entry.Rid --self-contained false -o $target --nologo -v q
        }
        Get-ChildItem -Path $target -File -Recurse | ForEach-Object {
            $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $artifacts += [ordered]@{
                path   = [IO.Path]::GetRelativePath($OutputDir, $_.FullName).Replace("\", "/")
                sha256 = $hash
            }
        }
    }
    $validation["publish"] = "passed"
}

if (-not $SkipSmoke) {
    # G6: automated smoke — published Service validates its own configuration.
    $serviceExe = Join-Path $OutputDir "CyberPipe.Service-win-x64/CyberPipe.Service.exe"
    if (Test-Path $serviceExe) {
        Invoke-Step "G6 smoke validate-config" {
            & $serviceExe --validate-config
        }
        $validation["validateConfig"] = "passed"
    }
    else {
        $validation["validateConfig"] = "skipped (publish skipped)"
    }
}

$manifest = [ordered]@{
    name          = "CyberPipe"
    version       = $Version
    date          = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    configuration = $Configuration
    validation    = $validation
    artifacts     = $artifacts
    manualSmoke   = @(
        "Install PostgreSQL 16 + pgvector, run bootstrap, start Service, check health endpoint",
        "Send Telegram commands to Owner chat, verify replies in the same chat",
        "Run a 4-step chain on each trigger kind, restart Service mid-run, verify checkpoint recovery",
        "Open Desktop UI, verify theme toggle, service controls and multilingual labels"
    )
}
$manifestPath = Join-Path $OutputDir "release-manifest.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host ""
Write-Host "Release manifest: $manifestPath" -ForegroundColor Green
