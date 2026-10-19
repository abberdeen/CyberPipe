# Architecture Review and Implementation Readiness Checklist

> **Document Role:** Provides a practical, evidence-gated engineering checklist across all 10 mandatory review domains specified in Section 11 of the architecture mandate.
> **Usage:** Use this checklist as the formal **Go / No-Go Gate** for transitioning from **Phase 0 (Walking Skeleton)** to **Phase 1–3 Implementation**, and for transitioning ADRs (`ADR-0001`..`ADR-0008`) from `Proposed` to `Accepted`.

---

## 1. Functional Completeness & Scope Alignment

| ID | Review Question | Why It Matters | Expected Evidence | Completion Criteria (Pass/Fail Gate) |
| :--- | :--- | :--- | :--- | :--- |
| **`CHK-01.1`** | Are all 20 architectural requirements (`REQ-001`..`REQ-020`) traced to specific source documents, building blocks, quality scenarios, and verification tests? | Prevents silent requirement drops or unowned capabilities across the 16 proposed projects. | Master Traceability Matrix in [`../01-overview.md`](../01-overview.md#5-master-requirements-traceability-matrix). | `100%` of `REQ-001`..`REQ-020` have an assigned project owner, `QAS-*`, `ADR-*`, and `TEST-*`. |
| **`CHK-01.2`** | Have all cross-document naming and parameter contradictions in `docs/plans/` (`RISK-016`) been reconciled into a single canonical contract? | Divergent enum names (`OwnerExplicit` vs `TrustedOwner`), CLI verbs (`schedule` vs `cron`), and turn limits (`3` vs `5`) cause compile and integration bugs across teams/agents. | Canonical Glossary in [`../02-arc42/12-glossary.md`](../02-arc42/12-glossary.md) and `CyberPipe.Contracts` assembly. | `CyberPipe.Contracts` compiles with `OriginTrustLevel`, `MaxCloudLlmTurnsPerTask = 3`, `MaxLocalLlmTurnsPerTask = 5`, and `TEST-013` passes. |
| **`CHK-01.3`** | Is the `--offline` CLI mode (`cyberpipe vault set --offline`) architecturally reconciled with the zero-dependency Native AOT constraint of `CyberPipe.Cli`? | Referencing `SQLCipher` directly in `CyberPipe.Cli` bloats the Native AOT binary and violates the 16-project DAG (`05-building-block-view.md` §5.2). | `CyberPipe.Cli.csproj` and `CyberPipe.Host` `--oneshot` handler (`OQ-05`). | `CyberPipe.Cli.csproj` references **only** `CyberPipe.Contracts` and `Spectre.Console.Cli` (`TEST-013`), delegating `--offline` mutations to `CyberPipe.exe --oneshot`. |

---

## 2. Quality Attribute Coverage & ATAM Trade-Off Completeness

| ID | Review Question | Why It Matters | Expected Evidence | Completion Criteria (Pass/Fail Gate) |
| :--- | :--- | :--- | :--- | :--- |
| **`CHK-02.1`** | Does every high-priority quality attribute (`Security`, `Reliability`, `Cost Governance`, `Performance`, `Modifiability`, `Safety`) have measurable 6-part ATAM scenarios? | Vague goals like *"fast routing"* or *"secure sandbox"* cannot be tested in CI. | [`../03-atam/quality-attribute-scenarios.md`](../03-atam/quality-attribute-scenarios.md) (`QAS-001`..`QAS-014`). | All 14 `QAS` scenarios have automated assertions in `TEST-001`..`TEST-015`. |
| **`CHK-02.2`** | Are all sensitivity points (`SP-01`..`SP-08`) and trade-off points (`TP-01`..`TP-07`) explicitly guarded by code assertions or configuration bounds? | Small misconfigurations at sensitivity points (e.g., `Cache=Shared` in `SP-02` or missing length check in `SP-07`) cause catastrophic security or reliability failures. | [`../03-atam/tradeoff-analysis.md`](../03-atam/tradeoff-analysis.md) and startup self-tests. | Startup validation rejects invalid sensitivity parameters (e.g., `Cache=Shared`, plaintext DB headers, frame `> 8 MB`) automatically. |

---

## 3. Threat Model & Zero-Trust Security Controls

| ID | Review Question | Why It Matters | Expected Evidence | Completion Criteria (Pass/Fail Gate) |
| :--- | :--- | :--- | :--- | :--- |
| **`CHK-03.1`** | Is **Hard Tool Stripping** enforced deterministically in C# (`ChatOptions.Tools = Empty`) whenever `ContextContainsUntrustedData == true`? | System-prompt instructions cannot stop indirect prompt injection (`THREAT-001`); only C# stripping the tool dictionary provides structural immunity. | `PipelineOrchestrator` & `HardToolStripper` implementation + [`TEST-005`](assumptions-and-open-questions.md#test-005). | `TEST-005` passes `100%` of 50 red-team prompt-injection payloads with `0` mutating tool invocations (`QAS-007`). |
| **`CHK-03.2`** | Does `CentralDataSanitizer` scrub all active `SecretsVault` values and PII patterns in `< 0.5 ms` with **zero rehydration** on LLM responses? | Rehydrating placeholders allows attackers to exfiltrate secrets via markdown links or echoed responses (`THREAT-003`). | `CentralDataSanitizer` implementation + [`TEST-005`](assumptions-and-open-questions.md#test-005). | `100%` redaction recall on secret/PII test corpus; `p99 < 0.5 ms`; zero rehydration logic exists in codebase (`QAS-008`). |
| **`CHK-03.3`** | Do `Brainstem`, `Netrunner`, and `DevToolProcessRunner` processes carry an explicit kernel NTFS **`AccessControlType.Deny` ACE** on `./data/` (`vault.bin` and `*.db`)? | A fallback `RestrictedToken` running under the Owner's SID can still read `./data/vault.bin` unless an explicit `Deny` ACE is applied (`RISK-006`, `THREAT-008`). | `BrainstemSupervisor` / `WindowsJobObjectSandbox` ACL setup + [`TEST-004`](assumptions-and-open-questions.md#test-004). | Child worker attempting `File.OpenRead("data/vault.bin")` throws `UnauthorizedAccessException` (`0` bytes read, `QAS-005`). |
| **`CHK-03.4`** | Does `DevToolProcessRunner` disable `.git/hooks` (`-c core.hooksPath=/dev/null`) and require `HITL` approval before `dotnet build/test` on untrusted/modified code? | `dotnet build/test` and `git` execute arbitrary code via MSBuild targets, source generators, and Git hooks (`RISK-007`, `THREAT-016`). | `DevToolProcessRunner` implementation + [`TEST-015`](assumptions-and-open-questions.md#test-015). | Canary `.git/hooks/fsmonitor` script does not execute during `TEST-015`. |
| **`CHK-03.5`** | Is the loopback `Hangfire` dashboard (`127.0.0.1:5080/hangfire`) protected by both a single-use `60 s OTT` and strict `Host` header validation? | Prevents browser-based localhost CSRF, SSRF, and DNS rebinding attacks (`THREAT-005`). | `HangfireOneTimeTokenMiddleware` + [`TEST-011`](assumptions-and-open-questions.md#test-011). | Unauthenticated, replayed-OTT, or forged-`Host` requests return `401`/`400` in `TEST-011`. |

---

## 4. Data Integrity, Encryption, and Transaction Consistency

| ID | Review Question | Why It Matters | Expected Evidence | Completion Criteria (Pass/Fail Gate) |
| :--- | :--- | :--- | :--- | :--- |
| **`CHK-04.1`** | Are `Microsoft.Data.Sqlite` (metapackage) and `Hangfire.Storage.SQLite` completely banned so `SQLitePCLRaw.bundle_e_sqlcipher` is the **sole** SQLite native provider? | Mixing `e_sqlite3` and `e_sqlcipher` causes `Password=...` to be silently ignored, leaving databases in plaintext on disk (`RISK-002`, `THREAT-006`). | `Directory.Packages.props` + [`TEST-002`](assumptions-and-open-questions.md#test-002) + [`TEST-013`](assumptions-and-open-questions.md#test-013). | Raw bytes `[0..15]` of all 9 `.db` files `!= "SQLite format 3\0"`; `PRAGMA cipher_version` is non-null (`QAS-006`). |
| **`CHK-04.2`** | Do all SQLite connections use `SqliteCacheMode.Private`, `journal_mode=WAL`, `synchronous=NORMAL`, and `busy_timeout=5000`? | `Cache=Shared` (in `09-data-protection-and-state-hub.md` L175) causes `SQLITE_LOCKED` errors that ignore `busy_timeout=5000` (`RISK-002`). | `SubsystemDatabaseManager` connection builder + [`TEST-002`](assumptions-and-open-questions.md#test-002). | Zero `SqliteCacheMode.Shared` usages in codebase; concurrent read/write stress test passes with `0` lock errors. |
| **`CHK-04.3`** | Are the three formerly orphan tables (`InFlightOperations`, `SemanticIntentCache`, `SemanticResponseCache`) assigned to `cyberpipe_master.db` and `knowledge.db` (`ADR-0003`)? | Every table requires a concrete database owner and migration script (`RISK-008`). | SQL migration scripts in `CyberPipe.State` + [`TEST-002`](assumptions-and-open-questions.md#test-002). | All 9 databases initialize and pass schema verification in `TEST-002`. |
| **`CHK-04.4`** | Does `SecretsVault` generate an offline **`Argon2id` Emergency Recovery Kit** during initialization (`RISK-014`)? | Without an offline recovery key, OS `DPAPI` profile corruption permanently bricks `vault.bin` and all 9 encrypted databases. | `SecretsVault` & `CyberPipeBackupService` + [`TEST-002`](assumptions-and-open-questions.md#test-002). | Backup archive `.cpbak` is successfully decrypted and restored using only the `Argon2id` recovery passphrase in `TEST-002`. |

---

## 5. Concurrency, Bulkheads, Preemption, and Failure Handling

| ID | Review Question | Why It Matters | Expected Evidence | Completion Criteria (Pass/Fail Gate) |
| :--- | :--- | :--- | :--- | :--- |
| **`CHK-05.1`** | Does `/stop` bypass normal command queues and terminate all `CancellationTokenSource` tokens, HTTP streams, and OS Job Objects within **`< 50 ms`**? | Guarantees immediate human override of runaway agents or scripts (`REQ-004`, `QAS-003`). | `CancellationRegistry` + [`TEST-008`](assumptions-and-open-questions.md#test-008). | `TEST-008` measures `p99 < 50.0 ms` from `/stop` ingress to OS child process termination. |
| **`CHK-05.2`** | Does `BrainstemSupervisor` validate the 4-byte Protobuf length prefix **`<= 8 MB`** *before* allocating memory on Contour 2 Named Pipes? | Prevents a compromised worker from crashing `CyberPipe.exe` via a `2 GB` allocation request (`THREAT-008`). | `BrainstemSupervisor` pipe reader + [`TEST-004`](assumptions-and-open-questions.md#test-004). | Sending `0x7FFFFFFF` length header in `TEST-004` immediately kills the worker with `0` host OOM exceptions. |
| **`CHK-05.3`** | Does `TokenBudgetGovernor` perform an **atomic `BEGIN IMMEDIATE` reservation** in `budget.db` before dispatching any paid cloud LLM request? | Prevents concurrent tasks from racing past `DailyLimitUsd` ("Denial of Wallet", `THREAT-013`). | `TokenBudgetGovernor` + [`TEST-010`](assumptions-and-open-questions.md#test-010). | 10 parallel requests against a 1-call remaining budget result in exactly 1 cloud call and 9 local fallbacks (`QAS-009`). |
| **`CHK-05.4`** | Are `Netrunner` scripts pre-compiled on the host during `NetrunnerPackageValidator` so runtime execution stays well below the **`64 MB`** sandbox cap? | In-memory Roslyn (`main.csx`) inside the sandbox requires `150–300 MB` RAM and crashes 100% of the time (`RISK-003`). | `NetrunnerPackageValidator` & `WorkflowDagRunner` + [`TEST-003`](assumptions-and-open-questions.md#test-003). | Pre-compiled package executes inside a `64 MB` Windows Job Object with peak working set `< 48 MB` (`TEST-003`). |

---

## 6. Dependency, Operational Readiness, Observability & Testability

| ID | Review Question | Why It Matters | Expected Evidence | Completion Criteria (Pass/Fail Gate) |
| :--- | :--- | :--- | :--- | :--- |
| **`CHK-06.1`** | Has `MassTransit.SqlTransport.Sqlite` been replaced with `MassTransit` `8.x` (`< 9.0.0`) + the custom Dapper/SQLCipher Outbox in `masstransit.db` (`ADR-0002`)? | `MassTransit.SqlTransport.Sqlite` does not exist on NuGet (`RISK-001`). | `Directory.Packages.props` + `OutboxPublishEndpoint` + [`TEST-001`](assumptions-and-open-questions.md#test-001). | `TEST-001` passes crash-recovery outbox replay with `0` lost or duplicated messages. |
| **`CHK-06.2`** | Do `ArchUnitNET` fitness tests enforce the 16-project DAG and verify that `./worker/` contains zero host/state/security assemblies? | Prevents architectural drift and ensures `Brainstem` workers do not carry vault/DB code (`QAS-013`). | `CyberPipe.Architecture.Tests` + [`TEST-013`](assumptions-and-open-questions.md#test-013). | `dotnet test` passes `TEST-013` in CI on every commit. |
| **`CHK-06.3`** | Does `LogCompressor` compress `1 GB` raw `.jsonl` logs into a `<= 35 KB` sanitized diagnostic digest, and does `SystemResourceMonitor` stay `< 0.1%` idle CPU? | Prevents diagnostic log analysis from blowing out LLM token budgets and protects laptop battery life (`QAS-012`). | `LogCompressor` & `SystemResourceMonitor` + [`TEST-012`](assumptions-and-open-questions.md#test-012). | `TEST-012` benchmark passes both CPU (`< 0.1%`) and compression (`<= 35 KB`) assertions. |

---

## Cross-References
- **Assumptions, Open Questions & `TEST-001`..`TEST-015` Specifications:** [`assumptions-and-open-questions.md`](assumptions-and-open-questions.md)
- **Technology Compatibility Matrix:** [`technology-compatibility-matrix.md`](technology-compatibility-matrix.md)
- **Root Architecture Entry Point:** [`../README.md`](../README.md)
