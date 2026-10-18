# ADR-0002: Message Bus and Durable Local Transport Strategy (`MassTransit` v8 `InMemory` + SQLite Outbox vs. Non-Existent `SqlTransport.Sqlite`)

- **Status:** `Proposed (Revises Plan ADR-001)`
- **Date:** 2026-10-18
- **Deciders:** CyberPipe Architecture Team
- **Source Plan Evidence:** [`02-architecture-overview.md`](../../plans/02-architecture-overview.md), [`03-tech-stack-libraries.md`](../../plans/03-tech-stack-libraries.md) (L46), [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md) (L40), [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-001`)
- **Related Requirements:** `REQ-008`, `REQ-010`, `REQ-018`
- **Related Quality Scenarios (ATAM):** [`QAS-010`](../03-atam/quality-attribute-scenarios.md#qas-010), [`QAS-011`](../03-atam/quality-attribute-scenarios.md#qas-011)
- **Related Risks & Threats:** [`RISK-001`](../03-atam/risk-register.md#risk-001), [`THREAT-015`](../04-threat-model/threat-register.md#threat-015)
- **Verification Gate:** [`TEST-001`](../06-validation/assumptions-and-open-questions.md#test-001)

---

## 1. Context and Problem Statement

In [`03-tech-stack-libraries.md`](../../plans/03-tech-stack-libraries.md) and [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-001`), the baseline architecture specifies using **`MassTransit`** with three runtime-switchable transports managed by `ISubsystemTransportManager`:
1. `InMemory` (default low-overhead mode)
2. `MassTransit.SqlTransport.Sqlite` backed by `data/subsystems/masstransit.db` (durable local mode)
3. `MassTransit.RabbitMQ` (external broker mode)

Our technical investigation uncovered **three critical blockers (`RISK-001`)** in that specification:
1. **`MassTransit.SqlTransport.Sqlite` Does Not Exist:** MassTransit's SQL Transport (`MassTransit.SqlTransport`) only supports **PostgreSQL** (`MassTransit.SqlTransport.PostgreSQL`) and **SQL Server** (`MassTransit.SqlTransport.SqlServer`). There is no SQLite transport provider in MassTransit.
2. **MassTransit v9 Commercial Licensing:** MassTransit v9 transitioned to a paid commercial license (~$400/month), whereas **MassTransit v8 (`8.x`)** remains open-source under **Apache 2.0** (with official security support ending late 2026).
3. **Runtime Transport Hot-Swapping Limitation:** `MassTransit` registers `IBusControl`, `IBus`, and `IPublishEndpoint` as singletons bound to a single transport factory during `IServiceCollection` build. Calling `IBusControl.StopAsync()` cannot mutate an `InMemory` bus instance into a `RabbitMQ` bus instance in-place without rebuilding the host/child DI container or routing through a custom proxy.

---

## 2. Decision Drivers

- **`RISK-001` (Compile & NuGet Blocker):** Must use real, existing NuGet packages that support `.NET 10` and `SQLCipher` encryption (`data/subsystems/masstransit.db`).
- **License Governance:** Must avoid unexpected commercial licensing fees by pinning open-source versions (`MassTransit` `8.x` Apache 2.0) or using MIT-licensed alternatives.
- **`REQ-008` & `THREAT-015`:** Provide crash-durable, at-least-once delivery for asynchronous cross-subsystem events (e.g., recording a transaction in `finance.db` and notifying `crm.db` / `knowledge.db`) without requiring an external DBMS.

---

## 3. Considered Options

1. **Option 1 (Baseline in `docs/plans/`):** `MassTransit.SqlTransport.Sqlite` + in-place `IBusControl` hot-swap *(Rejected — technically impossible; package does not exist)*.
2. **Option 2 (Chosen):** Pin **`MassTransit` `8.x` (Apache 2.0)** using the `InMemory` transport paired with a custom ~200-line **Dapper/SQLCipher Transactional Outbox** in `data/subsystems/masstransit.db` (`Optional: MassTransit.RabbitMQ` via graceful host restart).
3. **Option 3:** Replace `MassTransit` entirely with native **`System.Threading.Channels`** + the same Dapper/SQLCipher Transactional Outbox in `masstransit.db` (or `WolverineFx` MIT).

### Option Comparison Matrix

| Criterion | Option 1 (`SqlTransport.Sqlite`) | Option 2 (`MassTransit` v8 `InMemory` + Custom SQLite Outbox) | Option 3 (`System.Threading.Channels` + Custom SQLite Outbox) |
| :--- | :--- | :--- | :--- |
| **NuGet Package Existence** | **Fails (Does Not Exist)** | **Verified (`MassTransit` 8.x + `Dapper`)** | **Verified (BCL + `Dapper`)** |
| **SQLCipher (`masstransit.db`) Support** | N/A | **100% Native via `ISubsystemDatabaseManager`** | **100% Native via `ISubsystemDatabaseManager`** |
| **Licensing** | N/A (v9 is Commercial) | **Apache 2.0 (v8 pinned `< 9.0.0`)** | **MIT (.NET BCL)** |
| **RabbitMQ Scale-Out Compatibility** | N/A | **Built-in (`MassTransit.RabbitMQ` 8.x)** | Requires manual `RabbitMQ.Client` adapter |
| **Alignment with `docs/plans/`** | Broken | **Preserves `MassTransit` consumers/sagas & `masstransit.db`** | Requires rewriting `IConsumer<T>` interfaces |

---

## 4. Decision Outcome

**Chosen Option:** **Option 2 — Pin `MassTransit` `8.x` (`< 9.0.0`, Apache 2.0) with `InMemory` transport, backed by a custom Dapper/SQLCipher Transactional Outbox in `data/subsystems/masstransit.db`, and perform transport switches (`InMemory` $\leftrightarrow$ `RabbitMQ`) via a graceful `IHost` reload.**

### Architectural Specification
1. **Version Pinning (`Directory.Packages.props`):**
   Explicitly pin `MassTransit` and `MassTransit.RabbitMQ` to the latest `8.x` release (`[8.3.*, 9.0.0)`) and add an `ArchUnitNET` / build check prohibiting `MassTransit` `>= 9.0.0` without commercial license approval.
2. **Durable SQLite Outbox in `data/subsystems/masstransit.db`:**
   - Managed by `ISubsystemDatabaseManager` (`Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher`, `WAL` mode, `Cache=Private`).
   - Schema: `OutboxMessages (MessageId TEXT PRIMARY KEY, MessageType TEXT, PayloadJson TEXT, CorrelationId TEXT, CreatedAtUtc TEXT, ProcessedAtUtc TEXT NULL, RetryCount INT, Error TEXT NULL)` and `InboxConsumedMessages (MessageId TEXT, ConsumerType TEXT, ConsumedAtUtc TEXT, PRIMARY KEY (MessageId, ConsumerType))`.
   - `OutboxPublishEndpoint` writes the event to `masstransit.db` before publishing to the `MassTransit` `InMemory` bus. A background `OutboxDispatcherHostedService` replays any unacknowledged rows on startup or failure with exponential backoff.
3. **Transport Switching (`ISubsystemTransportManager`):**
   Switching between `LocalOutbox` (`InMemory` + `masstransit.db`) and `RabbitMQ` persists the new mode in `cyberpipe_master.db`, drains `InFlightOperations`, and triggers a clean `IHost` restart (`< 2 s` downtime) so DI singletons (`IBus`) are cleanly reconstructed.

---

## 5. Consequences and Trade-Offs

### Positive Consequences (`+`)
- **`+` Resolves Critical Blocker (`RISK-001`):** Eliminates the non-existent `MassTransit.SqlTransport.Sqlite` dependency while keeping `data/subsystems/masstransit.db` as a real, encrypted durable store.
- **`+` Full SQLCipher Compatibility (`QAS-006`):** Because the outbox uses `ISubsystemDatabaseManager`, `masstransit.db` is encrypted with the exact same `SQLCipher` provider and participates in atomic `PRAGMA rekey`.

### Negative Consequences and Trade-Offs (`-`)
- **`-` Custom Outbox Code (`TP-06`):** Requires maintaining ~200 lines of Dapper SQLite outbox/inbox deduplication code in `CyberPipe.State` / `CyberPipe.Core`.
- **`-` Long-Term v8 Sunset:** Before `MassTransit` v8 security support ends in late 2026, evaluate whether to migrate `IConsumer<T>` handlers to `System.Threading.Channels` (Option 3) or `WolverineFx` (MIT).

---

## 6. Validation and Compliance Checks

- **`TEST-001` (Durable Outbox & Package Gate):**
  1. Verify `dotnet restore` succeeds with zero references to `MassTransit.SqlTransport.Sqlite` and `MassTransit` version `< 9.0.0`.
  2. Enqueue 50 messages into `masstransit.db`, simulate an abrupt daemon crash after 25 messages, restart the host, and assert all 50 messages are processed **exactly once** by idempotent consumers (`0` lost, `0` duplicated).
