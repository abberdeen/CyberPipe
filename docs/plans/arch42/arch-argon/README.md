> **Уточнение владельца 2026-10-19:** этот вариант - аудит старых формулировок. Хранение канонически - одна PostgreSQL + pgvector; конкретная шина/брокер не фиксируется, код пишется через собственные интерфейсы с возможностью замены транспорта. При расхождении верны [DECISIONS.md](../../DECISIONS.md) и [17-adr-mvp.md](../../initial_arch/17-adr-mvp.md).

# CyberPipe Architecture Documentation Suite (`arch-argon`)

> **System Under Analysis:** **CyberPipe** — Local-First, Zero-Trust, Resource-Governed Autonomous Personal Assistant (`.NET 10 / C# 14`)
> **Repository State at Analysis:** **Pre-Implementation Specification Phase (`RISK-004`)** — `CyberPipe.slnx` is a 14-byte empty solution stub (`<Solution />`) with `0` `.cs` files and `0` `.csproj` files. All 19 planning documents in [`docs/plans/`](../plans/README.md) and 6 skill definitions in `.agents/skills/` were analyzed as primary architectural inputs.
> **Overall Architectural Verdict:** **Conditionally Viable — Requires 4 Critical Pre-Coding Corrections (`RISK-001`, `RISK-002`, `RISK-003`, `RISK-005`) and Phased Vertical-Slice Delivery (`RISK-004`).**

---

## 1. Purpose and Multi-Methodology Approach

This directory (`docs/arch-argon/`) contains the authoritative, evidence-based software, security, and reliability architecture documentation for **CyberPipe**. Rather than passively restating [`docs/plans/`](../plans/README.md), this suite stress-tests the proposed design using four complementary methodologies (detailed in [`00-methodology.md`](00-methodology.md)):

1. **arc42 (`02-arc42/`):** 12-part structural decomposition covering goals, constraints, context boundaries, solution strategy, 3-level building blocks (3 executables $\rightarrow$ 16-project DAG $\rightarrow$ critical components), 5 runtime sequence views, deployment topology, and cross-cutting mechanics.
2. **ATAM — Architecture Tradeoff Analysis Method (`03-atam/`):** Utility tree prioritization, 14 measurable 6-part Quality Attribute Scenarios (`QAS-001`..`QAS-014`), sensitivity points (`SP-01`..`SP-08`), trade-off points (`TP-01`..`TP-07`), non-risks (`NR-01`..`NR-06`), and a 16-item scored Risk Register (`RISK-001`..`RISK-016`).
3. **Threat Modeling — STRIDE + Agentic Zero-Trust (`04-threat-model/`):** Catalog of 10 high-value assets (`ASSET-01`..`ASSET-10`), 5 trust boundaries (`TB-01`..`TB-05`), 12 annotated data flows (`DF-01`..`DF-12`), 16 concrete threats (`THREAT-001`..`THREAT-016`), and 12 verifiable security controls (`MIT-01`..`MIT-12`).
4. **ADR — Markdown Architectural Decision Records (`05-adr/`):** 8 comprehensive decision records (`ADR-0001`..`ADR-0008`) that formalize core architectural choices and supersede/correct flawed baseline assumptions in [`docs/plans/17-adr-mvp.md`](../plans/17-adr-mvp.md).

---

## 2. Document Hierarchy and Navigation Map

```text
docs/arch-argon/
├── README.md                                           # [You Are Here] Master Entry Point & Readiness Summary
├── 00-methodology.md                                   # Methodological Framework (arc42, ATAM, STRIDE, MADR) & References
├── 01-overview.md                                      # Repo Inventory (28 files), Discovered Blockers & Master Traceability (REQ-001..020)
├── 02-arc42/
│   ├── 01-introduction-and-goals.md                    # Business/Technical Goals, Stakeholders, Non-Goals & REQ-001..020
│   ├── 02-architecture-constraints.md                  # Technical, OS, Security, Budget & Licensing Constraints
│   ├── 03-context-and-scope.md                         # Business & Technical Context, External Actors & TB-01..05
│   ├── 04-solution-strategy.md                         # Core Architectural Decisions & Quality Achievement Strategies
│   ├── 05-building-block-view.md                       # Level 1 (3 Exes), Level 2 (16-Project DAG), Level 3 (Critical Components)
│   ├── 06-runtime-view.md                              # 5 Sequence Diagrams (Fast-Path, Tier 3/Stop, Brainstem, Netrunner DAG, OTT)
│   ├── 07-deployment-view.md                           # Single-Host Topology, Session >= 1 Rule, Directory Layout & NTFS ACLs
│   ├── 08-crosscutting-concepts.md                     # Deep Dive: SQLCipher/WAL, MassTransit Outbox, Vault, Taint, Bulkheads
│   ├── 09-architecture-decisions.md                    # Summary Index & Dependency Graph of ADR-0001..0008
│   ├── 10-quality-requirements.md                      # ATAM Utility Tree & Summary Matrix of QAS-001..014
│   ├── 11-risks-and-technical-debt.md                  # Risk Heat Map, RISK-001..016 Summary & Specification Debt Reconciliation
│   └── 12-glossary.md                                  # Canonical Terminology & Resolution of Cross-Document Naming Drift
├── 03-atam/
│   ├── quality-attribute-scenarios.md                  # Full 6-Part Specifications for QAS-001..QAS-014
│   ├── tradeoff-analysis.md                            # Sensitivity Points (SP-01..08), Trade-Offs (TP-01..07) & Non-Risks (NR-01..06)
│   └── risk-register.md                                # Scored Risk Register (RISK-001..RISK-016) & Consolidated Risk Themes
├── 04-threat-model/
│   ├── scope-assets-and-trust-boundaries.md            # Protected Assets (ASSET-01..10), Actors, Adversaries & TB-01..05
│   ├── data-flows.md                                   # Annotated Mermaid DFD, Data Flows (DF-01..12) & Taint State Machine
│   ├── threat-register.md                              # 16 STRIDE + Agentic Threats (THREAT-001..THREAT-016)
│   └── mitigations-and-verification.md                 # Security Control Matrix (MIT-01..12), Residual Risks & Test Mapping
├── 05-adr/
│   ├── README.md                                       # ADR Governance Rules & Index
│   ├── template.md                                     # Extended MADR 4.0.0 Template
│   ├── 0001-example-decision.md                        # ADR-0001: 4-Tier Intent Router, C# Orchestrator & 16-Project DAG
│   ├── 0002-message-bus-and-transport-strategy.md      # ADR-0002: MassTransit v8 InMemory + SQLite Outbox (Fixes RISK-001)
│   ├── 0003-decentralized-sqlite-and-sqlcipher-encryption.md # ADR-0003: 9-DB SQLite WAL + SQLCipher Cache=Private (Fixes RISK-002/008)
│   ├── 0004-brainstem-process-isolation-and-sandboxing.md    # ADR-0004: Dual-Pool Brainstem & OS Sandboxing (Fixes RISK-006/007)
│   ├── 0005-ipc-security-and-hangfire-dashboard-auth.md      # ADR-0005: Dual-Contour Named Pipe IPC & Loopback OTT Auth (Fixes RISK-009)
│   ├── 0006-secrets-vault-and-egress-sanitization.md   # ADR-0006: SecretsVault (DPAPI + Argon2id Kit), Tool Stripping & Sanitizer
│   ├── 0007-proactive-scheduling-and-netrunner-dag-engine.md # ADR-0007: Dapper Scheduler & Pre-Compiled Netrunner DAG (Fixes RISK-003/005)
│   └── 0008-token-budget-governance-and-crash-recovery.md    # ADR-0008: Atomic TokenBudgetGovernor, <50ms /stop & InFlightOperations
└── 06-validation/
    ├── assumptions-and-open-questions.md               # ASSUMPTION-001..012, Open Questions (OQ-01..10) & Test Plan (TEST-001..015)
    ├── technology-compatibility-matrix.md              # Package, Driver, OS API & License Audit Matrix
    └── architecture-review-checklist.md                # Actionable Go/No-Go Engineering Gate Checklist (CHK-01..06)
```

---

## 3. Current Architecture Status & Top Unresolved Blockers

Because `CyberPipe.slnx` currently has **`0` source files (`RISK-004`)**, all components and ADRs are in **`Proposed`** status. Before writing application code, the engineering team must adopt the following **five critical architectural corrections** uncovered by this review:

| Priority | Blocker / Defect Discovered in `docs/plans/` | Root Cause | Mandatory Architectural Resolution in `arch-argon` |
| :---: | :--- | :--- | :--- |
| **P0** | **[`RISK-001`](03-atam/risk-register.md#risk-001): Non-Existent `MassTransit.SqlTransport.Sqlite` & v9 License** | `03-tech-stack-libraries.md` & `17-adr-mvp.md` (`ADR-001`) specify `MassTransit.SqlTransport.Sqlite`, which **does not exist** (MassTransit SQL only supports Postgres/SQL Server), and v9 is commercial paid. | Adopt **[`ADR-0002`](05-adr/0002-message-bus-and-transport-strategy.md)**: Pin `MassTransit` `8.x` (`< 9.0.0`, Apache 2.0) using `InMemory` + a custom Dapper/SQLCipher **Transactional Outbox** in `data/subsystems/masstransit.db`. |
| **P0** | **[`RISK-002`](03-atam/risk-register.md#risk-002): `SQLitePCLRaw` Native Clash & `Cache=Shared` Hazard** | Referencing `Microsoft.Data.Sqlite` or `Hangfire.Storage.SQLite` loads unencrypted `e_sqlite3`, silently ignoring `Password=...` (`THREAT-006`). `Cache=Shared` (`09-data-protection-and-state-hub.md` L175) causes `SQLITE_LOCKED` and weakens key validation. | Adopt **[`ADR-0003`](05-adr/0003-decentralized-sqlite-and-sqlcipher-encryption.md)**: Ban `Microsoft.Data.Sqlite` metapackage; use `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher` with **`Cache=Private`** and a startup binary header check. |
| **P0** | **[`RISK-003`](03-atam/risk-register.md#risk-003): Netrunner `64 MB` Roslyn (`main.csx`) OOM & Missing `BrainstemTaskKind`** | `16-netrunner-etl-and-workflow-engine.md` runs in-memory Roslyn (`main.csx`, which needs `150–300 MB` RAM) inside a `64 MB` sandbox—guaranteeing an immediate `OutOfMemoryException`. | Adopt **[`ADR-0007`](05-adr/0007-proactive-scheduling-and-netrunner-dag-engine.md)**: Pre-compile `main.csx` **on the host** during `NetrunnerPackageValidator` into a restricted `.dll`/`.wasm` module (`< 25 MB` RAM) executed in `ISandboxProvider` (`TB-05`). |
| **P1** | **[`RISK-006`](03-atam/risk-register.md#risk-006) & [`RISK-007`](03-atam/risk-register.md#risk-007): Windows `AppContainer` ACLs & `DevTools` Code Exec** | `AppContainer` fails outside `Program Files` without explicit `Allow RX` ACLs, while `RestrictedToken` can read `data/vault.bin` without an explicit `Deny` ACE. `dotnet build/test` and `git` execute arbitrary MSBuild targets and `.git/hooks`. | Adopt **[`ADR-0004`](05-adr/0004-brainstem-process-isolation-and-sandboxing.md)**: Programmatically set `Allow RX` on `./worker/` + explicit NTFS **`Deny` ACE on `./data/`**; enforce `HITL` approval and `-c core.hooksPath=/dev/null` in `DevToolProcessRunner`. |
| **P1** | **[`RISK-008`](03-atam/risk-register.md#risk-008) & [`RISK-014`](03-atam/risk-register.md#risk-014): Orphan Tables & `DPAPI` Single Point of Data Loss** | `InFlightOperations`, `SemanticIntentCache`, and `SemanticResponseCache` had no assigned `.db` file in `09-data-protection-and-state-hub.md`, and `DPAPI` profile loss bricks all 9 DBs. | Assign `InFlightOperations` to `cyberpipe_master.db` and caches to `knowledge.db` ([`ADR-0003`](05-adr/0003-decentralized-sqlite-and-sqlcipher-encryption.md)); export an **`Argon2id` Offline Emergency Recovery Kit** during `cyberpipe init` ([`ADR-0006`](05-adr/0006-secrets-vault-and-egress-sanitization.md)). |

---

## 4. Recommended Implementation Sequence (4-Phase Vertical Slice Roadmap)

To mitigate [`RISK-004`](03-atam/risk-register.md#risk-004), do **not** scaffold all 16 projects and 16 skills simultaneously. Execute in four verifiable phases:

1. **Phase 0 — Walking Skeleton & Security Foundation (`Gates: TEST-001, TEST-002, TEST-006, TEST-013`):**
   Scaffold `CyberPipe.Contracts`, `CyberPipe.Security` (`SecretsVault` + `CentralDataSanitizer`), `CyberPipe.State` (`SQLCipher` `Cache=Private` self-test), `CyberPipe.Host`, `CyberPipe.Cli` (Contour 1 Named Pipe), and `CyberPipe.Architecture.Tests` (`ArchUnitNET`).
2. **Phase 1 — Core Pipeline, `Brainstem` Isolation & Governance (`Gates: TEST-004, TEST-005, TEST-007, TEST-008, TEST-010`):**
   Implement `CyberPipe.Core` (`PipelineOrchestrator`, `< 50 ms` `/stop`, `masstransit.db` Outbox), `CyberPipe.Cognition` (`Tier 0/1` Router, `TokenBudgetGovernor`), `CyberPipe.Sandbox`, and `CyberPipe.Brainstem` (Pools A & B with NTFS `Deny` ACL on `./data/`).
3. **Phase 2 — Channels, Proactive Scheduling & Pre-Compiled `Netrunner` DAG (`Gates: TEST-003, TEST-009, TEST-011`):**
   Implement `CyberPipe.Channels` (`Telegram.Bot`), `CyberPipe.Scheduling` (Dapper/SQLCipher store + `OTT` dashboard), and `CyberPipe.Netrunner` (host pre-compiler + Mini-n8n DAG runner).
4. **Phase 3 — Domain Skills, Voice, Hardware & Telemetry (`Gates: TEST-012, TEST-014, TEST-015`):**
   Implement `CyberPipe.Skills` (CRM, Finance, Health, Docs, Email, DevTools), `CyberPipe.Voice`, `CyberPipe.Hardware` (`VolumeClamp`, `SystemResourceMonitor`), and `CyberPipe.Telemetry` (`LogCompressor`).

