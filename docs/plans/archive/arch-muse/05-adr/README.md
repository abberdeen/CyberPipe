# ADR Index

Format: MADR (see [template.md](template.md)). Status meanings: **Proposed**
(project-endorsed in `docs/plans/`, awaiting Phase-0 proof), **Accepted** (evidence or
explicit approval — none yet), **Rejected / Deprecated / Superseded** as marked.
No decision here is Accepted: the repository contains no implementation to validate
against, and this baseline claims no approval authority.

| ADR | Subject | Status | Resolves |
| :--- | :--- | :--- | :--- |
| [0001](0001-message-bus.md) | Message bus: MassTransit; InMemory default, SQLite WAL optional, RabbitMQ enterprise-only | Proposed | Audit L1, AUD-02, AUD-10 |
| [0002](0002-database-layout.md) | DB layout: master registry + isolated subsystem DBs | Proposed | Audit L2, AUD-03 |
| [0003](0003-sqlite-encryption.md) | SQLite encryption only via SQLCipher provider (hypothesis until PoC) | Proposed | Audit L3, AUD-04 |
| [0004](0004-target-os.md) | MVP on Windows 10/11; Linux/macOS later | Proposed | Audit L4, AUD-13 |
| [0005](0005-devtools-confinement.md) | DevTools (`dotnet`/`git`) confined to sandbox + subcommand whitelist | Proposed | Audit L5, AUD-08 |
| [0006](0006-slo-methodology.md) | SLOs are local-only budgets (p95/p99, reference HW); Telegram path best-effort | Proposed | Audit L6, AUD-06 |

Superseded/retired alternatives are recorded inside each ADR (e.g. `Channels`-as-bus,
`cyberpipe.db` naming, stock-`Password=` encryption belief). Revisit triggers are
mandatory sections — a decision without one is incomplete.
