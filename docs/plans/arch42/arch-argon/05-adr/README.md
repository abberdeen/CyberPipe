# Architecture Decision Records (ADR Index)

> **Document Role:** Entry point and governance rules for the **CyberPipe** Architecture Decision Record (ADR) suite in `docs/arch-argon/05-adr/`.
> **Format Standard:** Extended **MADR (Markdown Architectural Decision Records) 4.0.0** enriched with ATAM sensitivity/trade-off links, STRIDE threat mitigations, and executable verification checks (`TEST-001`..`TEST-015`).

---

## 1. Epistemic Governance Rules

Per Section 4 and Section 8 of the architecture review mandate:
1. **No Premature `Accepted` Status:** Because `CyberPipe.slnx` currently contains `0` `.cs` source files (`RISK-004`) and several baseline choices in `docs/plans/17-adr-mvp.md` contain critical technical blockers (`RISK-001`, `RISK-002`, `RISK-003`), **no ADR in this directory is marked `Accepted`**.
2. **Lifecycle States:**
   - **`Proposed`:** Evidence-backed architectural decision ready for Phase 0/1 implementation and gated by an automated verification test (`TEST-*`).
   - **`Proposed (Supersedes Plan ADR-00X)`:** Revises a flawed decision in [`docs/plans/17-adr-mvp.md`](../../plans/17-adr-mvp.md) (such as replacing the non-existent `MassTransit.SqlTransport.Sqlite` package or replacing `Cache=Shared` / `main.csx` in-memory Roslyn).
   - **`Accepted`:** Reserved strictly for decisions whose verification test (`TEST-*`) passes against compiled code in `src/`.

---

## 2. Master ADR Index (`ADR-0001` .. `ADR-0008`)

| ADR ID | File Link | Title | Status | Reconciles Plan ADR | Verification Gate |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`ADR-0001`** | [`0001-example-decision.md`](0001-example-decision.md) | 4-Tier Intent Router, Deterministic C# Orchestrator & 16-Project Modular Monolith DAG *(Real Initial Decision)* | **Proposed** | `ADR-002`, `ADR-005` | `TEST-007`, `TEST-013` |
| **`ADR-0002`** | [`0002-message-bus-and-transport-strategy.md`](0002-message-bus-and-transport-strategy.md) | Message Bus & Durable Local Transport Strategy (`MassTransit` v8 `InMemory` + SQLite Outbox vs Non-Existent `SqlTransport.Sqlite`) | **Proposed (Revises Plan `ADR-001`)** | `ADR-001` | `TEST-001` |
| **`ADR-0003`** | [`0003-decentralized-sqlite-and-sqlcipher-encryption.md`](0003-decentralized-sqlite-and-sqlcipher-encryption.md) | Decentralized Local SQLite WAL Topology & Mandatory `SQLCipher` Page Encryption (`Cache=Private`) | **Proposed (Revises Plan `ADR-004`/`010`)** | `ADR-004`, `ADR-010` | `TEST-002` |
| **`ADR-0004`** | [`0004-brainstem-process-isolation-and-sandboxing.md`](0004-brainstem-process-isolation-and-sandboxing.md) | Dual-Pool `Brainstem` Out-of-Process Worker Isolation & Pluggable OS Sandboxing (`Deny` `data/` ACL) | **Proposed** | `ADR-003`, `ADR-007` | `TEST-004`, `TEST-015` |
| **`ADR-0005`** | [`0005-ipc-security-and-hangfire-dashboard-auth.md`](0005-ipc-security-and-hangfire-dashboard-auth.md) | Dual-Contour Named Pipe IPC Security & Loopback `Hangfire` Dashboard `OTT` Authentication | **Proposed** | `ADR-009` | `TEST-006`, `TEST-011` |
| **`ADR-0006`** | [`0006-secrets-vault-and-egress-sanitization.md`](0006-secrets-vault-and-egress-sanitization.md) | Hardware-Backed `SecretsVault` (`DPAPI`/`AES-256-GCM`), Hard Tool Stripping & Egress `CentralDataSanitizer` | **Proposed** | `ADR-006` | `TEST-002`, `TEST-005` |
| **`ADR-0007`** | [`0007-proactive-scheduling-and-netrunner-dag-engine.md`](0007-proactive-scheduling-and-netrunner-dag-engine.md) | Proactive `Hangfire` Scheduling & `Netrunner` 6-File Pre-Compiled ETL / Mini-n8n DAG Engine | **Proposed (Revises Plan `ADR-008`)** | `ADR-008` | `TEST-003`, `TEST-009`, `TEST-011` |
| **`ADR-0008`** | [`0008-token-budget-governance-and-crash-recovery.md`](0008-token-budget-governance-and-crash-recovery.md) | Hard-Capped `TokenBudgetGovernor`, `< 50 ms` `/stop` Preemption, Polly Bulkheads & `InFlightOperations` Recovery | **Proposed** | `ADR-002`, `ADR-005` | `TEST-008`, `TEST-010`, `TEST-012` |

---

## 3. Creating a New ADR

1. Copy [`template.md`](template.md) to `NNNN-short-title-with-dashes.md`.
2. Link all relevant `REQ-*`, `QAS-*`, `RISK-*`, `THREAT-*`, and `TEST-*` identifiers.
3. Add the new record to the index table above and to [`../02-arc42/09-architecture-decisions.md`](../02-arc42/09-architecture-decisions.md).
