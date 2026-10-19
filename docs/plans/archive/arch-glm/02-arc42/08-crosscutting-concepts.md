# arc42 §8 — Cross-cutting Concepts

Part of the [arc42 documentation](01-introduction-and-goals.md).

---

## 8.1 Configuration and secrets management

| Concern | Approach | Source |
| :--- | :--- | :--- |
| App config | `appsettings.json` with env-var expansion + hot reload via `IOptionsMonitor<T>`. | [`09`](../../plans/09-data-protection-and-state-hub.md) §6 |
| Global settings | `AppGlobalSettings` table in `cyberpipe_master.db` (e.g. `Messaging:ActiveTransport`, `Pipeline:CrashRecoveryPolicy`). | [`09`](../../plans/09-data-protection-and-state-hub.md) §3.1 |
| Secrets at rest | `data/secrets.vault` (AES-256-GCM); master key in OS vault (`DPAPI` CurrentUser / `LibSecret` / `Keychain`). Optional Argon2id master password. | [`15`](../../plans/15-cli-and-execution-modes.md) §3.3 |
| Per-DB keys | Unique 256-bit key per subsystem DB; stored under `db:secret:{SubsystemKey}` in vault. | [`09`](../../plans/09-data-protection-and-state-hub.md) §3.3 |
| Plaintext key buffers | `CryptographicOperations.ZeroMemory` in `finally`. | [`09`](../../plans/09-data-protection-and-state-hub.md) §2 |
| Key rotation | `ISubsystemDatabaseManager` + SQLCipher `rekey` + `SubsystemDatabaseRegistry` update. | [`09`](../../plans/09-data-protection-and-state-hub.md) §7 |
| Master-key loss | Fail-closed; restore from `backups/cyberpipe.v{N}.bak`; re-init secrets via `CyberPipe setup`. | [`09`](../../plans/09-data-protection-and-state-hub.md) §7; `AUD-14` |

**Open concern**: SQLCipher provider (`SQLitePCLRaw.bundle_e_sqlcipher`) is required for
actual page encryption; stock `Microsoft.Data.Sqlite` does not encrypt (`AUD-04`). Until
PoC, treat DB files as **not confidential at rest**.

## 8.2 Authentication, authorization, local privilege boundaries

| Boundary | Mechanism |
| :--- | :--- |
| Telegram users | Whitelist numeric `TelegramUserId`; `Owner` vs `Guest` roles; non-whitelisted dropped `< 0.1 ms`. |
| IPC control (UI/CLI ↔ daemon) | `CurrentUserOnly` pipe flag + `PID` verification; command authorization by role. |
| IPC worker (daemon ↔ Brainstem) | `PID == spawnedProcess.Id` + one-time `SessionNonce` (TTL, burn-after-use). `AUD-09` — protocol not fully specified. |
| Brainstem OS isolation | AppContainer / RestrictedToken + Deny DACL / bwrap / Seatbelt; `ActiveProcessLimit=1`; network denied. `AUD-13` — equivalence unverified. |
| Hangfire dashboard | Loopback `127.0.0.1` only; one-time crypto token (OTT, 256-bit, TTL 30 s, SHA-256 stored); `HttpOnly` + `SameSite=Strict` cookie; `IsReadOnlyFunc=true`. |
| Tier-3 tool calls | Sandbox mandatory; native process requires `RequiresApproval` HITL. |
| Guest role | Cannot run Tier 3, change limits, access secrets. |
| OS-file boundary | `data/` ACL denies Brainstem SID; `chmod 0700` on Linux; no `bwrap` mount on Linux. |

## 8.3 Encryption at rest and in transit

| Where | What | Status |
| :--- | :--- | :--- |
| `secrets.vault` | AES-256-GCM, master key in OS vault | Designed; DPAPI is OS-verified, AES-GCM is standard. |
| SQLite DB files | SQLCipher page encryption | **Hypothesis** until PoC (`AUD-04`). |
| Telegram transport | TLS (Telegram API) | External; out of scope. |
| Cloud LLM transport | HTTPS | Standard. |
| Local IPC | Named Pipe / UDS, current-user-only | OS-enforced; no extra TLS needed on loopback. |
| In-process memory | `ZeroMemory` on key buffers; no blanket in-process encryption (deliberate — see [`09`](../../plans/09-data-protection-and-state-hub.md) §0). | Designed. |

## 8.4 Logging, diagnostics, sensitive-data redaction

- **Egress sanitizer** (`CentralDataSanitizer`) sits at every egress boundary: cloud LLM,
  `Serilog` sinks, Telegram output. Regex + Shannon entropy; `[REDACTED:ID]` substitution.
- **No blind rehydration**: `[REDACTED:ID]` is never string-substituted back into LLM-
  generated commands. Secret needs flow through `Git Credential Helper` / isolated env
  vars bound to domain whitelist.
- **Tool output truncation**: `ToolOutputTruncator` caps at `1 500` chars (head `500` + tail
  `800` + error lines); full log kept on disk for point queries.
- **Telemetry digest**: sandbox logs compressed `1 GB → 30 KB` JSON out-of-band.
- **Cost logging**: per-request token and USD cost via Serilog enricher.
- **Fail-closed logging**: security refusal events always logged.

## 8.5 Error handling and retries

| Layer | Strategy |
| :--- | :--- |
| Background workers | `IsolatedWorkerBase` catches managed exceptions; per-iteration 30 s timeout; 3 errors/min → `Quarantined`. |
| Host | `BackgroundServiceExceptionBehavior.Ignore`; `StartupTimeout=10s`; `ShutdownTimeout=5s`. |
| DI | `SafeModuleRegistration.TryInitializeModuleAsync<TModule>()` → `ModuleState.DisabledFaulted` on failure; others continue. |
| Netrunner nodes | `retryPolicy` (maxAttempts, backoff, `untilExpression`); `onError` branch; `DeferRetry` exit code 75. |
| Brainstem | Hard timeout kill; orphaned rows increment `AttemptCount` only on true crash. |
| IPC | `MaxReceive/SendMessageSize=4MB`; `KeepAliveTimeout=10s`; bad frame → drop before alloc. |
| MassTransit consumers | Must be idempotent (`MessageDeduplicationId`); `AUD-02` — per-consumer contracts not specified. |

## 8.6 Timeouts, cancellation, resource limits

| Resource | Limit | Source |
| :--- | :--- | :--- |
| Tier-0 routing | `< 1 ms` (target) | [`04`](../../plans/04-agent-mesh-proposals.md) |
| Tier-1 ONNX | `5–15 ms` (target) | [`04`](../../plans/04-agent-mesh-proposals.md) |
| `/stop` kill | `< 50 ms` (target) | [`04`](../../plans/04-agent-mesh-proposals.md) |
| IPC round-trip | `p95 < 25 ms` (target) | [`15`](../../plans/15-cli-and-execution-modes.md) §2 |
| Brainstem cold start (no Whisper weights) | `p95 < 80 ms` (target) | [`15`](../../plans/15-cli-and-execution-modes.md) §2 |
| gRPC frame | `4 MB` | [`15`](../../plans/15-cli-and-execution-modes.md) §4 |
| `QuotaBoundedStream` | `16 MB` | [`14`](../../plans/14-fault-isolation-and-resilience.md) §3 |
| Brainstem Pool 2 memory | `512 MB` (`JOB_OBJECT_LIMIT_JOB_MEMORY`) | [`15`](../../plans/15-cli-and-execution-modes.md) §6 |
| Brainstem Pool 1 memory | `WhisperModelSizeGb * 1.35 + 1.0 GB` | [`15`](../../plans/15-cli-and-execution-modes.md) §6 |
| Worker iteration | `30 s` | [`14`](../../plans/14-fault-isolation-and-resilience.md) §2 |
| Netrunner per-node timeout | `15 000 ms` default (manifest) | [`16`](../../plans/16-netrunner-etl-and-workflow-engine.md) §2.2 |
| `MaxOutputTokens` per step | `2 048` | [`04`](../../plans/04-agent-mesh-proposals.md) §2 |
| `HardTaskTokenLimit` | `12 000` | [`04`](../../plans/04-agent-mesh-proposals.md) §2 |
| `MaxTokensPerMinute` | `15 000` (velocity circuit breaker) | [`04`](../../plans/04-agent-mesh-proposals.md) §2 |
| `DailyBudgetUsdCap` | `$0.50` (default) | [`04`](../../plans/04-agent-mesh-proposals.md) §2 |

**All SLO numbers are local-budget targets on reference hardware (≥4 CPU cores, SSD NVMe,
warmed process); Telegram network latency excluded (`AUD-06`). Reporting is `p95/p99`, not
single-shot minimums.**

## 8.7 Concurrency and synchronization

- `Channel.CreateBounded<T>` only (`No_Unbounded_Channels_Allowed` ArchUnitNET rule).
- Bulkhead lanes (0 control, 1 fast-path, 2 watchdogs `SemaphoreSlim(3)`, 3A voice
  `SemaphoreSlim(1)`, 3B docs `SemaphoreSlim(2)`).
- `ActiveExecutionRegistry` `ConcurrentDictionary` for kill-switch.
- `GlobalStateHub` `BehaviorSubject` with bounded gRPC stream.
- `ArrayPool<byte>.Shared` + `RecyclableMemoryStream` for buffer hygiene.
- `InFlightOperations`: in-memory for Tier 0/1 (avoid 2× fsync on hot path), SQLite WAL for
  heavy/external tasks.

## 8.8 Transactions and consistency

- SQLite WAL: `journal_mode=WAL`, `synchronous=NORMAL/FULL`, `foreign_keys=ON`,
  `BusyTimeout=5000`.
- Step-level checkpointing in Netrunner: per-node `OutputPayloadJson` + SHA-256 committed
  atomically before next node.
- MassTransit outbox/inbox only with SQLite/RabbitMQ transports (not InMemory) — but
  outbox/inbox not explicitly specified in plans. **Open question** in
  [06-validation/assumptions-and-open-questions.md](../../06-validation/assumptions-and-open-questions.md).
- No cross-DB transactions by design (each subsystem isolated).

## 8.9 Idempotency and duplicate processing

- Telegram: dedup by `UpdateId`.
- MassTransit: dedup by `MessageDeduplicationId`; consumers must be idempotent (`AUD-02` —
  per-consumer idempotency not yet specified).
- Netrunner: `Idempotent` vs `NonIdempotentExternalAction` step types; the latter becomes
  `InterruptedNeedsConfirmation` on resume to avoid double side-effects.
- Semantic caches: key `(QueryHash, UserRole, DataVersion, ExpiresAtUtc)` — `AUD-05` fix.

## 8.10 Persistence and schema migrations

- `SqliteMigrationRunner` based on `PRAGMA user_version`; sequential runner; auto-backup
  before migrations via `SqliteBackupService`.
- Per-subsystem migrations via `MigrateSubsystemDatabaseAsync(subsystemKey)`.
- Master DB holds `SubsystemDatabaseRegistry` (path, `SecretVaultKeyRef`, `IsEncrypted`,
  `LastMigratedAtUtc`).
- Pre-migration backup mandatory; failed migration → auto-rollback to previous version + DB.

## 8.11 Backup, recovery, disaster

- Local-only; online Backup API; `backups/cyberpipe.v{N}.bak`; checksum-verified restore;
  restore runbook test required pre-release (`AUD-12`).
- WAL protects against `database disk image is malformed` on power loss.
- Crash recovery: `Pipeline:CrashRecoveryPolicy` (`ResumeFromCheckpoint` /
  `StopAndMarkCancelledByCrash`).
- No DR site; single-host product. If host dies, restore from backup to a new host.

## 8.12 Versioning and compatibility

- `BrainstemHandshake.ProtocolVersion = 1`; versioned `BrainstemTaskKind`.
- Netrunner manifest versioned (`<slug>@<version>`); SHA-256 verified pre-run.
- DB schema versions via `PRAGMA user_version`.
- `worker.manifest.json` pinned at build; mismatch blocks spawn (ExitCode 3).
- CLI `--json` contract and ExitCode `0..4` are part of the public surface.

## 8.13 Dependency management and supply chain

- NuGet packages (planned): `MassTransit` (+ RabbitMQ, SqlTransport.Sqlite), `Microsoft.Data.Sqlite`,
  `Dapper`, `Telegram.Bot`, `Whisper.net`, `Microsoft.ML.OnnxRuntime`, `Microsoft.Extensions.AI`,
  `OllamaSharp`, `OpenAI`/cloud SDKs, `Microsoft.ML.Tokenizers`, `NAudio`, `Hangfire.*`,
  `LibGit2Sharp`, `CliWrap` (only in `Execution.Providers`), `UglyToad.PdfPig`,
  `DocumentFormat.OpenXml`, `Avalonia`, `CommunityToolkit.Mvvm`, `System.CommandLine`,
  `Spectre.Console`, `Grpc.Net.Client`/`Grpc.AspNetCore`, `Serilog`, `OpenTelemetry`,
  `ArchUnitNET`, `BenchmarkDotNet`, `SQLitePCLRaw.bundle_e_sqlcipher` (PoC-pending).
- Native runtimes: `onnxruntime`, `whisper`, `e_sqlite3` (and SQLCipher variant if PoC passes).
- **Supply-chain risk**: native binaries (`whisper.dll`, etc.) are DLL-hijack vectors;
  mitigated by `worker.manifest.json` SHA-256 verification + `FileShare.Read` hold.
- No `Directory.Packages.props` or `NuGet.config` exists yet; central package management
  decision is an open question.

## 8.14 Licensing and redistribution

| Component | License (per plans) | Note |
| :--- | :--- | :--- |
| Most local models (Qwen3, Mistral, Gemma, SmolLM, Phi-4) | Apache 2.0 / MIT / Open / Llama / StabilityAI | Per [`13`](../../plans/13-model-advisor-and-tier-configuration.md) §3 table; verify each model's actual current license before distribution. |
| Cloud LLMs | Pay-per-token | N/A. |
| Ollama / LM Studio | User-installed separately | Not redistributed. |
| RabbitMQ (optional) | Mozilla Public License | Enterprise option. |
| CyberPipe itself | License **not specified** in plans | **Open question** — see [06-validation](../../06-validation/assumptions-and-open-questions.md). |

## 8.15 Updates, rollback, backward compatibility

See [§7 deployment](07-deployment-view.md) §7.5. Side-by-side update + auto-rollback;
pre-migration backup; `CyberPipe doctor --verify-update`; `rekey` rotation; fail-closed on
master-key loss.

## 8.16 Observability, maintenance, supportability

- `GlobalStateHub` reactive stream → UI live status (bus, budget, workers, sandbox).
- `health.db` `InfrastructureHealthRecords` (per-check latency, error, status).
- `CyberPipe doctor` (PRAGMA integrity_check, DACL check, manifest verify, AVX2/VRAM,
  GPU test, audio test, isolation test).
- `CyberPipe status --json` / `health` / `triage` / `logs --follow` (post-sanitization).
- Module quarantine visibility (`CyberPipe skills list|unquarantine`).
- Sandbox instance listing + kill (`CyberPipe sandbox list|kill`).

## 8.17 Explicit exclusions (with reasoning)

- **In-process encryption of every message buffer**: deliberately excluded. Same-process
  memory encryption adds CPU cost without real protection (dump attacks dominate); OS file
  ACL + vault is the real boundary. See [`09`](../../plans/09-data-protection-and-state-hub.md) §0.
- **Distributed tracing across hosts**: N/A; single host.
- **Multi-tenant RBAC**: N/A; single user with two roles (`Owner`/`Guest`).
- **Cloud-hosted control plane**: N/A; daemon is local-only by design.
