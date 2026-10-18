# ATAM Architectural Risk Register (`RISK-001` .. `RISK-016`)

> **Document Role:** The authoritative, evidence-backed register of architectural risks, hidden specification contradictions, technology incompatibilities, and unverified assumptions discovered in `docs/plans/`.
> **Traceability:** Every risk links to its source evidence in `docs/plans/`, affected building blocks, quality attribute scenarios (`QAS`), governing `ADR`, and verification test (`TEST`).

---

## 1. Explicit Risk Scoring Criteria

To avoid arbitrary labels, every risk in this register is evaluated using the following deterministic **Severity** and **Likelihood** rubrics:

### Severity Criteria (Impact on System if Realized)
- **Critical:** Prevents the solution from compiling/starting, silently violates a core security invariant (e.g., plaintext databases instead of encrypted), introduces an immediate licensing/legal violation, or causes total loss of user state. **Blocks implementation until architectural design is revised.**
- **High:** Causes deterministic runtime crashes (`OutOfMemoryException`, `SQLITE_LOCKED`), bypasses a sandbox boundary under realistic conditions, or breaks a primary functional capability (`Netrunner`, `Brainstem`, `Hangfire`).
- **Medium:** Degrades performance/latency below target SLOs, returns stale cached data in edge cases, or creates cross-module implementation ambiguity due to documentation drift.
- **Low:** Minor operational friction or edge-case limitation with an existing fallback mechanism.

### Likelihood Criteria (Probability of Occurrence Under Current `docs/plans/` Specification)
- **Certain (`> 95%`):** Guaranteed to occur immediately if implemented as written in `docs/plans/` (e.g., referencing a non-existent NuGet package or compiling Roslyn inside a `64 MB` memory limit).
- **High (`60% – 95%`):** Highly probable under standard deployment or normal concurrent usage.
- **Medium (`20% – 60%`):** Occurs under specific multi-subsystem interactions, edge configurations, or moderate load.
- **Low (`< 20%`):** Requires rare environmental faults (e.g., corrupted model file, OS user profile reset, or hardware driver bug).

---

## 2. Detailed Risk Entries (`RISK-001` .. `RISK-016`)

### `RISK-001` — Non-Existent `MassTransit.SqlTransport.Sqlite` Package, v9 Commercial License & Runtime Hot-Swap Trap {#risk-001}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **Critical** / **Certain** / **P0 (Architecture Blocker)** |
| **Affected Components** | `CyberPipe.Core` (`SubsystemTransportManager`), `CyberPipe.State` (`data/subsystems/masstransit.db`), `CyberPipe.Host` |
| **Related Quality Attributes** | Reliability (`QAS-010`), Modifiability (`QAS-013`), Cost/Legal Governance |
| **Evidence / Source of Uncertainty** | [`03-tech-stack-libraries.md`](../../plans/03-tech-stack-libraries.md) (L46), [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md) (L40), and [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-001` L24) specify `MassTransit.SqlTransport.Sqlite` backed by `data/subsystems/masstransit.db`, and claim `ISubsystemTransportManager` can hot-swap `InMemory` $\leftrightarrow$ `SqlTransport.Sqlite` $\leftrightarrow$ `RabbitMQ` at runtime via `StopAsync()`/`StartAsync()`. **Verification against NuGet and MassTransit documentation proves:** (1) `MassTransit.SqlTransport.Sqlite` **does not exist** (MassTransit SQL Transport only supports PostgreSQL and SQL Server); (2) MassTransit v9 transitioned to a commercial paid license (~$400/mo), leaving v8 (`8.x`, Apache 2.0) with security support ending late 2026; (3) MassTransit registers `IBus`/`IPublishEndpoint` as root DI singletons bound to a single transport factory at host build time. |
| **Mitigation Strategy** | 1. Revise `ADR-001` via **[`ADR-0002`](../05-adr/0002-message-bus-and-transport-strategy.md)**: pin `MassTransit` to **`8.x` (Apache 2.0)** and use `MassTransit` `InMemory` paired with a custom ~200-line Dapper/SQLCipher **Transactional Outbox** table inside `data/subsystems/masstransit.db` for durable local persistence.<br>2. Replace in-place `IBusControl` transport hot-swapping with either an `IAdaptiveMessageBus` wrapper or a graceful daemon host restart (`< 2 s` after draining `InFlightOperations`). |
| **Validation / Test Method** | **[`TEST-001`](../06-validation/assumptions-and-open-questions.md#test-001):** NuGet package restoration gate + durable outbox crash-recovery integration test. |

---

### `RISK-002` — `SQLitePCLRaw` Native Binary Clash (`e_sqlite3` vs `e_sqlcipher`) & `Cache=Shared` Hazard (`AUD-04`) {#risk-002}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **Critical** / **High** / **P0 (Security & Reliability Blocker)** |
| **Affected Components** | `CyberPipe.State` (`SubsystemDatabaseManager`), `CyberPipe.Scheduling` (`Hangfire`), all 9 `.db` files |
| **Related Quality Attributes** | Security / Confidentiality (`QAS-006`), Reliability / Concurrency |
| **Evidence / Source of Uncertainty** | [`03-tech-stack-libraries.md`](../../plans/03-tech-stack-libraries.md) and [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md) reference `Microsoft.Data.Sqlite`, `SQLitePCLRaw.bundle_e_sqlcipher`, and `Hangfire.Storage.SQLite`. Standard `Microsoft.Data.Sqlite` pulls `SQLitePCLRaw.bundle_e_sqlite3` (unencrypted), and `Hangfire.Storage.SQLite` pulls `sqlite-net-pcl` + `bundle_green` (unencrypted). When `e_sqlite3` initializes first, `Password=<hex>` in `SqliteConnectionStringBuilder` is silently ignored or fails, leaving databases **unencrypted on disk** (`THREAT-006`). Furthermore, [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md) L175 specifies `Cache = SqliteCacheMode.Shared`, which causes `SQLITE_LOCKED` table contention that **ignores `busy_timeout=5000`** in WAL mode and weakens per-connection SQLCipher password validation. |
| **Mitigation Strategy** | Documented in **[`ADR-0003`](../05-adr/0003-decentralized-sqlite-and-sqlcipher-encryption.md)**:<br>1. Ban the `Microsoft.Data.Sqlite` metapackage via `ArchUnitNET`; reference **only** `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher`.<br>2. Replace `SqliteCacheMode.Shared` with **`SqliteCacheMode.Private`** and `Pooling = true`.<br>3. Enforce a mandatory startup cryptographic self-test verifying `PRAGMA cipher_version;` is non-null, `fts5` compile option is present, and the first 16 bytes of every `.db` file on disk do not match `"SQLite format 3\0"`. |
| **Validation / Test Method** | **[`TEST-002`](../06-validation/assumptions-and-open-questions.md#test-002):** Automated SQLCipher header verification, `FTS5` query check, and concurrent WAL writer stress test. |

---

### `RISK-003` — Netrunner `64 MB` Roslyn (`main.csx`) OOM & Missing `BrainstemTaskKind` Contract {#risk-003}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **High** / **Certain** / **P0 (Architecture Blocker)** |
| **Affected Components** | `CyberPipe.Netrunner` (`NetrunnerPackageValidator`, `WorkflowDagRunner`), `CyberPipe.Sandbox`, `CyberPipe.Brainstem` |
| **Related Quality Attributes** | Reliability (`QAS-011`), Resource Efficiency (`QAS-012`) |
| **Evidence / Source of Uncertainty** | [`16-netrunner-etl-and-workflow-engine.md`](../../plans/16-netrunner-etl-and-workflow-engine.md) (L72–75) and `.agents/skills/netrunner-script-package/SKILL.md` specify `"engine": "CSharpRoslyn"`, `"entrypoint": "main.csx"`, and `"maxMemoryMb": 64`, stating scripts run in `Brainstem` or `ISandboxProvider`. **Two fatal contradictions exist:** (1) In-memory Roslyn scripting (`Microsoft.CodeAnalysis.CSharp.Scripting`) requires **`150–300 MB` of managed/native heap** to load metadata references and emit IL, guaranteeing an immediate `OutOfMemoryException` inside a `64 MB` OS Job Object / `cgroup`; (2) `BrainstemTaskKind` in [`15-cli-and-execution-modes.md`](../../plans/15-cli-and-execution-modes.md) (L139) only defines `Ping = 0`, `WhisperTranscribe = 1`, `DocumentExtractText = 2` and has no Netrunner task contract. |
| **Mitigation Strategy** | Documented in **[`ADR-0007`](../05-adr/0007-proactive-scheduling-and-netrunner-dag-engine.md)**:<br>1. **Host-Side Pre-Compilation:** During `NetrunnerPackageValidator` (on the host, outside the `64 MB` sandbox), compile `main.csx`/`main.cs` once into a restricted `.dll` assembly (or `Wasmtime` `.wasm` module), verify its AST/IL against forbidden namespaces, and cache the compiled artifact with a SHA-256 signature.<br>2. Execute the pre-compiled artifact inside `ISandboxProvider` (`TB-05`, `< 25 MB` footprint) rather than `Brainstem` (`TB-04`), keeping `Brainstem` dedicated to `Whisper` and `PdfPig`. |
| **Validation / Test Method** | **[`TEST-003`](../06-validation/assumptions-and-open-questions.md#test-003):** Execute a pre-compiled Netrunner ETL package inside a strict `64 MB` Windows Job Object and verify peak working set `< 45 MB`. |

---

### `RISK-004` — Zero Implementation Code (`CyberPipe.slnx` Empty) vs. Massive 16-Project MVP Scope (`AUD-01`) {#risk-004}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **Critical** / **Certain** / **P0 (Delivery Blocker)** |
| **Affected Components** | Entire Repository (`CyberPipe.slnx`, all 16 proposed `src/` projects and 5 `tests/` projects) |
| **Related Quality Attributes** | Modifiability (`QAS-013`), Delivery Feasibility |
| **Evidence / Source of Uncertainty** | Repository inspection confirms `CyberPipe.slnx` is a 14-byte empty stub (`<Solution />`) with **`0` `.cs` files and `0` `.csproj` files**, while `docs/plans/` specifies 16 source projects, 5 test projects, 9 encrypted SQLite databases, custom Windows/Linux/macOS sandboxes, dual Named Pipe IPC contours, ONNX + Whisper inference, and 16+ domain skills. Attempting "big-bang" parallel implementation across all 16 projects simultaneously carries an extreme risk of integration failure. |
| **Mitigation Strategy** | Enforce a strict **4-Phase Incremental Vertical-Slice Roadmap** (see [`../01-overview.md`](../01-overview.md)):<br>- **Phase 0 (Walking Skeleton):** `Contracts`, `Security` (`SecretsVault`), `State` (`SQLCipher` self-test), `Host`, `Cli` (Contour 1 IPC), and `Architecture.Tests`.<br>- **Phase 1 (Core Pipeline & Isolation):** `Core` (`Tier 0/1` Router, `/stop`), `Cognition` (`TokenBudgetGovernor`), `Sandbox` + `Brainstem` (Contour 2 IPC).<br>- **Phase 2 (Channels, Scheduling & Netrunner):** `Channels`, `Scheduling`, `Netrunner`.<br>- **Phase 3 (Domain Skills, Voice & Hardware):** `Skills`, `Voice`, `Hardware`. |
| **Validation / Test Method** | **[`TEST-013`](../06-validation/assumptions-and-open-questions.md#test-013):** Phase 0 walking skeleton CI gate (`dotnet build` + `ArchUnitNET` + `TEST-001`..`TEST-004`). |

---

### `RISK-005` — `Hangfire.Storage.SQLite` Driver Incompatibility (`sqlite-net-pcl`), `SQLITE_BUSY` & LGPL v3 {#risk-005}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **High** / **High** / **P1** |
| **Affected Components** | `CyberPipe.Scheduling` (`ProactiveScheduler`), `data/subsystems/hangfire.db` |
| **Related Quality Attributes** | Security (`QAS-006`), Reliability, Legal Compliance |
| **Evidence / Source of Uncertainty** | [`03-tech-stack-libraries.md`](../../plans/03-tech-stack-libraries.md) and [`12-assistant-capabilities-and-skills.md`](../../plans/12-assistant-capabilities-and-skills.md) select `Hangfire.Core` + `Hangfire.Storage.SQLite` (`hangfire.db`). Community `Hangfire.Storage.SQLite` depends on `sqlite-net-pcl` (not `Microsoft.Data.Sqlite`), meaning it **cannot use encrypted connections from `ISubsystemDatabaseManager` (`SQLCipher`)**, pulls conflicting native SQLite binaries (`RISK-002`), and suffers from frequent `SQLITE_BUSY` exceptions under polling workers. Additionally, `Hangfire.Core` is licensed under **LGPL v3**. |
| **Mitigation Strategy** | Documented in **[`ADR-0007`](../05-adr/0007-proactive-scheduling-and-netrunner-dag-engine.md)**:<br>1. **Do not reference `Hangfire.Storage.SQLite`.** Either implement a lightweight custom Dapper-based `JobStorage` backed by `ISubsystemDatabaseManager` (`hangfire.db` with `SQLCipher` and `Workers = 1–2`), or use `Cronos` + a durable SQLite schedule table in `hangfire.db` with a lightweight embedded status page.<br>2. If `Hangfire.Core` is retained, document LGPL v3 dynamic-linking compliance in `THIRD-PARTY-NOTICES.md`. |
| **Validation / Test Method** | **[`TEST-011`](../06-validation/assumptions-and-open-questions.md#test-011):** Verify `hangfire.db` is SQLCipher-encrypted and completes 100 scheduled jobs with `0` `SQLITE_BUSY` errors. |

---

### `RISK-006` — Windows `AppContainer` (`S-1-15-2-1`) Access Denied Outside `Program Files` & Unproven OS Parity (`AUD-13`) {#risk-006}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **High** / **High** / **P1** |
| **Affected Components** | `CyberPipe.Sandbox` (`BrainstemSupervisor`, `WindowsJobObjectSandbox`, `LinuxBubblewrapSandbox`, `MacOsSeatbeltSandbox`) |
| **Related Quality Attributes** | Security (`QAS-005`), Operability |
| **Evidence / Source of Uncertainty** | [`02-architecture-diagram.md`](../../plans/02-architecture-diagram.md), [`10-sandbox-telemetry-and-heuristics.md`](../../plans/10-sandbox-telemetry-and-heuristics.md), and [`15-cli-and-execution-modes.md`](../../plans/15-cli-and-execution-modes.md) propose Windows `AppContainer` (`CreateAppContainerProfile`) or `RestrictedToken` for `Brainstem` and `Tier 3` sandboxes, plus `bubblewrap` on Linux and `sandbox-exec` on macOS (`deprecated` by Apple). On Windows, an `AppContainer` token (`S-1-15-2-1`) has **zero access** to `%USERPROFILE%`, `%LOCALAPPDATA%`, or parent Named Pipes unless explicit ACLs are granted. Conversely, if a fallback `RestrictedToken` is used without an explicit `Deny` ACE on `./data/`, the restricted token may still inherit user read permissions on `./data/vault.bin`. |
| **Mitigation Strategy** | Documented in **[`ADR-0004`](../05-adr/0004-brainstem-process-isolation-and-sandboxing.md)**:<br>1. `BrainstemSupervisor` must programmatically grant `ReadAndExecute` ACLs for the worker's AppContainer/Restricted SID on `./worker/` and `./models/`, and grant `ReadWrite` on the Contour 2 Named Pipe `PipeSecurity`.<br>2. Explicitly apply an NTFS **`AccessControlType.Deny` ACE** for the sandbox SID and `S-1-15-2-1` on `./data/`.<br>3. Scope MVP support strictly to **Windows 11 (`x64`/`ARM64`)**; gate Linux (`bwrap`) and macOS behind explicit platform integration tests. |
| **Validation / Test Method** | **[`TEST-004`](../06-validation/assumptions-and-open-questions.md#test-004):** Spawn `CyberPipe.Brainstem.exe` from a user directory (`%LOCALAPPDATA%`), verify Contour 2 Protobuf ping succeeds, and verify reading `./data/vault.bin` throws `UnauthorizedAccessException`. |

---

### `RISK-007` — `DevToolProcessRunner` Arbitrary Code Execution via MSBuild Targets & Git Hooks (`AUD-08`) {#risk-007}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **High** / **High** / **P1** |
| **Affected Components** | `CyberPipe.Sandbox` (`DevToolProcessRunner`), `CyberPipe.Skills` (`DevToolsSkill`) |
| **Related Quality Attributes** | Security (`QAS-005`, `QAS-007`) |
| **Evidence / Source of Uncertainty** | [`12-assistant-capabilities-and-skills.md`](../../plans/12-assistant-capabilities-and-skills.md) treats `dotnet build`, `dotnet test`, and `git` as safe developer CLI commands wrapped by `DevToolProcessRunner`. In reality, running `dotnet build` or `dotnet test` on an untrusted or agent-modified repository executes arbitrary C# code via MSBuild `<UsingTask>`, NuGet `buildTransitive` targets, Roslyn Source Generators, and unit test bodies. Similarly, running `git` in a repository with a malicious `.git/hooks/pre-commit` or `fsmonitor` executes arbitrary shell scripts. |
| **Mitigation Strategy** | Documented in **[`ADR-0004`](../05-adr/0004-brainstem-process-isolation-and-sandboxing.md)**:<br>1. Classify `dotnet build` and `dotnet test` as **Tier 3 Code Execution** requiring explicit Owner `HITL` approval when the workspace contains external or LLM-generated edits.<br>2. Always pass `-c core.hooksPath=/dev/null -c core.fsmonitor=false` to all automated `git` invocations.<br>3. Execute `DevToolProcessRunner` inside a Job Object with an explicit NTFS `Deny` ACL on `./data/` (`vault.bin` and `*.db`) and scrubbed environment variables. |
| **Validation / Test Method** | **[`TEST-015`](../06-validation/assumptions-and-open-questions.md#test-015):** Verify `DevToolProcessRunner` blocks `.git/hooks` execution and cannot read `./data/vault.bin` from inside an MSBuild target or unit test. |

---

### `RISK-008` — Orphan SQLite Tables (`InFlightOperations`, Caches) & Lack of Cross-DB 2PC (`AUD-03`) {#risk-008}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **High** / **Certain** / **P1** |
| **Affected Components** | `CyberPipe.State`, `CyberPipe.Core`, `CyberPipe.Cognition` |
| **Related Quality Attributes** | Reliability (`QAS-011`), Modifiability |
| **Evidence / Source of Uncertainty** | [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md) defines `cyberpipe_master.db` (settings & subsystem registry only) and 8 `data/subsystems/*.db` files (`hangfire`, `masstransit`, `knowledge`, `crm`, `finance`, `budget`, `health`, `netrunner`). However, `InFlightOperations` ([`14-fault-isolation-and-resilience.md`](../../plans/14-fault-isolation-and-resilience.md) L168), `SemanticIntentCache`, and `SemanticResponseCache` ([`10-sandbox-telemetry-and-heuristics.md`](../../plans/10-sandbox-telemetry-and-heuristics.md) L48) are defined in SQL DDL without being assigned to any of the 9 `.db` files. Furthermore, SQLite in `WAL` mode does not support atomic two-phase commit (2PC) across multiple `.db` files on OS crash. |
| **Mitigation Strategy** | Documented in **[`ADR-0003`](../05-adr/0003-decentralized-sqlite-and-sqlcipher-encryption.md)**:<br>1. Assign `InFlightOperations` to **`data/cyberpipe_master.db`**.<br>2. Assign `SemanticIntentCache` and `SemanticResponseCache` to **`data/subsystems/knowledge.db`**.<br>3. Prohibit multi-DB `ATTACH DATABASE` write transactions; coordinate cross-subsystem mutations via the `masstransit.db` Outbox + idempotent consumer keys. |
| **Validation / Test Method** | **[`TEST-002`](../06-validation/assumptions-and-open-questions.md#test-002):** Schema migration test verifying all tables exist in their assigned `.db` files. |

---

### `RISK-009` — Named Pipe `FirstPipeInstance` Pool Conflict & Local User Role Spoofing (`AUD-09`) {#risk-009}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **High** / **Medium** / **P1** |
| **Affected Components** | `CyberPipe.Host` (Kestrel Contour 1 Named Pipe server), `CyberPipe.Security` |
| **Related Quality Attributes** | Security (`QAS-004`, `QAS-005`), Availability |
| **Evidence / Source of Uncertainty** | [`05-security-agent.md`](../../plans/05-security-agent.md) and [`15-cli-and-execution-modes.md`](../../plans/15-cli-and-execution-modes.md) specify `PipeOptions.FirstPipeInstance` (`FILE_FLAG_FIRST_PIPE_INSTANCE`) with `MaxConcurrentConnections = 8` on `\\.\pipe\CyberPipe.Ipc.{Username}`. In Win32, `FILE_FLAG_FIRST_PIPE_INSTANCE` must **only** be passed when creating the *first* instance of a named pipe; if a custom factory passes it to instances `2..8`, `CreateNamedPipe` fails with `ERROR_ACCESS_DENIED`. Additionally, `05-security-agent.md` describes `Owner` vs `Member`/`Guest` RBAC, but any process running under the Owner's Windows user session shares the Owner's Windows SID on the Named Pipe. |
| **Mitigation Strategy** | Documented in **[`ADR-0005`](../05-adr/0005-ipc-security-and-hangfire-dashboard-auth.md)**:<br>1. Use .NET's built-in `NamedPipeServerStreamAcl.Create` where `PipeOptions.FirstPipeInstance` is applied strictly to the first pool handle (or verify Kestrel's `CreateNamedPipeServerStream` factory behavior in `TEST-006`).<br>2. Explicitly document that `Member`/`Guest` RBAC applies to **external multi-user channels (Telegram User IDs)**, whereas local Named Pipe connections under the Owner's OS SID are authenticated as `Owner` (except sandboxed child processes, which are blocked by `AppContainer`/Restricted SID DACLs). |
| **Validation / Test Method** | **[`TEST-006`](../06-validation/assumptions-and-open-questions.md#test-006):** 8-client concurrent Named Pipe stress test + squatter rejection test. |

---

### `RISK-010` — Stale `SemanticResponseCache` Across Decentralized Subsystem Databases (`AUD-05`) {#risk-010}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **Medium** / **High** / **P2** |
| **Affected Components** | `CyberPipe.State` (`SemanticResponseCache`, `SubsystemDatabaseManager`) |
| **Related Quality Attributes** | Correctness / Consistency (`QAS-002`) |
| **Evidence / Source of Uncertainty** | [`10-sandbox-telemetry-and-heuristics.md`](../../plans/10-sandbox-telemetry-and-heuristics.md) invalidates `L2 SemanticResponseCache` entries using SQLite's `PRAGMA data_version`. Because CyberPipe uses 8 separate subsystem `.db` files, a write to `crm.db` or `finance.db` only increments `PRAGMA data_version` inside that specific `.db` file—not in `cyberpipe_master.db` or `knowledge.db`. |
| **Mitigation Strategy** | Store `TargetSubsystemId` on every `SemanticResponseCache` row and maintain an in-memory `Interlocked` composite mutation counter per subsystem in `SubsystemDatabaseManager` combined with `PRAGMA data_version` on the target subsystem's database (`ADR-0001`). |
| **Validation / Test Method** | **[`TEST-007`](../06-validation/assumptions-and-open-questions.md#test-007):** Write to `crm.db` and verify `SemanticResponseCache` immediately invalidates cached CRM responses. |

---

### `RISK-011` — In-Process `ONNX Runtime` Native Crash Blast Radius in `CyberPipe.exe` {#risk-011}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **High** / **Low** / **P2** |
| **Affected Components** | `CyberPipe.Cognition` (`TieredIntentRouter`), `CyberPipe.Host` |
| **Related Quality Attributes** | Availability / Fault Containment (`QAS-002`, `QAS-010`) |
| **Evidence / Source of Uncertainty** | [`02-architecture-diagram.md`](../../plans/02-architecture-diagram.md) isolates `Whisper.net` in `Brainstem` because native C++ crashes kill the host process, yet keeps `Microsoft.ML.OnnxRuntime` (`onnxruntime.dll`) in-process inside `CyberPipe.exe` for `< 15 ms` `Tier 1` routing. |
| **Mitigation Strategy** | Verify the SHA-256 hash of `all-MiniLM-L6-v2.onnx` at startup, cap input token length to `<= 512` tokens, use the stable CPU execution provider, and support a fallback configuration toggle (`Cognition:RunOnnxInBrainstem = true`) that delegates `Tier 1` embeddings to `Brainstem` Pool A if a native fault ever occurs (`ADR-0001`). |
| **Validation / Test Method** | **[`TEST-007`](../06-validation/assumptions-and-open-questions.md#test-007):** Fuzz `Tier 1` tokenizer/ONNX input with malformed Unicode and max-length payloads. |

---

### `RISK-012` — False Technical Guarantee on External Media Player Content Filtering (`1.1.1.3`) {#risk-012}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **Medium** / **High** / **P2** |
| **Affected Components** | `CyberPipe.Hardware` (`NextDnsGuard`, `MediaController`) |
| **Related Quality Attributes** | Safety (`QAS-014`), Honesty of Security Claims |
| **Evidence / Source of Uncertainty** | [`10-sandbox-telemetry-and-heuristics.md`](../../plans/10-sandbox-telemetry-and-heuristics.md) claims to enforce family-safe music/media playback by checking DNS `1.1.1.3` (Cloudflare Family) before controlling external players (Spotify, Yandex.Music). Checking `1.1.1.3` in `CyberPipe.exe` **does not** force external desktop apps or browsers using DoH/cached connections to use `1.1.1.3`, nor does DNS `1.1.1.3` filter individual explicit song tracks inside Spotify/Yandex.Music CDN streams. |
| **Mitigation Strategy** | Reclassify `NextDnsGuard` as a **best-effort host DNS health check**, and implement track-level safety inspection by checking GSMTC media metadata (`Title`/`Artist`/explicit tags) and blocklists in `MediaController`. |
| **Validation / Test Method** | **[`TEST-014`](../06-validation/assumptions-and-open-questions.md#test-014):** Unit test GSMTC metadata filter and `VolumeClamp`. |

---

### `RISK-013` — `C:\Program Files\CyberPipe\` Admin ACL vs. Non-Elevated Side-by-Side Update {#risk-013}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **Medium** / **High** / **P2** |
| **Affected Components** | `CyberPipe.Host`, Deployment Installer / Updater |
| **Related Quality Attributes** | Operability, Security (Least Privilege) |
| **Evidence / Source of Uncertainty** | [`15-cli-and-execution-modes.md`](../../plans/15-cli-and-execution-modes.md) §1 places binaries in `C:\Program Files\CyberPipe\` while §7 describes a non-elevated daemon performing atomic side-by-side directory updates (`./releases/v1.x/`). Standard users cannot write to `C:\Program Files\`. |
| **Mitigation Strategy** | Default per-user installations to `%LOCALAPPDATA%\Programs\CyberPipe\` (binaries) and `%LOCALAPPDATA%\CyberPipe\data\` (encrypted state hub), or require explicit UAC elevation via `cyberpipe update apply` when installed in `C:\Program Files\`. |
| **Validation / Test Method** | **[`TEST-013`](../06-validation/assumptions-and-open-questions.md#test-013):** Verify deployment layout and non-admin write permissions in integration tests. |

---

### `RISK-014` — Single Point of Catastrophic Data Loss on `DPAPI` Master Key Loss (`AUD-14`) {#risk-014}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **High** / **Low** / **P2** |
| **Affected Components** | `CyberPipe.Security` (`SecretsVault`), `CyberPipe.State` (`CyberPipeBackupService`) |
| **Related Quality Attributes** | Availability / Recoverability (`QAS-006`) |
| **Evidence / Source of Uncertainty** | [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md) seals `vault.bin` (which holds the SQLCipher key for all 9 databases) using Windows `DPAPI` (`CurrentUser`). If the OS disk dies or the Windows user profile is reset without an exported recovery key, all `.db` files and `.cpbak` backups encrypted with that master key become permanently unrecoverable. |
| **Mitigation Strategy** | Documented in **[`ADR-0006`](../05-adr/0006-secrets-vault-and-egress-sanitization.md)**: During initial `cyberpipe init`, require the Owner to export an offline **Emergency Recovery Kit** (master key wrapped with `Argon2id` + `AES-256-GCM` using an Owner passphrase) and support passphrase-encrypted `.cpbak` archives in `CyberPipeBackupService`. |
| **Validation / Test Method** | **[`TEST-002`](../06-validation/assumptions-and-open-questions.md#test-002):** Backup restore test on a clean user profile using the Argon2id recovery passphrase. |

---

### `RISK-015` — Unverified Sub-Millisecond & Millisecond Latency SLOs (`AUD-06`) {#risk-015}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **Medium** / **Medium** / **P2** |
| **Affected Components** | `CyberPipe.Cognition`, `CyberPipe.Core`, `CyberPipe.Security`, `CyberPipe.Cli` |
| **Related Quality Attributes** | Performance (`QAS-001`, `QAS-002`, `QAS-003`, `QAS-004`, `QAS-008`) |
| **Evidence / Source of Uncertainty** | `docs/plans/` asserts `< 1 ms` (`Tier 0`), `< 15 ms` (`Tier 1` ONNX), `< 1.5 ms` (`SIMD` 10k vectors), `< 0.5 ms` (`CentralDataSanitizer`), `< 15 ms` (Native AOT CLI), and `< 50 ms` (`/stop`). Because `0` lines of code exist (`RISK-004`), these numbers are currently unverified engineering estimates. |
| **Mitigation Strategy** | Treat all latency figures as **Target SLOs** gated by automated `BenchmarkDotNet` suites in Phase 0/Phase 1 (`TEST-006`, `TEST-007`, `TEST-008`). |
| **Validation / Test Method** | **[`TEST-007`](../06-validation/assumptions-and-open-questions.md#test-007)** & **[`TEST-008`](../06-validation/assumptions-and-open-questions.md#test-008):** `BenchmarkDotNet` CI regression suite. |

---

### `RISK-016` — Cross-Document Specification Drift (`MaxLlmTurnsPerTask`, CLI Verbs, Trust Enums, Models) {#risk-016}

| Attribute | Details |
| :--- | :--- |
| **Severity / Likelihood / Priority** | **Medium** / **Certain** / **P2** |
| **Affected Components** | `CyberPipe.Contracts`, `CyberPipe.Cognition`, `CyberPipe.Cli`, `CyberPipe.Scheduling` |
| **Related Quality Attributes** | Modifiability (`QAS-013`) |
| **Evidence / Source of Uncertainty** | Cross-examination of `docs/plans/` revealed: (1) `MaxLlmTurnsPerTask` is `3` globally in `02`/`08` vs `3` Cloud / `5` Local in `13`; (2) CLI command is `cyberpipe schedule` in `12` vs `CyberPipe cron` in `15`; (3) Trust enum is `OwnerExplicit` in `05` vs `TrustedOwner` in `12`; (4) `13-model-advisor-and-tier-configuration.md` references speculative `Meta Muse Spark / Muse Loom` cloud models alongside standard Claude/OpenAI/Gemini models. |
| **Mitigation Strategy** | Enforce the canonical identifiers defined in [`../02-arc42/12-glossary.md`](../02-arc42/12-glossary.md) (`OriginTrustLevel`, `MaxCloudLlmTurnsPerTask = 3`, `MaxLocalLlmTurnsPerTask = 5`, `cyberpipe schedule` with `cron` alias) and make all cloud model IDs configurable via `appsettings.json` (`IChatClient`). |
| **Validation / Test Method** | **[`TEST-013`](../06-validation/assumptions-and-open-questions.md#test-013):** Contract compilation and CLI command alias unit test. |

---

## 3. Consolidated Risk Themes (ATAM Synthesis)

Grouping the 16 individual risks reveals **four overarching ATAM Risk Themes** that must govern Phase 0 implementation:

1. **Theme 1 — Phantom & Conflicting NuGet Dependencies (`RISK-001`, `RISK-002`, `RISK-005`):**
   The persistence and messaging layer in `docs/plans/` relies on a non-existent package (`MassTransit.SqlTransport.Sqlite`) and mixing incompatible SQLite wrappers (`Microsoft.Data.Sqlite`, `Hangfire.Storage.SQLite`, `SQLitePCLRaw.bundle_e_sqlcipher`). **Resolution:** Standardize 100% of local database access on `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher` + `Dapper` (`ADR-0002`, `ADR-0003`, `ADR-0007`).
2. **Theme 2 — OS Sandbox vs. Runtime Resource Realities (`RISK-003`, `RISK-006`, `RISK-007`):**
   Running Roslyn in-memory inside `64 MB` (`RISK-003`) or running Windows `AppContainer` outside `Program Files` without explicit ACL provisioning (`RISK-006`) will fail immediately on first execution. **Resolution:** Pre-compile Netrunner packages on the host (`ADR-0007`) and automate sandbox ACL setup + startup self-tests (`ADR-0004`).
3. **Theme 3 — Cross-Database State Coordination Without 2PC (`RISK-008`, `RISK-010`, `RISK-014`):**
   Splitting SQLite into 9 files eliminates write lock contention (`TP-02`) but requires explicit table ownership (`RISK-008`), composite cache invalidation epochs (`RISK-010`), and an offline Argon2id recovery key (`RISK-014`).
4. **Theme 4 — Scope Discipline on a Zero-Code Repository (`RISK-004`, `RISK-015`, `RISK-016`):**
   With `0` `.cs` files in `CyberPipe.slnx`, implementation must begin with a strict **Phase 0 Walking Skeleton** that proves the encryption, sandbox, and IPC invariants before building the 16 domain skills.

---

## Cross-References
- **ATAM Quality Attribute Scenarios (`QAS-001`..`QAS-014`):** [`quality-attribute-scenarios.md`](quality-attribute-scenarios.md)
- **ATAM Trade-Off Analysis:** [`tradeoff-analysis.md`](tradeoff-analysis.md)
- **Threat Register (`THREAT-001`..`THREAT-016`):** [`../04-threat-model/threat-register.md`](../04-threat-model/threat-register.md)
- **Validation Test Plan (`TEST-001`..`TEST-015`):** [`../06-validation/assumptions-and-open-questions.md`](../06-validation/assumptions-and-open-questions.md)
