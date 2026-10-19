# arc42 §4 — Solution Strategy

Part of the [arc42 documentation](01-introduction-and-goals.md).

---

## 4.1 Top-level strategy

The strategy is **"deterministic-fast locally, AI-expensive only when needed, host-never-
harmed."** Six tactics implement it:

| # | Tactic | How it addresses the drivers |
| :--- | :--- | :--- |
| S1 | **4-tier intent cascade** | Drives latency (`Tier 0 < 1 ms`) and token cost (0 tokens on T0/T1). Complex AI only at T2/T3. [`04`](../../plans/04-agent-mesh-proposals.md) §1. |
| S2 | **Deterministic C# orchestrator and guards** | Latency + safety: no "LLM director" burning tokens; `HostAndMediaSafetyGuard` and `TokenBudgetGovernor` are pure code. [`11`](../../plans/11-changelog.md) §2. |
| S3 | **No raw shell from chat; compiled skills + sandboxed Tier-3** | Host integrity: chat can only call compiled `IHostSkill`s; generated code runs under `Wasmtime`/`Docker`/OS sandbox. [`05`](../../plans/05-security-agent.md) §1.B; [`01`](../../plans/01-principles.md) §5. |
| S4 | **Egress sanitization + no blind rehydration** | Secret confidentiality on every egress boundary; secrets never string-substituted back into LLM-generated commands. [`09`](../../plans/09-data-protection-and-state-hub.md) §1. |
| S5 | **Decentralized SQLite-WAL + per-DB vault keys + step checkpointing** | Recoverability (WAL + `SqliteBackupService`), blast-radius containment (per-subsystem DB), and Netrunner crash recovery (`ResumeFromCheckpoint` / `CancelledByCrash`). [`09`](../../plans/09-data-protection-and-state-hub.md) §3, [`16`](../../plans/16-netrunner-etl-and-workflow-engine.md) §4.1. |
| S6 | **Strict acyclic project DAG enforced by `ArchUnitNET`** | Modifiability/testability: UI depends only on Contracts; Skills never call Process; no unbounded channels; all workers inherit `IsolatedWorkerBase`. [`06`](../../plans/06-solution-structure.md), [`07`](../../plans/07-ui-isolation-and-arch-tests.md). |

## 4.2 Architectural principles (from [`01-principles.md`](../../plans/01-principles.md))

1. **Simplicity over enterprise bloat** — InMemory bus default, zero-dependency startup. (In tension with the full stack — `AUD-10`.)
2. **4-Tier cascade with Local vs RemoteCloud per tier** — 70–85% of requests at 0 tokens.
3. **Hard host protection from Telegram** — Anti-Wipe, Volume Clamp, media whitelist.
4. **Token-budget governor + instant kill-switch** — soft/hard limits, auto-pause, `/stop` < 50 ms.
5. **Zero-Trust Tier-3 with real sandboxes** — generated code never runs unsandboxed.
6. **Egress sanitizer + persistent vault** — secrets protected at rest and on egress.
7. **Stealth SecurityGuard** — local AST + passive OSINT, fail-closed.
8. **Lean resource use** — bounded channels, `ArrayPool`, `QuotaBoundedStream`, write-behind.
9. **"Second brain" companion** — local FTS5 + SIMD memory at 0 tokens.
10. **Decentralized DB** — master registry + per-subsystem DBs via `ISubsystemDatabaseManager`.

## 4.3 Decisive trade-offs

These trade-offs are explored in depth in [03-atam/tradeoff-analysis.md](../../03-atam/tradeoff-analysis.md)
and summarized here:

- **Latency vs. recoverability**: InMemory bus is fastest but loses in-flight messages on
  restart. SQLite/RabbitMQ add latency and duplicates but enable retries. **Trade-off
  point**: default `InMemory` + idempotent consumers; consumers must be idempotent regardless
  (`AUD-02`).
- **Security vs. simplicity**: `AppContainer`/`bwrap`/`Seatbelt` are mandatory for safety but
  conflict with "zero-dependency startup" (`AUD-10`). Resolution: OS sandboxes are core, not
  optional, but they ship with the OS.
- **Token economy vs. quality**: Tier-1 ONNX embeddings (0 tokens, ~5 ms) may mis-route;
  threshold `0.86` is a tunable. **Sensitivity point**: too low → wrong-skill invocation;
  too high → falls through to paid Tier 2.
- **Host control surface vs. host safety**: giving chat any host control (volume, media,
  lock, getfile) raises EoP risk. Mitigated by compiled-skill-only API + whitelist
  directories + egress sanitizer (`THREAT-` entries in [04-threat-model](../../04-threat-model/)).

## 4.4 Strategy-to-requirement traceability

| Strategy | Satisfies |
| :--- | :--- |
| S1 cascade | REQ-002, REQ-003, REQ-018; QAS latency/cost scenarios. |
| S2 deterministic orchestrator | REQ-005 (budget), REQ-006 (no raw shell). |
| S3 compiled skills + sandbox | REQ-006, REQ-010, REQ-011; THREATs on shell/wipe/EoP. |
| S4 egress sanitizer | REQ-012; THREATs on info disclosure. |
| S5 decentralized DB + checkpointing | REQ-008, REQ-019; QAS recoverability. |
| S6 DAG + ArchUnitNET | REQ-017; QAS modifiability/testability. |

## 4.5 What the strategy explicitly does NOT guarantee

Each entry below is a guarantee the plans **appear** to make but which is actually
unverified. They are tracked in the [risk register](../../03-atam/risk-register.md) and
[threat register](../../04-threat-model/threat-register.md).

- DB-at-rest confidentiality (depends on `AUD-04` SQLCipher PoC).
- OS sandbox equivalence across Windows/Linux/macOS (depends on `AUD-13`).
- IPC protocol resistance to squatting/replay (`AUD-09`).
- Dev-tool (`dotnet`/`git`) sandboxing via binary whitelist (`AUD-08`).
- Netrunner execution isolation beyond file integrity (`AUD-07`).
