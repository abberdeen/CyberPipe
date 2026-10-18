# ADR-0007: Proactive Scheduling and `Netrunner` 6-File Pre-Compiled ETL / Mini-n8n DAG Engine

- **Status:** `Proposed (Revises Plan ADR-008 & Netrunner Script Engine)`
- **Date:** 2026-10-18
- **Deciders:** CyberPipe Architecture Team
- **Source Plan Evidence:** [`03-tech-stack-libraries.md`](../../plans/03-tech-stack-libraries.md), [`12-proactive-automation-and-scheduling.md`](../../plans/12-proactive-automation-and-scheduling.md), [`16-netrunner-etl-and-workflow-engine.md`](../../plans/16-netrunner-etl-and-workflow-engine.md), [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-008`), [`.agents/skills/netrunner-script-package/SKILL.md`](../../../.agents/skills/netrunner-script-package/SKILL.md)
- **Related Requirements:** `REQ-011`, `REQ-013`, `REQ-020`
- **Related Quality Scenarios (ATAM):** [`QAS-011`](../03-atam/quality-attribute-scenarios.md#qas-011), [`QAS-012`](../03-atam/quality-attribute-scenarios.md#qas-012)
- **Related Risks & Threats:** [`RISK-003`](../03-atam/risk-register.md#risk-003), [`RISK-005`](../03-atam/risk-register.md#risk-005), [`THREAT-011`](../04-threat-model/threat-register.md#threat-011), [`THREAT-012`](../04-threat-model/threat-register.md#threat-012)
- **Verification Gate:** [`TEST-003`](../06-validation/assumptions-and-open-questions.md#test-003), [`TEST-009`](../06-validation/assumptions-and-open-questions.md#test-009), [`TEST-011`](../06-validation/assumptions-and-open-questions.md#test-011)

---

## 1. Context and Problem Statement

CyberPipe requires two closely linked automation capabilities:
1. **Proactive Scheduling (`REQ-011`):** Running recurring cron jobs, delayed reminders, and background triggers with quiet-hours deferral (`22:00–08:00`) and battery-saver throttling (`< 0.1%` idle CPU).
2. **`Netrunner` Deterministic ETL & Mini-n8n Workflow DAGs (`REQ-013`):** Executing 6-file ETL packages (`netrunner.manifest.json`, `schema.input.json`, `schema.output.json`, `SKILL.md`, script, `tests/`) chained into multi-node DAGs (`SkillNode`, `NetrunnerScriptNode`, `LlmProcessNode`, `HitlApprovalNode`) persisted in `data/subsystems/netrunner.db`.

Our architectural audit exposed **two high-severity technical defects (`RISK-003`, `RISK-005`)** in the baseline plans:
- **`RISK-003` (Netrunner `64 MB` Roslyn OOM & Missing `BrainstemTaskKind`):** [`16-netrunner-etl-and-workflow-engine.md`](../../plans/16-netrunner-etl-and-workflow-engine.md) specifies `"engine": "CSharpRoslyn"`, `"entrypoint": "main.csx"`, and `"maxMemoryMb": 64`. In-memory Roslyn compilation (`Microsoft.CodeAnalysis.CSharp.Scripting`) requires **`150–300 MB` RAM** to load metadata and emit IL—guaranteeing a `100%` `OutOfMemoryException` crash inside a `64 MB` OS Job Object. Furthermore, `BrainstemTaskKind` (`15-cli-and-execution-modes.md` L139) has no enum value for Netrunner scripts.
- **`RISK-005` (`Hangfire.Storage.SQLite` Incompatibility):** Community `Hangfire.Storage.SQLite` uses `sqlite-net-pcl` (incompatible with `Microsoft.Data.Sqlite.Core` + `SQLCipher` in `ISubsystemDatabaseManager`), pulls conflicting `e_sqlite3` binaries (`RISK-002`), and suffers from `SQLITE_BUSY` lock errors.

---

## 2. Decision Drivers

- **`RISK-003` / `QAS-011`:** Execute `NetrunnerScriptNode` packages reliably within a strict **`<= 64 MB`** sandbox memory cap and **`5–30 s`** timeout.
- **`RISK-005` / `QAS-006`:** Ensure `data/subsystems/hangfire.db` uses `ISubsystemDatabaseManager` (`SQLCipher` `AES-256`) with zero `e_sqlite3` native binary conflicts.
- **`THREAT-011` & `THREAT-012`:** Block malicious C# reflection/P-Invoke and enforce strict `allowedDomains` HTTP egress filtering.

---

## 3. Considered Options

1. **Option 1 (Baseline in `docs/plans/`):** In-memory `CSharpRoslyn` (`main.csx`) inside `64 MB` sandbox + `Hangfire.Storage.SQLite` *(Rejected — fails with `OutOfMemoryException` and breaks `SQLCipher`)*.
2. **Option 2 (Chosen):** **Host-Side Pre-Compilation** of `Netrunner` scripts during `NetrunnerPackageValidator` into restricted `.dll` / `.wasm` modules executed via `ISandboxProvider` (`TB-05`, `< 25 MB` RAM) + Custom Dapper/SQLCipher `Hangfire` Storage (or `Cronos` + SQLite Scheduler) in `hangfire.db`.
3. **Option 3:** Raise Netrunner Sandbox Memory Limit from `64 MB` to `512 MB` to Accommodate In-Memory Roslyn *(Rejected — wastes workstation RAM and increases DoS blast radius by 8x)*.

### Option Comparison Matrix

| Criterion | Option 1 (In-Sandbox Roslyn + `Hangfire.Storage.SQLite`) | Option 2 (Host Pre-Compiled Module + Dapper/SQLCipher Scheduler) | Option 3 (`512 MB` In-Sandbox Roslyn) |
| :--- | :--- | :--- | :--- |
| **Fits Inside `64 MB` Sandbox Cap** | **Fails (`150–300 MB` OOM)** | **Pass (`~18–28 MB` working set)** | Fails (Requires `512 MB`) |
| **Worker Startup Latency** | Poor (`1,200–2,500 ms` compile per run) | **Excellent (`< 30 ms` pre-compiled load)** | Poor (`1,200–2,500 ms`) |
| **Static AST/IL Security Inspection** | Runtime only | **Enforced once at install time (`NetrunnerPackageValidator`)** | Runtime only |
| **`hangfire.db` SQLCipher Compatibility** | **Fails (`sqlite-net-pcl` conflict)** | **100% Native via `ISubsystemDatabaseManager`** | Fails |

---

## 4. Decision Outcome

**Chosen Option:** **Option 2 — Host-Side Pre-Compilation of `Netrunner` 6-File Packages Executed in `ISandboxProvider` (`TB-05`), Paired with Dapper/SQLCipher Persistence for `hangfire.db` and `netrunner.db`.**

### Architectural Specification

#### 4.1 `Netrunner` 6-File Package Lifecycle & Pre-Compilation (`RISK-003` Fix)
1. **Authoring Contract Preserved:** Skill authors and coding agents continue to write the standard 6-file package (`netrunner.manifest.json`, `schema.input.json`, `schema.output.json`, `SKILL.md`, `main.csx`/`main.cs`, and `tests/`).
2. **Host-Side Compilation & Static Analysis Gate (`NetrunnerPackageValidator`):**
   When a package is installed or updated:
   - `NetrunnerPackageValidator` parses `main.csx` on the **host** (`CyberPipe.Netrunner`) using a Roslyn `SyntaxTree` / `Compilation` analyzer that **rejects** forbidden APIs (`System.Reflection`, `System.Runtime.InteropServices`, `System.Diagnostics.Process`, `System.Environment`, raw `System.IO.File` outside scratch temp).
   - It compiles the script into a deterministic, self-contained `.dll` assembly (or `Wasmtime` `.wasm` module), runs all `tests/input_*.json` $\rightarrow$ `expected_*.json` fixtures against `schema.input.json` / `schema.output.json`, and records the SHA-256 hash of the compiled module in `netrunner.db`.
3. **Runtime Execution in `ISandboxProvider` (`TB-05`):**
   - At workflow execution time, `WorkflowDagRunner` launches a lightweight runner via `ISandboxProvider` (`64 MB` Job Object limit, `5–30 s` timeout, explicit NTFS `Deny` ACL on `./data/`) that loads the pre-compiled `.dll`/`.wasm` directly—consuming **`< 25 MB` RAM** with zero Roslyn compiler overhead.
   - Outbound HTTP requests are mediated exclusively through `AllowedDomainHttpDelegatingHandler`, which blocks all private/loopback IPs (`127.0.0.0/8`, `10.0.0.0/8`, `192.168.0.0/16`, `169.254.0.0/16`) and restricts requests to `manifest.permissions.allowedDomains` (`THREAT-012`).

#### 4.2 Mini-n8n Workflow DAG & WAL Checkpointing (`QAS-011`)
- After every node (`SkillNode`, `NetrunnerScriptNode`, `LlmProcessNode`, `HitlApprovalNode`), `WorkflowDagRunner` validates the node's JSON output via `JsonSchema.Net` and commits a checkpoint row to `WorkflowNodeCheckpoints` in `data/subsystems/netrunner.db` (`WAL` mode).
- When a `HitlApprovalNode` is reached, the workflow state transitions to `WaitingForHitlApproval`, releasing all threads and memory until the Owner clicks `[Approve]` or `[Reject]`.

#### 4.3 Proactive Scheduler Storage (`RISK-005` Fix)
- **Ban `Hangfire.Storage.SQLite` (`sqlite-net-pcl`).** Persist all cron schedules (`Cronos`) and job states in `data/subsystems/hangfire.db` using `ISubsystemDatabaseManager` (`Microsoft.Data.Sqlite.Core` + `SQLCipher` + `Dapper`) with `WorkerCount = 1` (serializing SQLite writes) and document `Hangfire.Core` LGPL v3 attribution in `THIRD-PARTY-NOTICES.md`.

---

## 5. Consequences and Trade-Offs

### Positive Consequences (`+`)
- **`+` Solves `64 MB` Roslyn OOM (`RISK-003`):** Pre-compiled modules run in `< 25 MB` RAM and start in `< 30 ms`.
- **`+` Eliminates `sqlite-net-pcl` Clash (`RISK-005`):** `hangfire.db` is 100% SQLCipher-encrypted and free of `SQLITE_BUSY` worker contention.

### Negative Consequences and Trade-Offs (`-`)
- **`-` Install-Time Compilation Step (`TP-05`):** Takes `~1–2 s` during `cyberpipe skill install` to compile and run fixture tests.

---

## 6. Validation and Compliance Checks

- **`TEST-003` (`Netrunner` `64 MB` Sandbox & Egress Proxy Test):** Compile a sample 6-file package, run it inside a `64 MB` Windows Job Object, assert peak RAM `< 45 MB`, and assert requests to `127.0.0.1` or non-allowlisted domains are blocked.
- **`TEST-009` (DAG Crash & `HITL` Recovery Test):** Kill `CyberPipe.exe` while a 4-node DAG is paused at Node 3 (`HitlApprovalNode`), restart, send approval, and assert Nodes 1 & 2 are not re-executed.
