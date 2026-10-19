# arc42 §2 — Architecture Constraints

Constraints are split into **explicit** (stated in plans/audit) and **inferred**
(analyst-derived, marked as such). All are testable obligations, not aspirations.

## Technical constraints

| ID | Constraint | Source |
| :--- | :--- | :--- |
| `C-001` | Target framework .NET 10; verified SDK `10.0.402` present on analyst machine | `docs/plans/03`, local `dotnet --list-sdks` |
| `C-002` | MVP target OS: Windows 10/11 only; Linux/macOS after first working version | `docs/plans/17-adr-mvp.md` (L4), `09` §7 |
| `C-003` | No raw shell (`cmd`/`powershell`/`bash`) and no arbitrary-URL browser opening from any chat-derived input | `docs/plans/05` §1 |
| `C-004` | Generated/parsed untrusted content runs only inside a real sandbox (OS sandbox / Wasmtime / Docker / isolated Brainstem); native host execution outside them requires explicit Owner approval | `docs/plans/05` §1, `16` §2.5 |
| `C-005` | One ADR-gated message bus (`MassTransit`); `InMemory` default, SQLite WAL optional, RabbitMQ enterprise-only; consumers idempotent (`MessageDeduplicationId`) | `docs/plans/17` (ADR-001) |
| `C-006` | DB layout: `data/cyberpipe_master.db` (settings + `SubsystemDatabaseRegistry` only) + `data/subsystems/*.db`; legacy names (`cyberpipe.db`, `data/hangfire.db`) invalid | `docs/plans/17` (ADR-002), `09` §3 |
| `C-007` | SQLite at-rest encryption **only** via a SQLCipher provider; stock `Microsoft.Data.Sqlite` `Password=` encrypts nothing — guarantee unproven until PoC | `docs/plans/17` (ADR-003), [RISK-001](../03-atam/risk-register.md) |
| `C-008` | Unique 256-bit key per subsystem DB in `SecretsVault` (DPAPI/LibSecret/Keychain); fail-closed on master-key loss | `docs/plans/09` §2–3, §7 |
| `C-009` | IPC: two separated Named-Pipe/UDS circuits, `FILE_FLAG_FIRST_PIPE_INSTANCE`, PID + single-use `SessionNonce` handshake, Owner/Guest command authorization | `docs/plans/15` §4 |
| `C-010` | `CyberPipe.UI` and `CyberPipe.Brainstem` depend **only** on `CyberPipe.Contracts`; `Skills.Host` never touches `Process`/CliWrap directly | `docs/plans/06` §DAG, `07` §2 |
| `C-011` | Only `Channel.CreateBounded<T>`; gRPC frame ≤ 4 MB; stream quota 16 MB (`QuotaBoundedStream`) | `docs/plans/14` §3, `15` §4 |
| `C-012` | Semantic response cache key must be `(QueryHash, UserRole, DataVersion, ExpiresAtUtc)`, TTL 24 h (fixed post-audit) | `docs/plans/04` §Tier 1, `08` Phase 0 |
| `C-013` | Latency budgets are **local-only** on reference hardware (4+ CPU cores, NVMe, warm process), reported as p95/p99; Telegram network path is best-effort and excluded | `docs/plans/17` (L6), `15` §2 |
| `C-014` | Daemon runs in the interactive user session (`Session ≥ 1`), never as a Session-0 service — audio/clipboard/OCR require it | `docs/plans/15` §10–11 |
| `C-015` | Hangfire dashboard: loopback-only, read-only (`IsReadOnlyFunc`), one-time-token + HttpOnly cookie handshake | `docs/plans/03` §Hangfire |

## Organizational / process constraints

| ID | Constraint | Source |
| :--- | :--- | :--- |
| `C-016` | Phase 0 (ADRs + DB registry + encryption/isolation PoCs) gates all Sprint 1–5 work | `docs/plans/08` Phase 0 |
| `C-017` | `ArchUnitNET` DAG tests + `Tests.Isolation` negative tests run from Sprint 1 / in CI | `docs/plans/07` §2, `15` §9 |
| `C-018` | `docs/plans/` and application code are out of scope for this task (analysis only) | Task statement §2 |

## Inferred constraints (analyst, require confirmation)

| ID | Constraint | Rationale |
| :--- | :--- | :--- |
| `C-019` | SQLite single-writer semantics govern all write-path design (serialized migrations, write-behind queues, one writer per DB file) | SQLite engine property; see [compatibility matrix](../06-validation/technology-compatibility-matrix.md) |
| `C-020` | Local-model VRAM/RAM fitting (weights + KV-cache + runtime headroom) must gate Tier 2/3 Local dispatch, not just advise | Advisor formula exists (`13` §2); enforcement point unspecified — [ASSUMPTION-004](../06-validation/assumptions-and-open-questions.md) |
