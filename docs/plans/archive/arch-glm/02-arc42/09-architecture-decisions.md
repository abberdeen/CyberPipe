# arc42 §9 — Architecture Decisions

Part of the [arc42 documentation](01-introduction-and-goals.md). Decision records live in
[05-adr/](../../05-adr/). This section is an index plus status.

---

## 9.1 Decision index

| ADR | Title | Status | Evidence | Notes |
| :--- | :--- | :--- | :--- | :--- |
| [ADR-001 (plan)](../../plans/17-adr-mvp.md) | Message bus: MassTransit (`InMemory` default; `SQLite WAL` optional; `RabbitMQ` enterprise) | **Accepted in plan, PoC pending** | [`17-adr-mvp.md`](../../plans/17-adr-mvp.md), `AUD-02`, `AUD-10` | Closes the `Channels` vs `MassTransit` contradiction. PoC: integration test of delivery + retries + idempotency. |
| [ADR-002 (plan)](../../plans/17-adr-mvp.md) | DB layout: `cyberpipe_master.db` + `data/subsystems/*.db` | **Accepted in plan** | [`17-adr-mvp.md`](../../plans/17-adr-mvp.md), `AUD-03` | Closes the `cyberpipe.db`/`data/hangfire.db` path contradictions. PoC: test creates all DBs from registry on clean machine. |
| [ADR-003 (plan)](../../plans/17-adr-mvp.md) | SQLite encryption via SQLCipher provider | **Accepted in plan, hypothesis until PoC** | [`17-adr-mvp.md`](../../plans/17-adr-mvp.md), `AUD-04` | PoC: wrong key fails to open, right key opens. Until then, treat DB-at-rest as **not confidential**. |
| L4 (plan) | Target OS MVP: Windows 10/11 only | **Accepted in plan** | [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) | Linux/macOS after first working version. |
| L5 (plan) | DevTools (`dotnet`/`git`) forbidden outside sandbox | **Accepted in plan, design incomplete** | [`17-adr-mvp.md`](../../plans/17-adr-mvp.md), `AUD-08` | Binary whitelist insufficient; needs sandbox routing. |
| L6 (plan) | SLO methodology: local budgets vs end-to-end Telegram best-effort | **Accepted in plan** | [`17-adr-mvp.md`](../../plans/17-adr-mvp.md), `AUD-06` | Reference hardware: ≥4 cores, SSD NVMe; `p95/p99`. |

## 9.2 Decisions that cannot yet be made

The following are **Proposed/Unresolved** ADRs that should be opened in
[05-adr/](../../05-adr/) once PoC evidence exists. Until then, the architecture is
intentionally non-committal.

| Candidate ADR | Why blocked | Alternatives | Revisit trigger |
| :--- | :--- | :--- | :--- |
| OS-sandbox equivalence | `AUD-13`: `AppContainer`/`bwrap`/`Seatbelt` equivalence unverified per OS | (a) Windows-only MVP first (chosen L4); (b) per-OS feature matrix; (c) drop non-Windows | PoC negative tests pass on each target OS, or differences documented. |
| IPC protocol specification | `AUD-09`: PID/Nonce asserted, not specified end-to-end | (a) Full nonce + PID + command-authz protocol + negative tests; (b) mTLS over UDS (heavier) | Before Brainstem implementation. |
| Dev-tool sandboxing strategy | `AUD-08`: `dotnet`/`git` execute arbitrary project code | (a) Route all `dotnet`/`git` through Brainstem/sandbox; (b) explicit per-repo trust with signed manifests; (c) drop dev-tool skills from MVP | Before implementing dev skills (Phase 3). |
| SQLCipher integration package | `AUD-04`: which provider + version | (a) `SQLitePCLRaw.bundle_e_sqlcipher`; (b) `Microsoft.Data.Sqlite` + SQLCipher native; (c) `SQLitePCLRaw.bundle_e_sqlcipher` vs alternate | PoC opens DB with wrong key (fails) / right key (succeeds). |
| MassTransit consumer idempotency contracts | `AUD-02`: per-consumer contracts not specified | (a) Mandate `MessageDeduplicationId` on every consumer; (b) per-consumer dedup table | Before switching off `InMemory`. |
| Netrunner script execution isolation | `AUD-07`: manifest SHA-256 ≠ execution isolation | (a) Mandatory OS-sandbox routing for every `NetrunnerScriptNode`; (b) Wasmtime-only for Roslyn scripts; (c) reject non-sandboxable runtimes | PoC: script cannot read outside `workspace/out/`. |
| Central package management | No `Directory.Packages.props` exists | (a) Central package management in repo; (b) per-project `<PackageReference>` | Before first production release. |
| Master-key loss recovery runbook | `AUD-14`: procedure described, not tested | (a) Tested restore runbook; (b) Shamir-split key backup; (c) accept irrecoverability | Tested restore from backup pre-release. |
| CyberPipe distribution license | Not specified anywhere in plans | (a) Proprietary; (b) OSS license | Before any public release. |

## 9.3 Decisions implied by the design (record for audit)

These are not ADRs yet, but the design commits to them. Each should become an ADR.

| Implied decision | Where it appears | Suggested ADR |
| :--- | :--- | :--- |
| Daemon runs in interactive user session, not Session 0 | [`15`](../../plans/15-cli-and-execution-modes.md) §11 | ADR-004 (proposed) |
| `BackgroundServiceExceptionBehavior.Ignore` | [`14`](../../plans/14-fault-isolation-and-resilience.md) §1 | ADR-005 (proposed) |
| Bounded channels only | [`14`](../../plans/14-fault-isolation-and-resilience.md) §3 | ADR-006 (proposed) |
| Egress sanitizer never rehydrates `[REDACTED:ID]` | [`09`](../../plans/09-data-protection-and-state-hub.md) §1 | ADR-007 (proposed) |
| `MemoryTrustSource` taint propagation + Hard Tool Stripping | [`05`](../../plans/05-security-agent.md) §3.3 | ADR-008 (proposed) |
| Tier-1 ONNX embeddings in-process (not in Brainstem) | [`15`](../../plans/15-cli-and-execution-modes.md) §1.3 | ADR-009 (proposed) |
| Generative local LLMs via Ollama (not embedded) | [`15`](../../plans/15-cli-and-execution-modes.md) §1.2 | ADR-010 (proposed) |

See [05-adr/README.md](../../05-adr/README.md) for the ADR workflow and the illustrative
example [05-adr/0001-example-decision.md](../../05-adr/0001-example-decision.md).
