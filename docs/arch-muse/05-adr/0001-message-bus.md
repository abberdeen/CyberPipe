# ADR-0001 — Use MassTransit as the message bus (InMemory default, SQLite WAL optional, RabbitMQ enterprise-only)

- **Status:** Proposed (project-endorsed in `docs/plans/17-adr-mvp.md`; acceptance gated on Phase-0 tests)
- **Date:** 2026-10-17
- **Sources:** `docs/plans/01`, `03`, `08`, `17-adr-mvp.md`; audit AUD-02, AUD-10, L1

## Context and constraints

The audit found two competing bus stories (`Channels` vs `MassTransit`) with different
delivery/persistence semantics (`C-005`). A single-user local product must start with
zero dependencies, yet survive restarts for scheduled/DAG work and optionally scale to
a broker. Consumers must behave identically under transports that lose (InMemory),
redeliver (SQLite/RabbitMQ), or reorder.

## Decision drivers

1. Zero-dependency default start. 2. Transport swappability without consumer rewrites.
3. Explicit, testable delivery semantics per transport. 4. No mandatory broker for MVP.

## Alternatives considered

| Alternative | Fit | Fatal flaw |
| :--- | :--- | :--- |
| `System.Threading.Channels` as inter-service bus | Lightest | No transport abstraction, no persistence story; **rejected** (all such mentions invalid) |
| RabbitMQ mandatory | Industrial durability | Violates zero-dep start and simplicity principle for a single-user app |
| SQLite-only bus | Durable + local | fsync cost on hot paths; overkill for fast-path commands |
| MassTransit, tiered transports | Matches all four drivers | Heavier dependency; SQLite-transport package facts unverified (RISK-003) |

## Evidence and comparative analysis

- Verified (repo): no code exists; plans converge on MassTransit post-audit.
- Vendor-documented, not independently tested here: MassTransit transport abstraction
  and `InMemory` semantics (no internet at analysis — package/version reconfirmation required).
- Unverified hypothesis: `MassTransit.SqlTransport.Sqlite` availability/maintenance
  for .NET 10 — see compatibility matrix.

## Decision and rationale

Canonical bus is **MassTransit**: `InMemory` default; `SQLite WAL`
(`data/subsystems/masstransit.db`, exclusive to the bus) optional; `RabbitMQ`
enterprise-only behind its own ADR. Delivery contract: InMemory may lose on restart;
SQLite/RabbitMQ may redeliver; **all consumers idempotent via
`MessageDeduplicationId`**. Internal bounded queues stay on `Channels` (not a bus).

## Consequences

+ Swappable transports; honest semantics; fast default. − Dependency weight;
− dual programming model (bus vs in-memory channels) to document; − SQLite-transport risk.

## Risks and mitigations

RISK-003 (verify package + pin version; fallback: custom SQLite outbox, ADR amendment);
RISK-007 (single-writer contention benchmarks).

## Validation and acceptance criteria

`TEST-BUS-01`: one bus name in all docs; `TEST-BUS-02`: delivery + redelivery/idempotency
integration test per transport. Both green → Accepted.

## Revisit triggers

SQLite-transport package missing/unmaintained; InMemory loss unacceptable to scheduling;
RabbitMQ demanded by a real deployment.

## Related ADRs and source documents

ADR-0002 (DB files for the bus); `03-atam/tradeoff-analysis.md` TP-02; `02-arc42/08` (outbox gap).
