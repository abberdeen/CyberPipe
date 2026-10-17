param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [switch]$UpdateHashes
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $PackagePath -PathType Container)) {
    throw "Netrunner package directory not found: $PackagePath"
}

$resolvedDir = (Resolve-Path $PackagePath).Path
Write-Host "[NetrunnerValidator] Inspecting package at: $resolvedDir" -ForegroundColor Cyan

# 1. Verify mandatory base files
$mandatoryBaseFiles = @(
    "netrunner.manifest.json",
    "SKILL.md",
    "schema.input.json",
    "schema.output.json",
    "tests/sample.fixture.json"
)

foreach ($relFile in $mandatoryBaseFiles) {
    $fullPath = Join-Path $resolvedDir $relFile
    if (-not (Test-Path -Path $fullPath -PathType Leaf)) {
        throw "MANDATORY FILE MISSING: '$relFile' in package '$resolvedDir'"
    }
}

# 2. Parse and validate netrunner.manifest.json
$manifestPath = Join-Path $resolvedDir "netrunner.manifest.json"
$manifest = Get-Content -Path $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($manifest.slug)) { throw "Manifest missing 'slug'." }
if ([string]::IsNullOrWhiteSpace($manifest.version)) { throw "Manifest missing 'version'." }
if ($null -eq $manifest.runtime -or [string]::IsNullOrWhiteSpace($manifest.runtime.entrypoint)) {
    throw "Manifest missing 'runtime.entrypoint'."
}
if ($null -eq $manifest.io) { throw "Manifest missing 'io' section." }
if ($null -eq $manifest.permissions) { throw "Manifest missing 'permissions' section." }

$entrypointRel = $manifest.runtime.entrypoint
$entrypointFull = Join-Path $resolvedDir $entrypointRel
if (-not (Test-Path -Path $entrypointFull -PathType Leaf)) {
    throw "MANDATORY ENTRYPOINT SCRIPT MISSING: '$entrypointRel'"
}

# 3. Validate JSON syntax of schema.input.json, schema.output.json, and tests/sample.fixture.json
$null = Get-Content -Path (Join-Path $resolvedDir "schema.input.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$null = Get-Content -Path (Join-Path $resolvedDir "schema.output.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$null = Get-Content -Path (Join-Path $resolvedDir "tests/sample.fixture.json") -Raw -Encoding UTF8 | ConvertFrom-Json

# 4. Verify or update SHA-256 integrity hashes
$filesToHash = @(
    $entrypointRel,
    "schema.input.json",
    "schema.output.json",
    "SKILL.md",
    "tests/sample.fixture.json"
)

if ($UpdateHashes) {
    $newHashes = [ordered]@{}
    foreach ($f in $filesToHash) {
        $fp = Join-Path $resolvedDir $f
        $hash = (Get-FileHash -Path $fp -Algorithm SHA256).Hash.ToLowerInvariant()
        $newHashes[$f] = $hash
    }
    if ($null -eq $manifest.integrity) {
        $manifest | Add-Member -NotePropertyName "integrity" -NotePropertyValue ([pscustomobject]@{ filesSha256 = $newHashes })
    } else {
        $manifest.integrity.filesSha256 = [pscustomobject]$newHashes
    }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding UTF8
    Write-Host "[NetrunnerValidator] Updated SHA-256 hashes in netrunner.manifest.json." -ForegroundColor Green
} else {
    if ($null -eq $manifest.integrity -or $null -eq $manifest.integrity.filesSha256) {
        throw "Manifest missing 'integrity.filesSha256'."
    }
    foreach ($f in $filesToHash) {
        $fp = Join-Path $resolvedDir $f
        $actualHash = (Get-FileHash -Path $fp -Algorithm SHA256).Hash.ToLowerInvariant()
        $expectedHash = $manifest.integrity.filesSha256.$f
        if ([string]::IsNullOrWhiteSpace($expectedHash) -or $actualHash -ne $expectedHash.ToLowerInvariant()) {
            throw "SHA-256 mismatch for '$f'! Expected: '$expectedHash', Actual: '$actualHash'."
        }
    }
}

Write-Host "[NetrunnerValidator] PASS: Package '$($manifest.slug)@$($manifest.version)' is valid and complete." -ForegroundColor Green
