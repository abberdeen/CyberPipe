# ADR-0002 — Master-registry + isolated per-subsystem SQLite databases

- **Status:** Proposed (project-endorsed in `docs/plans/17-adr-mvp.md`; acceptance gated on Phase-0 registry test)
- **Date:** 2026-10-17
- **Sources:** `docs/plans/09`, `08`, `17-adr-mvp.md`; audit AUD-03, L2

## Context and constraints

Three competing layouts existed (`cyberpipe_master.db + subsystems/`, `cyberpipe.db`,
`data/hangfire.db`), making migrations, backups, and isolation tests unwritable
(`C-006`). Eight subsystems plus master need independent lifecycles, per-area keys,
and Brainstem-denied ACLs.

## Decision drivers

1. One canonical path registry. 2. Blast-radius isolation + per-DB keys. 3. No
cross-domain file access (enforced by API, not convention). 4. Clean provisioning on a
fresh machine.

## Alternatives considered

| Alternative | Fit | Fatal flaw |
| :--- | :--- | :--- |
| Single monolithic SQLite file | Simple joins/backups | Shared fate: one key, one lock, one corruption domain; violates isolation goals |
| Server DB (PostgreSQL) | Concurrency, types | Mandatory server install kills zero-dep start; rejected for MVP |
| Master + `subsystems/*.db` via `ISubsystemDatabaseManager` | Matches all drivers | Cross-DB queries move to app layer; 9-file backup orchestration |

## Evidence and comparative analysis

- Verified: plans converge post-audit; legacy names declared invalid in `09` §3 + `17`.
- Vendor-documented (not tested here): SQLite WAL single-writer semantics, `user_version`
  migration pattern, Backup API.
- Inference: per-file writers + serialized migrations suffice for single-user load —
  must be benchmarked (RISK-007).

## Decision and rationale

`data/cyberpipe_master.db` holds **only** `AppGlobalSettings` + `SubsystemDatabaseRegistry`;
all subsystem state lives in `data/subsystems/*.db` (hangfire, masstransit, knowledge,
crm, finance, budget, health, netrunner). All access flows through
`ISubsystemDatabaseManager` (provision, key from vault, migrate, connect); direct
cross-domain file access is forbidden and architecturally tested.

## Consequences

+ Isolation, rotation, per-area backup/restore. − App-layer joins; − migration runner
must serialize per file; − backup procedure spans 9 files + vault.

## Risks and mitigations

RISK-001 (encryption PoC per DB), RISK-007 (contention/consistency tests).

## Validation and acceptance criteria

`TEST-DB-01`: fresh-machine test provisions all DBs from the registry; `TEST-DUR-01`:
backup/restore round-trip. Green → Accepted.

## Revisit triggers

Measured contention breaks SLOs; subsystem count makes 9-file ops untenable; server-DB
need proven by a real multi-machine requirement.

## Related ADRs and source documents

ADR-0001 (bus DB), ADR-0003 (encryption), `02-arc42/08` (migrations/backup).
