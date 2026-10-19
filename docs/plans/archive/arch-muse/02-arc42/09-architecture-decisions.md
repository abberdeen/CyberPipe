# arc42 §9 — Architecture Decisions

Summary only — the records of truth live in [`05-adr/`](../05-adr/README.md).

| ADR | Subject | Status | Effect |
| :--- | :--- | :--- | :--- |
| [ADR-0001](../05-adr/0001-message-bus.md) | Message bus: MassTransit; InMemory default, SQLite WAL optional, RabbitMQ enterprise-only; idempotent consumers | Proposed | Settles audit AUD-02/AUD-10; kills `Channels`-as-bus |
| [ADR-0002](../05-adr/0002-database-layout.md) | Master `data/cyberpipe_master.db` + `data/subsystems/*.db` via `ISubsystemDatabaseManager` | Proposed | Settles AUD-03; kills legacy DB names |
| [ADR-0003](../05-adr/0003-sqlite-encryption.md) | At-rest encryption only via SQLCipher provider; stock `Password=` encrypts nothing; PoC-gated | Proposed (hypothesis) | Blocks at-rest guarantees until PoC — [RISK-001](../03-atam/risk-register.md) |
| [ADR-0004](../05-adr/0004-target-os.md) | MVP on Windows 10/11; Linux/macOS after v1; cross-OS isolation equivalence unproven | Proposed | Scopes PoCs, CI, installer work |
| [ADR-0005](../05-adr/0005-devtools-confinement.md) | `dotnet`/`git` only via `IDevToolProcessRunner` whitelist inside sandbox/isolation; build output is code execution | Proposed | Closes AUD-08-shaped hole by design |
| [ADR-0006](../05-adr/0006-slo-methodology.md) | Millisecond budgets are local-only (reference HW, p95/p99); Telegram path best-effort | Proposed | Makes Sprint DoDs testable; settles AUD-06 |

No ADR is marked Accepted: acceptance requires Phase-0 evidence or explicit approval,
neither of which exists in a docs-only repository. Unresolved points and revisit
triggers are recorded inside each ADR.
