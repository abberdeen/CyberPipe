# ADR-0003: Decentralized Local SQLite WAL Topology and Mandatory `SQLCipher` Page Encryption

- **Status:** `Proposed (Revises Plan ADR-004 & ADR-010 Connection Config)`
- **Date:** 2026-10-18
- **Deciders:** CyberPipe Architecture Team
- **Source Plan Evidence:** [`03-tech-stack-libraries.md`](../../plans/03-tech-stack-libraries.md), [`14-fault-isolation-and-resilience.md`](../../plans/14-fault-isolation-and-resilience.md), [`10-sandbox-telemetry-and-heuristics.md`](../../plans/10-sandbox-telemetry-and-heuristics.md), [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md), [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-004`, `ADR-010`)
- **Related Requirements:** `REQ-007`, `REQ-010`, `REQ-019`
- **Related Quality Scenarios (ATAM):** [`QAS-006`](../03-atam/quality-attribute-scenarios.md#qas-006), [`QAS-011`](../03-atam/quality-attribute-scenarios.md#qas-011)
- **Related Risks & Threats:** [`RISK-002`](../03-atam/risk-register.md#risk-002), [`RISK-008`](../03-atam/risk-register.md#risk-008), [`RISK-010`](../03-atam/risk-register.md#risk-010), [`THREAT-006`](../04-threat-model/threat-register.md#threat-006), [`THREAT-015`](../04-threat-model/threat-register.md#threat-015)
- **Verification Gate:** [`TEST-002`](../06-validation/assumptions-and-open-questions.md#test-002)

---

## 1. Context and Problem Statement

CyberPipe stores highly sensitive personal data (financial ledgers, CRM contacts, health/medication logs, private knowledge base, and automation state) locally without an external database server (`REQ-007`). [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md) proposes a decentralized SQLite topology (`data/cyberpipe_master.db` + 8 `data/subsystems/*.db` files) encrypted via `SQLCipher`.

However, our architectural audit identified **three critical flaws (`RISK-002`, `RISK-008`)** in the baseline specification:
1. **`SQLitePCLRaw` Native Binary Clash (`AUD-04` / `RISK-002`):** Referencing the standard `Microsoft.Data.Sqlite` metapackage pulls `SQLitePCLRaw.bundle_e_sqlite3` (unencrypted), and `Hangfire.Storage.SQLite` pulls `sqlite-net-pcl` + `bundle_green` (unencrypted). If `e_sqlite3` wins static initialization over `SQLitePCLRaw.bundle_e_sqlcipher`, `Password=<hex>` in `SqliteConnectionStringBuilder` is silently ignored—writing **plaintext SQLite files** to disk (`THREAT-006`).
2. **`Cache=Shared` Concurrency & Security Hazard (`RISK-002`):** Line 175 of `09-data-protection-and-state-hub.md` sets `Cache = SqliteCacheMode.Shared`. SQLite officially deprecates shared-cache mode; in `WAL` mode it causes `SQLITE_LOCKED` table errors that **ignore `busy_timeout = 5000`** and allows subsequent connections in the same process to attach without re-verifying the SQLCipher key.
3. **Orphan Tables (`AUD-03` / `RISK-008`):** `InFlightOperations`, `SemanticIntentCache`, and `SemanticResponseCache` are defined in SQL DDL without being assigned to any of the 9 `.db` files.

---

## 2. Decision Drivers

- **`REQ-007` / `QAS-006` / `THREAT-006`:** Guarantee `100%` verifiable `AES-256` page-level encryption across all `.db`, `.db-wal`, and `.db-shm` artifacts on disk, with atomic key rotation (`PRAGMA rekey`).
- **Write Concurrency (`TP-02`):** Eliminate cross-subsystem `SQLITE_BUSY` write lock contention between background infrastructure jobs (`hangfire`, `masstransit`, `budget`) and interactive user skills (`crm`, `finance`, `knowledge`).
- **`RISK-008`:** Assign a single, unambiguous database owner to every SQL table and define consistency rules across the 9 databases.

---

## 3. Considered Options

1. **Option 1:** Single Monolithic `cyberpipe.db` File with Standard `Microsoft.Data.Sqlite`
2. **Option 2 (Baseline in `docs/plans/`):** 9 Decentralized `.db` Files with `Microsoft.Data.Sqlite` + `Cache=Shared` *(Rejected — vulnerable to `RISK-002` plaintext fallback and `SQLITE_LOCKED`)*
3. **Option 3 (Chosen):** 9 Decentralized `.db` Files using **`Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher`**, **`SqliteCacheMode.Private`**, explicit table ownership, and a mandatory startup cryptographic self-test.

### Option Comparison Matrix

| Criterion | Option 1 (Single Monolithic DB) | Option 2 (Plan Baseline `Cache=Shared`) | Option 3 (`Sqlite.Core` + `bundle_e_sqlcipher` + `Cache=Private`) |
| :--- | :--- | :--- | :--- |
| **Cross-Subsystem Write Contention** | High (`1` global WAL writer) | Low across files, **High within file (`SQLITE_LOCKED`)** | **Minimal (`9` independent WAL writers, `busy_timeout=5000` honored)** |
| **Guaranteed SQLCipher Encryption** | Vulnerable if metapackage used | **Vulnerable (`RISK-002` / `AUD-04`)** | **100% Enforced + Verified by Startup Self-Test** |
| **Cross-DB Atomic 2PC** | Supported (Single file) | Unsupported in WAL mode | Eventual consistency via `masstransit.db` Outbox (`ADR-0002`) |

---

## 4. Decision Outcome

**Chosen Option:** **Option 3 — Decentralized 9-Database SQLite WAL Topology with `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher` and `SqliteCacheMode.Private`.**

### Architectural Specification

#### 4.1 Package & Initialization Rules (`RISK-002` Fix)
1. **Strict NuGet Graph:** Reference **only** `Microsoft.Data.Sqlite.Core` (never the `Microsoft.Data.Sqlite` metapackage) and `SQLitePCLRaw.bundle_e_sqlcipher`. Enforce via `ArchUnitNET` (`TEST-013`) that `e_sqlite3.dll` is never present in the build output.
2. **Explicit Startup Init:** Call `SQLitePCL.Batteries_V2.Init();` as the first statement in `Program.Main`.
3. **Connection String & Pragmas (`ISubsystemDatabaseManager`):**
   ```csharp
   var builder = new SqliteConnectionStringBuilder
   {
       DataSource = dbFilePath,
       Mode = SqliteOpenMode.ReadWriteCreate,
       Cache = SqliteCacheMode.Private, // NEVER Shared (fixes RISK-002)
       Pooling = true,
       Password = hexKeyFromSecretsVault
   };
   ```
   On every physical connection open:
   ```sql
   PRAGMA journal_mode = WAL;
   PRAGMA synchronous = NORMAL;
   PRAGMA busy_timeout = 5000;
   PRAGMA foreign_keys = ON;
   PRAGMA temp_store = MEMORY;
   ```
4. **Mandatory Startup Cryptographic & `FTS5` Self-Test (`TEST-002`):**
   At daemon startup, `SubsystemDatabaseManager` verifies:
   - `SELECT sqlite_compileoption_used('ENABLE_FTS5');` returns `1`.
   - `PRAGMA cipher_version;` returns a non-empty SQLCipher version string.
   - Reading the first 16 raw bytes of each `.db` file via `FileStream` **does not equal** `"SQLite format 3\0"`. If any file header is plaintext, `CyberPipe.exe` aborts startup immediately with `CryptographicIntegrityException`.

#### 4.2 Canonical 9-Database Ownership Map (Resolving `RISK-008` / `AUD-03`)

| Database File | Assigned Tables & Domain Ownership |
| :--- | :--- |
| **`data/cyberpipe_master.db`** | `SystemSettings`, `SubsystemRegistry`, `SchemaVersions`, and **`InFlightOperations`** *(assigned here to resolve `RISK-008`)*. |
| **`data/subsystems/knowledge.db`** | `KnowledgeDocuments`, `KnowledgeChunks_Fts5` (`FTS5`), `KnowledgeVectors`, **`SemanticIntentCache` (`L1`)**, and **`SemanticResponseCache` (`L2`)** *(assigned here to resolve `RISK-008`)*. |
| **`data/subsystems/budget.db`** | `TokenBudgetLedger`, `DailySpendSummaries`, `ModelPricingCatalog`. |
| **`data/subsystems/crm.db`** | `Contacts`, `Interactions`, `ContactReminders`, `RelationshipGraph`. |
| **`data/subsystems/finance.db`** | `Transactions`, `Subscriptions`, `Budgets`, `Accounts`. |
| **`data/subsystems/hangfire.db`** | `ScheduledJobs`, `CronTriggers`, `JobExecutionHistory` (via Dapper/SQLCipher storage per `ADR-0007`). |
| **`data/subsystems/health.db`** | `HabitLogs`, `SleepRecords`, `MedicationSchedules`, `WorkoutMetrics`. |
| **`data/subsystems/masstransit.db`** | `OutboxMessages`, `InboxConsumedMessages`, `DeadLetterMessages` (per `ADR-0002`). |
| **`data/subsystems/netrunner.db`** | `InstalledPackages`, `WorkflowDefinitions`, `WorkflowExecutions`, `WorkflowNodeCheckpoints`, `HitlPendingApprovals`. |

---

## 5. Consequences and Trade-Offs

### Positive Consequences (`+`)
- **`+` Eliminates Plaintext Database Risk (`QAS-006`):** Startup binary header verification makes silent `e_sqlite3` fallback impossible.
- **`+` High Concurrency Without `SQLITE_LOCKED` (`SP-02`):** `Cache=Private` + `WAL` + 9 separate files isolates subsystem writers and honors `busy_timeout = 5000`.

### Negative Consequences and Trade-Offs (`-`)
- **`-` No Cross-DB 2PC (`TP-02`):** Cross-database updates cannot use atomic `ATTACH DATABASE` transactions in WAL mode; they must use the `masstransit.db` Outbox (`ADR-0002`).
- **`-` Coordinated `PRAGMA rekey`:** Key rotation must iterate across all 9 `.db` files under an exclusive maintenance lock with pre-rotation file snapshots.

---

## 6. Validation and Compliance Checks

- **`TEST-002` (SQLCipher, `FTS5` & `PRAGMA rekey` Verification):**
  1. Initialize all 9 databases, write test rows, checkpoint WAL, and assert raw file bytes `[0..15] != "SQLite format 3\0"`.
  2. Attempt to open each database with an empty or wrong password and assert `SqliteException` (`SQLITE_NOTADB`, Error Code `26`).
  3. Execute `PRAGMA rekey` across all 9 databases and verify `PRAGMA integrity_check` returns `"ok"`.
