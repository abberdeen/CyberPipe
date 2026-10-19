# ATAM — Tradeoff Analysis

## Sensitivity points

| ID | Decision | Sensitive attribute | Why |
| :--- | :--- | :--- | :--- |
| `SP-01` | In-process Tier-1 ONNX embedder | Latency, cost, availability | Model load failure or RAM pressure silently converts free traffic into billable Tier 2/3 load |
| `SP-02` | Cache key `(QueryHash, UserRole, DataVersion, ExpiresAtUtc)` | Correctness/privacy | Any missing key element reintroduces cross-question or cross-role leakage |
| `SP-03` | Single-writer SQLite per DB file | Throughput, durability | Write-behind, Hangfire, bus, and skills contend on one writer; queue depth decides latency |
| `SP-04` | SQLCipher provider choice | Confidentiality, startup, portability | Wrong provider/version = no encryption or no Windows/Linux/macOS parity |
| `SP-05` | `MaxOutputTokens` + turn limits | Cost vs task completion | Too tight starves reasoning models mid-task; too loose reopens burn-rate risk |
| `SP-06` | AppContainer vs Restricted-Token profiles | Compatibility vs isolation | GPU drivers may force the weaker-audited profile B |

## Tradeoff points (competing attributes)

| ID | Tradeoff | Side A | Side B | Resolution in plans |
| :--- | :--- | :--- | :--- | :--- |
| `TP-01` | In-process ONNX vs out-of-process inference | +5–10 ms, no IPC | Crash/memory isolation | In-process accepted; Brainstem covers only opaque native parsing |
| `TP-02` | InMemory vs SQLite vs RabbitMQ bus | Zero-dep start vs durability vs distribution | Loses restarts / adds crypto+ops burden / adds broker ops | Tiered offer: default InMemory, opt-in SQLite, enterprise RabbitMQ |
| `TP-03` | Tool truncation (1 500 chars) vs agent visibility | −99% token cost | May hide the failing line | Head+tail+error-line heuristic; full log on disk for drill-down |
| `TP-04` | Fail-closed OSINT vs availability | Blocks on outage | Unknown domains stall Tier-3 tasks | `RequiresApproval` escape hatch (human decides) |
| `TP-05` | Single-file-per-subsystem DBs vs one database | Blast-radius isolation, per-key rotation | Cross-entity queries, backup orchestration ×9 | Isolation wins; joins move to application layer |
| `TP-06` | Hangfire + Netrunner checkpoints | Industrial scheduling vs overlapping persistence | Two sources of truth for "scheduled work" | Accepted duplication, flagged `DEBT-003` |

## Risk themes (cutting across components)

1. **Unproven boundaries.** Encryption (RISK-001) and cross-OS isolation (RISK-002)
   are the two guarantees the whole safety story rests on — both await PoCs.
2. **Silent degradation into spend.** Every local-first shortcut (ONNX, cache, ollama
   health, Cloud Freeze) needs a defined degraded mode, or failures convert to
   cloud bills instead of errors (RISK-006).
3. **One-writer physics.** SQLite WAL is not a server DB: backup consistency, migration
   serialization, and write contention need explicit design, not assumption (RISK-007).
4. **Pattern-based security.** Sanitizer, AST blocklists, NSFW wordlists are
   enumerations of known-bad; each needs a fail-closed default and adversarial tests
   (RISK-005, THREAT-006/007).
