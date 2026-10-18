# ADR-0004: Dual-Pool `Brainstem` Out-of-Process Worker Isolation and Pluggable OS Sandboxing

- **Status:** `Proposed`
- **Date:** 2026-10-18
- **Deciders:** CyberPipe Architecture Team
- **Source Plan Evidence:** [`02-architecture-overview.md`](../../plans/02-architecture-overview.md), [`06-sandbox-and-process-execution.md`](../../plans/06-sandbox-and-process-execution.md), [`14-dev-tools-and-code-agents.md`](../../plans/14-dev-tools-and-code-agents.md), [`15-cli-and-execution-modes.md`](../../plans/15-cli-and-execution-modes.md), [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-003`, `ADR-007`)
- **Related Requirements:** `REQ-002`, `REQ-014`, `REQ-015`, `REQ-020`
- **Related Quality Scenarios (ATAM):** [`QAS-005`](../03-atam/quality-attribute-scenarios.md#qas-005), [`QAS-010`](../03-atam/quality-attribute-scenarios.md#qas-010)
- **Related Risks & Threats:** [`RISK-006`](../03-atam/risk-register.md#risk-006), [`RISK-007`](../03-atam/risk-register.md#risk-007), [`THREAT-008`](../04-threat-model/threat-register.md#threat-008), [`THREAT-009`](../04-threat-model/threat-register.md#threat-009), [`THREAT-010`](../04-threat-model/threat-register.md#threat-010), [`THREAT-016`](../04-threat-model/threat-register.md#threat-016)
- **Verification Gate:** [`TEST-004`](../06-validation/assumptions-and-open-questions.md#test-004), [`TEST-015`](../06-validation/assumptions-and-open-questions.md#test-015)

---

## 1. Context and Problem Statement

CyberPipe processes untrusted external documents (`50 MB` PDFs, DOCX, HTML) and runs native C/C++ audio inference (`Whisper.net` via `ggml.dll`). If executed in-process inside `CyberPipe.exe`, a native `AccessViolationException`, stack overflow, or `2 GB` PDF decompression bomb immediately kills the main daemon (`THREAT-010`) and exposes `data/vault.bin` and all 9 SQLCipher databases in the same address space (`THREAT-008`).

Additionally, [`02-architecture-overview.md`](../../plans/02-architecture-overview.md) and [`15-cli-and-execution-modes.md`](../../plans/15-cli-and-execution-modes.md) propose running `./worker/CyberPipe.Brainstem.exe` inside a Windows `AppContainer` (`S-1-15-2-1`) or `RestrictedToken`, while [`14-dev-tools-and-code-agents.md`](../../plans/14-dev-tools-and-code-agents.md) proposes running `dotnet build/test` and `git` via `DevToolProcessRunner`. Our analysis uncovered two major hazards:
1. **`RISK-006` (Windows `AppContainer` vs `RestrictedToken` ACL Trap):** `AppContainer` denies access to `%LOCALAPPDATA%` and parent Named Pipes unless explicit `Allow` ACLs are granted on `./worker/`, `./models/`, and the pipe. Conversely, a fallback `RestrictedToken` running under the user's SID can still read `./data/vault.bin` unless an explicit NTFS **`AccessControlType.Deny` ACE** is placed on `./data/`.
2. **`RISK-007` (`DevToolProcessRunner` Code Execution):** `dotnet build/test` and `git` execute arbitrary code via MSBuild `<UsingTask>`, Roslyn Source Generators, unit tests, and `.git/hooks`.

---

## 2. Decision Drivers

- **`REQ-002` / `QAS-010`:** Guarantee `100%` survival of `CyberPipe.exe` when a worker crashes or hits OOM (`256 MB` for parsers; `2048 MB` for Whisper).
- **`REQ-015` / `QAS-005` / `THREAT-008`:** Enforce kernel-level isolation so child workers (`Brainstem`, `Netrunner`, `DevTools`) can read `0` bytes from `./data/`.
- **`RISK-006` & `RISK-007`:** Ensure sandboxes work reliably outside `C:\Program Files` and prevent arbitrary code execution via Git hooks or unapproved MSBuild targets.

---

## 3. Considered Options

1. **Option 1:** In-Process Execution of `Whisper.net` and `PdfPig` inside `CyberPipe.exe` *(Rejected — zero fault or memory isolation)*.
2. **Option 2:** Docker / WSL2 Container Isolation for Workers *(Rejected — violates Zero-Config single-host deployment constraint `CON-01`)*.
3. **Option 3 (Chosen):** Out-of-Process Dual-Pool `CyberPipe.Brainstem.exe` + Pluggable OS Native Sandboxing (`JobObject` + `RestrictedToken`/`AppContainer` + Explicit NTFS `Deny` ACL on `./data/`) + Hardened `DevToolProcessRunner`.

### Option Comparison Matrix

| Criterion | Option 1 (In-Process) | Option 2 (Docker / WSL2) | Option 3 (Dual-Pool `Brainstem` + OS Native Sandbox) |
| :--- | :--- | :--- | :--- |
| **Daemon Survival on Native Segfault / OOM** | Fails (Daemon dies) | Pass | **Pass (`BrainstemSupervisor` respawns worker)** |
| **Zero-Config Workstation Install** | Pass | Fails (Requires Docker Desktop) | **Pass (`100%` native OS APIs)** |
| **Protection of `./data/vault.bin`** | None (Same process) | Strong | **Strong (Kernel NTFS `Deny` ACE + Job Object)** |
| **Memory Overhead** | Lowest | Very High (`> 1 GB` VM) | **Low (`~30 MB` per active pool; scales to 0 on idle)** |

---

## 4. Decision Outcome

**Chosen Option:** **Option 3 — Dual-Pool Out-of-Process `CyberPipe.Brainstem.exe` Supervised via OS Job Objects and Explicit Filesystem ACLs.**

### Architectural Specification

1. **Two Isolated `Brainstem` Process Pools (`CyberPipe.Sandbox`):**
   - **Pool A (`HeavyCompute`):** Executes `WhisperTranscribe` (`Whisper.net` GGML). Configured with a Windows Job Object / Linux `cgroup` memory limit of **`2048 MB`**, hard timeout of **`120 s`**, read-only access to `./models/whisper/`, zero network access, and `5 min` idle scale-to-zero.
   - **Pool B (`UntrustedParser`):** Executes `DocumentExtractText` (`PdfPig`, `HtmlAgilityPack`, DOCX). Configured with a strict **`256 MB`** memory limit, **`15 s`** hard timeout, zero access to `./models/`, and zero network access (`QAS-010`).
2. **Mandatory ACL Provisioning Protocol (`RISK-006` Fix):**
   Before spawning any `Brainstem`, `Netrunner`, or `DevToolProcessRunner` process:
   - Create a dedicated restricted security identity (Windows Restricted SID via `CreateRestrictedToken` or `AppContainer` SID via `CreateAppContainerProfile`).
   - Grant explicit **`FileSystemRights.ReadAndExecute` (`Allow`)** for that SID on `./worker/` and (for Pool A only) `./models/`.
   - Apply an explicit **`AccessControlType.Deny` ACE (`FileSystemRights.FullControl`)** for that SID and `S-1-15-2-1` (`ALL APPLICATION PACKAGES`) on the **`./data/`** and **`./backups/`** directories. Because Windows evaluates `Deny` ACEs before `Allow` ACEs, kernel `CreateFile` calls to `./data/vault.bin` or `./data/*.db` are guaranteed to fail with `ERROR_ACCESS_DENIED` (`QAS-005`).
3. **Binary Integrity Gate (`THREAT-009`):**
   `BrainstemSupervisor` verifies the SHA-256 hash of `./worker/CyberPipe.Brainstem.exe` and all native DLLs against `./worker/worker.manifest.json` prior to every process launch.
4. **Hardened `DevToolProcessRunner` (`RISK-007` / `THREAT-016` Fix):**
   - Always append `-c core.hooksPath=/dev/null -c core.fsmonitor=false` to automated `git` commands.
   - Require explicit Owner `HITL` approval before executing `dotnet build`, `dotnet test`, or `dotnet run` on untrusted or agent-modified project files.
   - Run under a non-breakaway Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) with the `./data/` `Deny` SID and scrubbed environment variables.

---

## 5. Consequences and Trade-Offs

### Positive Consequences (`+`)
- **`+` Complete Blast-Radius Isolation (`QAS-010`):** A `50 MB` PDF bomb or `ggml.dll` crash kills only the child worker; `CyberPipe.exe` remains 100% responsive.
- **`+` Kernel-Enforced Vault Protection (`QAS-005`):** Even a full native RCE inside `Brainstem` cannot read `data/vault.bin` or open an outbound network socket.

### Negative Consequences and Trade-Offs (`-`)
- **`-` Protobuf IPC & Process Spawn Overhead (`TP-01`):** Adds `~1–3 ms` per IPC call; mitigated by keeping Pool A warm for `5 minutes` after voice activity.
- **`-` Phased OS Rollout (`CON-06`):** Windows 11 (`JobObject` + `RestrictedToken`/`AppContainer`) is the primary MVP target; Linux (`bubblewrap`) and macOS must pass platform parity gates before release.

---

## 6. Validation and Compliance Checks

- **`TEST-004` (`Brainstem` Isolation & PDF Bomb Survival):**
  1. Attempt to open `data/vault.bin` and `8.8.8.8:443` from inside `CyberPipe.Brainstem.exe` and assert `UnauthorizedAccessException` and `SocketException`.
  2. Send a synthetic `300 MB` allocation bomb to Pool B and assert the worker is killed within `<= 15 s` while `CyberPipe.exe` remains healthy.
- **`TEST-015` (`DevToolProcessRunner` Hardening):**
  Execute `git status` on a fixture repo containing a canary `.git/hooks/fsmonitor` script and assert the hook is **never** executed.
