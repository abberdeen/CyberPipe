# arc42 §8 — Cross-Cutting Concepts

Only topics material to this system are covered; exclusions are stated at the end.

- **Configuration & secrets.** Single `appsettings.json` (`IOptionsMonitor<T>`, hot
  reload, env expansion). Secrets live only in `data/secrets.vault` (AES-256-GCM,
  OS-bound master key, `ZeroMemory` after use); per-subsystem DB keys under
  `db:secret:{SubsystemKey}`; RabbitMQ creds likewise. No secret in logs, health DB,
  chat, or `--json stdout` (diagnostics → `stderr`).
- **AuthN/AuthZ & local privilege.** Telegram whitelist (`Owner`/`Guest`);
  IPC PID+nonce authentication **plus** per-command role authorization; Brainstem runs
  least-privilege (AppContainer / restricted token / bwrap / seatbelt).
- **Encryption.** In transit: HTTPS to cloud APIs, loopback-only local HTTP; at rest:
  vault + per-DB SQLCipher (unproven until PoC — [RISK-001](../03-atam/risk-register.md)).
  No blanket in-memory re-encryption inside one process (deliberate, documented).
- **Logging & redaction.** All sinks (Serilog, Telegram, dashboard) sit *behind*
  `CentralDataSanitizer`; local Tier 0/1 notes are never distorted at ingress; secret
  placeholders are never blindly rehydrated into LLM-generated commands (credential
  helper / scoped env instead).
- **Errors & retries.** `IsolatedWorkerBase` (3 strikes/min → quarantine); Netrunner
  `retryPolicy` + `untilExpression` + `DeferRetry` (no RAM held while waiting);
  `onError` fallback branches; poison-pill recorder distinguishes crash orphans from
  `/stop` cancels and validation errors.
- **Timeouts/cancellation/limits.** `/stop` priority channel (<50 ms local);
  Brainstem soft-cancel → hard-kill; per-pool semaphores; 4 MB gRPC frames; 16 MB
  stream quota; 512 MB single-use parser cap; Netrunner run-workspace cap (128 MB).
- **Concurrency.** Bulkhead lanes + bounded channels (`DropOldest` for telemetry,
  `Wait`-with-timeout for commands); `MaxHopLimit=3`; `MaxParallelBranches=8`;
  SQLite single-writer discipline per DB file.
- **Transactions & consistency.** Outbox/inbox required where bus meets DB (unresolved
  detail — [ASSUMPTION-003](../06-validation/assumptions-and-open-questions.md));
  Netrunner step-level atomic checkpoints; idempotent resume; `InterruptedNeedsConfirmation`
  for non-idempotent external steps.
- **Idempotency/duplicates.** `MessageDeduplicationId` + `UpdateId` dedup; bus
  transports may redeliver (SQLite/RabbitMQ) or lose (InMemory) — consumers must be
  idempotent ([ADR-0001](../05-adr/0001-message-bus.md)).
- **Persistence & migrations.** `SqliteMigrationRunner` on `PRAGMA user_version`,
  serialized per DB, pre-migration backup; crash-safe WAL claims must be PoC-tested.
- **Backup/recovery/DR.** `backup sqlite create|verify|restore` (Backup API);
  master-key loss = data unrecoverable → restore from verified backup + re-onboard
  (fail-closed). Single-machine DR: backups must leave the machine (open procedure —
  [ASSUMPTION-007](../06-validation/assumptions-and-open-questions.md)).
- **Versioning/compatibility.** Versioned Brainstem protocol (`CurrentBrainstemProtocolVersion=1`);
  Netrunner JSON Schema Draft 2020-12 contracts on node edges; strict `--json` CLI
  envelope; exit codes 0–4.
- **Supply chain & licensing.** Dependency risk concentrated in: SQLCipher provider,
  `MassTransit.SqlTransport.Sqlite`, `Hangfire.Storage.SQLite`, `Whisper.net`,
  `LibGit2Sharp` (native binaries), ONNX Runtime, Avalonia — see
  [compatibility matrix](../06-validation/technology-compatibility-matrix.md). Model
  licenses vary (Apache/MIT vs Llama/StabilityAI terms) — curated catalog must record
  redistribution limits before bundling any weights.
- **Updates/rollback.** Side-by-side update + verify gate + automatic DB rollback.
- **Observability/maintenance.** `GlobalStateHub` → UI/CLI; health records in
  `health.db`; token/cost accounting per request; `triage`/`health`/`doctor` commands;
  telemetry digest (1 GB → ~30 KB) + 1 500-char tool truncation for LLM paths.

## Deliberately excluded

Distributed tracing across machines (single host), multi-region/failover design,
formal compliance regimes (no auditor stakeholder), in-process memory encryption
(rejected as cost without boundary benefit).
