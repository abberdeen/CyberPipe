<#
.SYNOPSIS
    CyberPipe release gates G1-G6 (TASK-093).
.DESCRIPTION
    One-command release pipeline: clean Release build (G1), all test levels
    (G2: unit, data, architecture, e2e incl. TASK-091/092), architecture and
    composition bans (G3), model catalog data checks (G4), multi-RID publish
    (G5) and automated smoke on the published bits (G6). Writes
    release-manifest.json with SHA-256 hashes. Any failed gate stops the run
    and names the gate; the manifest is still written with per-gate statuses.
    Exit code is 0 only when every gate passes.
.EXAMPLE
    pwsh eng/release.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File eng/release.ps1 -SkipPublish
#>
[CmdletBinding()]
param(
    [string]$OutputDir = "",
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [switch]$SkipSmoke,
    [string]$SmokeDbHost = "localhost",
    [int]$SmokeDbPort = 5433,
    [string]$SmokeDbName = "cyberpipe_test",
    [string]$SmokeDbUser = "cyberpipe",
    [string]$SmokeDbPassword = "cyberpipe-test-pw",
    [int]$SmokeTimeoutSec = 120
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScriptDir = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    Split-Path $PSCommandPath -Parent
}
else {
    $PSScriptRoot
}
$RepoRoot = Split-Path $ScriptDir -Parent
Set-Location $RepoRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $ScriptDir "artifacts"
}

$gates = @()
function Add-GateResult([string]$Id, [string]$Status, [string]$Detail, [long]$DurationMs) {
    $script:gates += [ordered]@{
        id         = $Id
        status     = $Status
        detail     = $Detail
        durationMs = $DurationMs
    }
    $color = "Red"
    if ($Status -eq "passed") {
        $color = "Green"
    }
    Write-Host "GATE $Id : $Status - $Detail" -ForegroundColor $color
}

function Invoke-Gate([string]$Id, [scriptblock]$Body) {
    Write-Host ""
    Write-Host "=== $Id ===" -ForegroundColor Cyan
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $detail = & $Body
        $sw.Stop()
        Add-GateResult $Id "passed" "$detail" $sw.ElapsedMilliseconds
    }
    catch {
        $sw.Stop()
        Add-GateResult $Id "failed" "$($_.Exception.Message)" $sw.ElapsedMilliseconds
        throw
    }
}

# Version: single source of truth is Directory.Build.props <Version>.
[xml]$props = Get-Content (Join-Path $RepoRoot "Directory.Build.props")
$Version = $props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is missing in Directory.Build.props."
}
$Commit = "unknown"
$Dirty = $false
try {
    $Commit = (git rev-parse HEAD 2>$null | Out-String).Trim()
    $dirtyLines = (git status --short 2>$null | Out-String).Trim()
    $Dirty = -not [string]::IsNullOrWhiteSpace($dirtyLines)
}
catch {
}
Write-Host "CyberPipe release $Version (commit $Commit$(if ($Dirty) { ', dirty tree' } else { '' }))" -ForegroundColor Green

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$script:logsDir = Join-Path $OutputDir "logs"
New-Item -ItemType Directory -Force -Path $script:logsDir | Out-Null
$script:artifacts = @()
$script:measured = [ordered]@{}
$script:findings = @()
$script:failedGate = $null

function Assert-LastExit([string]$What) {
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}

# Runs a native command with the error stream contained (the script runs with
# $ErrorActionPreference=Stop, which would otherwise abort on the first
# stderr line and swallow the evidence). Full output goes to a per-gate log
# file; success returns a short summary, failure throws with the tail.
function Invoke-Native([string]$GateId, [string]$What, [scriptblock]$Cmd) {
    $logPath = Join-Path $script:logsDir "$GateId.log"
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $out = @(& $Cmd 2>&1 | ForEach-Object { "$_" })
    }
    finally {
        $ErrorActionPreference = $prev
    }
    $out | Set-Content -Path $logPath -Encoding UTF8
    if ($LASTEXITCODE -ne 0) {
        $tail = ($out | Select-Object -Last 12) -join "`n"
        throw "$What failed with exit code $LASTEXITCODE (log: logs/$GateId.log).`n$tail"
    }
    return $out
}

try {
# G1: full clean Release build (--no-incremental: content files such as model
# catalogs must be recopied from src on every release, never carried stale
# from a previous build). Warnings are errors (Directory.Build.props).
Invoke-Gate "G1-build" {
    Invoke-Native "g1-build" "dotnet build" { dotnet build CyberPipe.slnx -c $Configuration --no-incremental --nologo -v q }
    "slnx $Configuration, 0 warnings/errors (log: logs/g1-build.log)"
}

# G2: every test level in the solution (unit, data, architecture, e2e incl.
# TASK-091/092). E2E provisions its own test PostgreSQL when missing.
Invoke-Gate "G2-tests" {
    Invoke-Native "g2-tests" "dotnet test" { dotnet test CyberPipe.slnx -c $Configuration --no-build --nologo }
    "slnx $Configuration, all levels green (log: logs/g2-tests.log)"
}

# G3: architecture bans. Forbidden tokens in sources (the guard's own denylist
# file is excluded — it names the tokens to forbid them), in package refs,
# plus structural checks: Contracts dependency-free, single database.
Invoke-Gate "G3-bans" {
    # Files that legitimately name the tokens: the product denylist itself
    # and the architecture tests that enforce the bans.
    $excluded = @(
        "src/CyberPipe.Service/Composition/ServiceComposition.cs",
        "tests/CyberPipe.Tests.Architecture/ContractsBoundaryTests.cs",
        "tests/CyberPipe.Tests.Architecture/ProjectGraphTests.cs"
    ) | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $RepoRoot $_)) }
    $tokens = @("Hangfire", "RabbitMQ", "Kafka", "MassTransit", "NServiceBus", "ServiceBus", "Grpc", "Guest", "DevTools")
    $hits = @()
    foreach ($token in $tokens) {
        $found = Get-ChildItem -Path src, tests -Include *.cs -Recurse |
            Where-Object { $excluded -notcontains [IO.Path]::GetFullPath($_.FullName) } |
            Select-String -Pattern $token -CaseSensitive -List |
            Select-Object -ExpandProperty Path
        if ($found) {
            $hits += $found | ForEach-Object { "$token in $_" }
        }
    }
    $pkgHits = Get-ChildItem -Path src, tests -Include *.csproj -Recurse |
        Select-String -Pattern "Hangfire|RabbitMQ|Kafka|MassTransit|NServiceBus|ServiceBus|Grpc|SQLite" -List |
        Select-Object -ExpandProperty Path
    if ($pkgHits) {
        $hits += $pkgHits | ForEach-Object { "package-ref in $_" }
    }
    if ($hits.Count -gt 0) {
        $hits | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "Forbidden identifiers found: $($hits.Count)."
    }

    $contractsRefs = Select-String -Path src/CyberPipe.Contracts/CyberPipe.Contracts.csproj -Pattern "Reference"
    if ($contractsRefs) {
        throw "CyberPipe.Contracts must not reference projects or packages."
    }

    $dbLiterals = Get-ChildItem -Path src -Include *.cs -Recurse |
        Select-String -Pattern 'Database\s*=\s*"([^"]+)"' -AllMatches |
        ForEach-Object { $_.Matches } |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
    $allowedDb = @("cyberpipe", "postgres")
    $foreignDb = @($dbLiterals | Where-Object { $allowedDb -notcontains $_ })
    if ($foreignDb.Count -gt 0) {
        throw "Second database literals found: $($foreignDb -join ', ')."
    }
    "tokens/csproj/contracts/single-db clean"
}

# G4: release data. Catalogs parsed as JSON (exactly 15 local + 8 cloud),
# catalog unit tests, fixture manifests only under tests/.
Invoke-Gate "G4-data" {
    $local = (Get-Content src/CyberPipe.Models/Catalogs/local-models.json -Raw | ConvertFrom-Json).models.Count
    $cloud = (Get-Content src/CyberPipe.Models/Catalogs/cloud-models.json -Raw | ConvertFrom-Json).models.Count
    if ($local -ne 15 -or $cloud -ne 8) {
        throw "Catalog counts are $local/$cloud, expected 15/8."
    }
    Invoke-Native "g4-catalog-tests" "ModelCatalog tests" { dotnet test tests/CyberPipe.Tests.Unit -c $Configuration --no-build --nologo --filter "FullyQualifiedName~ModelCatalog" }
    $strayManifests = @(Get-ChildItem -Path . -Filter "netrunner.manifest.json" -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch "[\\/]tests[\\/]" -and $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
        Select-Object -ExpandProperty FullName)
    if ($strayManifests.Count -gt 0) {
        throw "skill-hack manifests outside tests/: $($strayManifests -join '; ')."
    }
    "catalogs 15+8, manifests only in tests/"
}

# G5: publish Service + CLI (win-x64) and Desktop (win/linux/osx, framework-
# dependent). Records path + SHA-256 + size; product version must match.
if (-not $SkipPublish) {
    Invoke-Gate "G5-publish" {
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
            Invoke-Native "g5-publish-$name-$($entry.Rid)" "publish $name $($entry.Rid)" { dotnet publish $entry.Project -c $Configuration -r $entry.Rid --self-contained false -o $target --nologo -v q }
            $targetFull = [IO.Path]::GetFullPath($target)
            $outFull = [IO.Path]::GetFullPath($OutputDir)
            Get-ChildItem -Path $target -File -Recurse | ForEach-Object {
                $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                $rel = [IO.Path]::GetFullPath($_.FullName).Substring($outFull.Length).TrimStart('\', '/').Replace("\", "/")
                $script:artifacts += [ordered]@{
                    path   = $rel
                    sha256 = $hash
                    size   = $_.Length
                }
            }
        }

        # No model files, private keys or secret values in publications.
        $badExt = @(".onnx", ".bin", ".gguf", ".safetensors", ".pt", ".pth", ".ckpt", ".pfx", ".pem", ".key")
        $badFiles = @(Get-ChildItem -Path $OutputDir -File -Recurse |
            Where-Object { $badExt -contains $_.Extension.ToLowerInvariant() } |
            Select-Object -ExpandProperty FullName)
        if ($badFiles.Count -gt 0) {
            throw "Forbidden files in publish: $($badFiles -join '; ')."
        }
        $secretHits = @(Get-ChildItem -Path $OutputDir -Filter "*.json" -Recurse |
            Select-String -Pattern '"Password"\s*:\s*"[^"]+"' -List |
            Select-Object -ExpandProperty Path)
        if ($secretHits.Count -gt 0) {
            throw "Secret values in publish JSON: $($secretHits -join '; ')."
        }

        # One version everywhere: manifest version must prefix each win-x64 exe.
        foreach ($exe in @("CyberPipe.Service-win-x64/CyberPipe.Service.exe",
                           "CyberPipe.Cli-win-x64/CyberPipe.Cli.exe",
                           "CyberPipe.Desktop-win-x64/CyberPipe.Desktop.exe")) {
            $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $OutputDir $exe))
            if (-not $info.ProductVersion.StartsWith($Version)) {
                throw "Version mismatch in ${exe}: $($info.ProductVersion) vs $Version."
            }
        }
        "$($script:artifacts.Count) files, versions match $Version"
    }
}
else {
    Add-GateResult "G5-publish" "skipped" "SkipPublish" 0
}

# G6: automated smoke on the published bits (Windows): validate-config, live
# console start against the test PostgreSQL (env-only config, pristine
# artifacts), CLI diagnostics, clean stop. Credential seeding uses the same
# UTF-8 Credential Manager encoding as the product (cmdkey writes UTF-16 and
# would store garbage); a pre-existing credential is reused and left alone.
if (-not $SkipSmoke) {
    if ($SkipPublish) {
        Add-GateResult "G6-smoke" "skipped" "publish skipped" 0
    }
    elseif ($env:OS -ne "Windows_NT") {
        Add-GateResult "G6-smoke" "skipped" "Credential Manager smoke needs Windows" 0
    }
    else {
        Invoke-Gate "G6-smoke" {
            Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SmokeCreds {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential {
        public uint Flags; public uint Type; public string TargetName;
        public string Comment; public long LastWritten; public uint CredentialBlobSize;
        public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount;
        public IntPtr Attributes; public string TargetAlias; public string UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential c, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr p);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr p);
    public static bool Exists(string target) {
        IntPtr p;
        if (!CredRead(target, 1, 0, out p)) return false;
        CredFree(p); return true;
    }
    public static void WriteUtf8(string target, string user, string secret) {
        byte[] blob = Encoding.UTF8.GetBytes(secret);
        IntPtr ptr = Marshal.AllocHGlobal(blob.Length);
        try {
            Marshal.Copy(blob, 0, ptr, blob.Length);
            var c = new NativeCredential { Type = 1, TargetName = target,
                CredentialBlobSize = (uint)blob.Length, CredentialBlob = ptr,
                Persist = 2, UserName = user };
            if (!CredWrite(ref c, 0)) throw new InvalidOperationException("CredWrite failed.");
        } finally { Marshal.FreeHGlobal(ptr); }
    }
    public static void Delete(string target) { CredDelete(target, 1, 0); }
}
"@
            $credTarget = "CyberPipe/database.password"
            $seededByGate = $false
            if (-not [SmokeCreds]::Exists($credTarget)) {
                [SmokeCreds]::WriteUtf8($credTarget, $SmokeDbUser, $SmokeDbPassword)
                $seededByGate = $true
            }

            try {
                $svcExe = Join-Path $OutputDir "CyberPipe.Service-win-x64/CyberPipe.Service.exe"
                $cliExe = Join-Path $OutputDir "CyberPipe.Cli-win-x64/CyberPipe.Cli.exe"
                Invoke-Native "g6-validate-config" "published validate-config" { & $svcExe --validate-config }

                $env:CYBERPIPE_Database__Host = $SmokeDbHost
                $env:CYBERPIPE_Database__Port = "$SmokeDbPort"
                $env:CYBERPIPE_Database__Database = $SmokeDbName
                $env:CYBERPIPE_Database__Username = $SmokeDbUser
                $svcLog = Join-Path $script:logsDir "g6-service-console.log"
                Remove-Item $svcLog -ErrorAction SilentlyContinue
                $svc = Start-Process -FilePath $svcExe -ArgumentList "--console" `
                    -WorkingDirectory (Split-Path $svcExe) `
                    -RedirectStandardOutput $svcLog -PassThru
                try {
                    $sw2 = [Diagnostics.Stopwatch]::StartNew()
                    $healthy = $false
                    $deadline = (Get-Date).AddSeconds($SmokeTimeoutSec)
                    while ((Get-Date) -lt $deadline) {
                        Start-Sleep 5
                        if ($svc.HasExited) { break }
                        $log = Get-Content $svcLog -Raw -ErrorAction SilentlyContinue
                        if ($log -match '"Step":"Database","Status":"OK"' -and
                            $log -match 'CyberPipe Service started\. Version') {
                            $healthy = $true
                            break
                        }
                    }
                    $script:measured["serviceStartupMs"] = $sw2.ElapsedMilliseconds
                    if (-not $healthy) {
                        throw "Service did not reach healthy startup within ${SmokeTimeoutSec}s (see logs/g6-service-console.log)."
                    }

                    $dsw = [Diagnostics.Stopwatch]::StartNew()
                    $diagOut = Invoke-Native "g6-cli-diagnostics" "CLI diagnostics" { & $cliExe diagnostics }
                    if (($diagOut -join "`n") -notmatch "Diagnostics report") { throw "CLI diagnostics failed." }
                    $script:measured["cliDiagnosticsMs"] = $dsw.ElapsedMilliseconds

                    # Known gap (TASK-062 wiring, product scope): CliContext never
                    # gets IServiceControl, so `service status` cannot reach the
                    # live pipe. Captured as evidence, tracked as a finding.
                    # NOTE: the probe is expected to fail; contain the error
                    # stream or $ErrorActionPreference=Stop aborts the gate.
                    $prevEap = $ErrorActionPreference
                    $ErrorActionPreference = "Continue"
                    try {
                        $statusOut = (& $cliExe service status 2>&1 | Out-String)
                    }
                    finally {
                        $ErrorActionPreference = $prevEap
                    }
                    $script:findings += [ordered]@{
                        id     = "F-CLI-SERVICE-CONTROL"
                        text   = "CLI `service status` reports disconnected against a healthy service; CliContext.ServiceControl is never assigned (src/CyberPipe.Cli/Program.cs). Product fix belongs to the CLI area task, not the release script."
                        output = $statusOut.Trim()
                    }
                }
                finally {
                    if (-not $svc.HasExited) {
                        Stop-Process -Id $svc.Id -Force -ErrorAction SilentlyContinue
                    }
                    $svc.WaitForExit(15000) | Out-Null
                    if (-not $svc.HasExited) {
                        throw "Service process did not stop."
                    }
                }
                "validate-config + healthy console start + diagnostics + clean stop"
            }
            finally {
                if ($seededByGate) {
                    [SmokeCreds]::Delete($credTarget)
                }
                Remove-Item Env:CYBERPIPE_Database__Host -ErrorAction SilentlyContinue
                Remove-Item Env:CYBERPIPE_Database__Port -ErrorAction SilentlyContinue
                Remove-Item Env:CYBERPIPE_Database__Database -ErrorAction SilentlyContinue
                Remove-Item Env:CYBERPIPE_Database__Username -ErrorAction SilentlyContinue
            }
        }
    }
}
else {
    Add-GateResult "G6-smoke" "skipped" "SkipSmoke" 0
}
}
catch {
    $script:failedGate = $_.Exception.Message
    Write-Host ""
    Write-Host "RELEASE STOPPED: $script:failedGate" -ForegroundColor Red
}

# Manifest is always written so a red run still reports per-gate evidence.
$manifest = [ordered]@{
    name          = "CyberPipe"
    version       = $Version
    commit        = $Commit
    dirtyTree     = $Dirty
    date          = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    configuration = $Configuration
    gates         = $gates
    artifacts     = $script:artifacts
    slo           = [ordered]@{
        note    = "Reference only (Q11/DECISIONS): targets are goals, never release blockers."
        targets = [ordered]@{
            tier0Ms        = "< 1"
            tier1Ms        = "5-15"
            fastPathMs     = "< 20"
            stopMs         = "< 50"
            cliIpcP95Ms    = "< 25"
        }
        measured = $script:measured
    }
    manualChecklist = @(
        [ordered]@{ item = "Real Telegram bot (TASK-091 scenario on a live bot)"; status = "not-run" },
        [ordered]@{ item = "HITL buttons in Telegram and Avalonia"; status = "not-run" },
        [ordered]@{ item = "Local Qwen3 model install via Ollama (TASK-081 tool)"; status = "not-run" },
        [ordered]@{ item = "Voice input (TASK-052)"; status = "not-run" },
        [ordered]@{ item = "Windows service restart from MMC/Desktop"; status = "not-run" },
        [ordered]@{ item = "First-run wizard completed in Desktop (offers multilingual-e5-small)"; status = "not-run" },
        [ordered]@{ item = "CLI service status/stop/start against installed service (blocked: F-CLI-SERVICE-CONTROL)"; status = "blocked" }
    )
    findings = $script:findings
}
$manifestPath = Join-Path $OutputDir "release-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host ""
Write-Host "Release manifest: $manifestPath" -ForegroundColor Green

$blocked = @($manifest.manualChecklist | Where-Object { $_.status -eq "blocked" })
$notRun = @($manifest.manualChecklist | Where-Object { $_.status -eq "not-run" })
if ($blocked.Count -gt 0) {
    Write-Host "BLOCKED manual items ($($blocked.Count)):" -ForegroundColor Red
    $blocked | ForEach-Object { Write-Host "  - $($_.item)" -ForegroundColor Red }
}
Write-Host "Manual checklist: $($notRun.Count) not-run (by design — human acceptance)."

if ($script:failedGate) {
    exit 1
}
