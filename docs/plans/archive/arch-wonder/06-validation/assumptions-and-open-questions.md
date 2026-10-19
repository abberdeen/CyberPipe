# Assumptions and Open Questions

`ASSUMPTION-xxx` items the architecture depends on. Each names its impact and the
cheapest reliable way to discharge it. Resolved items move to ADRs/risks; nothing is
deleted — resolution is recorded with date.

| ID | Assumption / question | Impact if wrong | Cheapest verification |
| :--- | :--- | :--- | :--- |
| `ASSUMPTION-001` | A SQLCipher provider exists for .NET 10 on Windows 10/11 with `rekey` | At-rest encryption story collapses | 1-day PoC: open-right/fail-wrong/rotate (blocks RISK-001) |
| `ASSUMPTION-002` | AppContainer permits Whisper CPU inference + ONNX loads (else profile B) | Two-profile complexity becomes mandatory, not optional | `doctor` GPU/profile probe on reference machines |
| `ASSUMPTION-003` | Bus↔DB exactly-once needs an outbox/inbox pattern (design gap, not assumption of existence) | Duplicate side effects on redelivery | Specify per-transport outbox before Sprint 3 |
| `ASSUMPTION-004` | Advisor RAM formula (`Q4×1.35+1 GB`) approximates Ollama reality | Wrong 🟢 verdicts → swap/freezes | Calibrate against measured Ollama RSS at 8K ctx |
| `ASSUMPTION-005` | `MassTransit.SqlTransport.Sqlite` + `Hangfire.Storage.SQLite` are available/maintained for .NET 10 | Bus/scheduler persistence redesign | NuGet + repo-history check with version pinning (RISK-003); methodology links were live-checked 2026-10-18 but package facts were not |
| `ASSUMPTION-006` | `GlobalStateHub` staleness is tolerable without a freshness label | UI acts on dead state as live | Add snapshot-timestamps to the DTO now (cheap) |
| `ASSUMPTION-007` | Backups leave the machine (off-disk copy procedure exists) | Single disk failure = total loss incl. backups | Write the 3-2-1-lite procedure pre-release |
| `ASSUMPTION-008` | Telegram-account-takeover response (revoke/rotate/re-key) is documented before release | Takeover = host control with no playbook | Owner runbook + Guest-minimization default |
| `ASSUMPTION-009` | Netrunner `main.*` runtimes (Roslyn/Python/JS) each have a sandboxed executor | Package standard promises what runtime can't deliver | One spike per runtime in Phase 0 |
| `ASSUMPTION-010` | Reference hardware + p95/p99 method stands (ADR-0006 numbers reachable) | Sprint DoDs unachievable | First BenchmarkDotNet pass decides; retune numbers, not method |

Cross-links: RISK-001…010, THREAT-001…014, ADR-0001…0006, `technology-compatibility-matrix.md`.
