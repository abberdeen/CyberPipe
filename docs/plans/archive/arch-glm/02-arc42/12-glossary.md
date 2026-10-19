# arc42 §12 — Glossary

Part of the [arc42 documentation](01-introduction-and-goals.md).

---

| Term | Meaning | Source |
| :--- | :--- | :--- |
| **AppContainer** | Windows low-integrity sandbox profile with empty capabilities (no network, no file access by default). | [`15`](../../plans/15-cli-and-execution-modes.md) §3.1 |
| **Anti-Wipe** | Hard block on destructive operations (`Format-Volume`, `rm -rf`, `del /s`, registry/system-folder modification). | [`05`](../../plans/05-security-agent.md) §1.B |
| **AUD-xx** | Audit finding identifier from the muse-wonder audit. | [`muse-wonder-audit.md`](../../plans/muse-wonder-audit.md) §J |
| **Brainstem** | Isolated worker process (`worker/CyberPipe.Brainstem.exe`) running Whisper.net STT and untrusted PDF/DOCX parsing. | [`15`](../../plans/15-cli-and-execution-modes.md) §1 |
| **BrainstemHandshake** | Protocol-v1 handshake: `ProtocolVersion`, `ProcessId`, `SessionNonce`, `WorkerManifestSha256`. | [`15`](../../plans/15-cli-and-execution-modes.md) §5 |
| **Bulkhead Lanes** | Isolated execution compartments with separate bounded channels and semaphores. | [`14`](../../plans/14-fault-isolation-and-resilience.md) §2 |
| **CentralDataSanitizer** | Egress-gateway filter that masks secrets/PII as `[REDACTED:ID]` before cloud LLM / Serilog / Telegram. | [`09`](../../plans/09-data-protection-and-state-hub.md) §1 |
| **Cloud Freeze** | UI toggle that blocks all RemoteCloud models while keeping Local models working. | [`13`](../../plans/13-model-advisor-and-tier-configuration.md) §1 |
| **cyberpipe_master.db** | Central SQLite master DB holding only `AppGlobalSettings` + `SubsystemDatabaseRegistry`. | [`09`](../../plans/09-data-protection-and-state-hub.md) §3.1 |
| **data/subsystems/*.db** | Isolated per-subsystem SQLite DBs (hangfire, masstransit, knowledge, crm, finance, budget, health, netrunner). | [`09`](../../plans/09-data-protection-and-state-hub.md) §3.2 |
| **DeferRetry** | Netrunner control signal (exit code 75) to free RAM and reschedule a step after `retryAfterSeconds`. | [`16`](../../plans/16-netrunner-etl-and-workflow-engine.md) §3.3 |
| **DPAPI** | Windows Data Protection API — `ProtectedData.Protect` with `CurrentUser` scope. | [`15`](../../plans/15-cli-and-execution-modes.md) §3.3 |
| **Dynamic Tool Pruning** | Selecting only 2–3 relevant tool schemas per request instead of all 50. | [`04`](../../plans/04-agent-mesh-proposals.md) §1 (Tier 2) |
| **EphemeralWorkspace** | `IAsyncDisposable` temp directory for a sandbox task; auto-cleaned on completion or `/stop`. | [`10`](../../plans/10-sandbox-telemetry-and-heuristics.md) §3 |
| **EnsureSafeVolumeBeforePlayback** | Pre-`Play` check that lowers system volume to the safe ceiling if it was manually raised. | [`05`](../../plans/05-security-agent.md) §1.V |
| **ExecutionTelemetryDigest** | Out-of-band compressed digest (1 GB → 30 KB) of sandbox activity. | [`10`](../../plans/10-sandbox-telemetry-and-heuristics.md) §2.B |
| **GlobalStateHub** | Reactive singleton broadcasting system state (bus, budget, workers) over gRPC to UI. | [`09`](../../plans/09-data-protection-and-state-hub.md) §4 |
| **Hard Tool Stripping** | Physical removal of host-tool schemas from `ChatOptions.Tools` when context is `TaintedContext`. | [`05`](../../plans/05-security-agent.md) §3.3 |
| **HITL** | Human-in-the-Loop: owner approval gate (`HitlApprovalNode`). | [`16`](../../plans/16-netrunner-etl-and-workflow-engine.md) §3 |
| **IHostSkill** | Closed set of compiled C# skills (Media, Volume, Workspace, etc.); the only way chat touches the host. | [`05`](../../plans/05-security-agent.md) §1.B |
| **InFlightOperations** | SQLite table (heavy tasks) / in-memory dict (Tier 0/1) tracking running operations for poison-pill detection. | [`14`](../../plans/14-fault-isolation-and-resilience.md) §4 |
| **IsolatedWorkerBase** | Base class in `CyberPipe.Security.Telemetry` for all background workers; per-iteration try/catch + 30 s timeout + 3-strikes quarantine. | [`14`](../../plans/14-fault-isolation-and-resilience.md) §2 |
| **ISubsystemDatabaseManager** | Service that lazy-creates subsystem DBs, manages their keys, and issues `SqliteConnection` per subsystem. | [`09`](../../plans/09-data-protection-and-state-hub.md) §4 |
| **Job Object** | Windows kernel object enforcing process limits (`KillOnJobClose`, `ActiveProcessLimit=1`, CPU rate cap, memory). | [`15`](../../plans/15-cli-and-execution-modes.md) §6 |
| **KnowledgeVault** | Long-term memory: `SQLite FTS5` + in-memory SIMD vectors for "second brain". | [`12`](../../plans/12-assistant-capabilities-and-skills.md) §1.3 |
| **Local** (`ModelHostingType.Local`) | Model running on user PC via `Ollama`/`LM Studio` at `localhost:11434`; $0/token; RAM/VRAM gated. | [`13`](../../plans/13-model-advisor-and-tier-configuration.md) §1 |
| **OTT** | One-Time Token (256-bit, TTL 30 s) for Hangfire dashboard login; SHA-256 stored; burn-after-use. | [`03`](../../plans/03-tech-stack-libraries.md) Hangfire section |
| **PriorityControlChannel** | Dedicated bulkhead lane for `/stop`, `/pause`, status; bypasses normal queue. | [`04`](../../plans/04-agent-mesh-proposals.md) §3 |
| **QuotaBoundedStream** | Stream wrapper that aborts reads beyond `16 MB` with `QuotaExceededException`. | [`14`](../../plans/14-fault-isolation-and-resilience.md) §3 |
| **RemoteCloud** (`ModelHostingType.RemoteCloud`) | Paid external LLM API; strict budget + sanitizer. | [`13`](../../plans/13-model-advisor-and-tier-configuration.md) §1 |
| **RestrictedWorkerCustomSid** | `S-1-5-99-214-1` SID added to Brainstem's `SidsToRestrict` and denied in `data/` DACL (Profile B). | [`15`](../../plans/15-cli-and-execution-modes.md) §3.1 |
| **SecretsVault** | `data/secrets.vault` file (AES-256-GCM); master key in OS vault. | [`09`](../../plans/09-data-protection-and-state-hub.md) §2 |
| **SemanticIntentCache / SemanticResponseCache** | Caches for phrase→intent and (role-scoped, query-hashed) response; TTL 24 h. | [`04`](../../plans/04-agent-mesh-proposals.md) §1 (Tier 1) |
| **SessionNonce** | 256-bit one-time nonce generated per Brainstem run; verified at handshake; burn-after-use; TTL-bounded. | [`15`](../../plans/15-cli-and-execution-modes.md) §4 |
| **SQLCipher** | SQLite page-encryption provider (`SQLitePCLRaw.bundle_e_sqlcipher`); PoC-pending (`AUD-04`). | [`17`](../../plans/17-adr-mvp.md) ADR-003 |
| **STRIDE** | Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege. | [00-methodology.md](../00-methodology.md) §3 |
| **TaintedContext** | Session state when context includes `UntrustedExternal` data; triggers Hard Tool Stripping. | [`05`](../../plans/05-security-agent.md) §3.3 |
| **Tier 0/1/2/3** | 4-tier intent router: regex (T0), ONNX embeddings + cache (T1), light LLM (T2), heavy LLM + sandbox (T3). | [`04`](../../plans/04-agent-mesh-proposals.md) §1 |
| **TokenBudgetGovernor** | `DelegatingChatClient` middleware enforcing per-task / per-minute / per-day token+USD budgets. | [`04`](../../plans/04-agent-mesh-proposals.md) §2 |
| **ToolOutputTruncator** | Caps tool output at 1 500 chars (head 500 + tail 800 + error lines); full log kept on disk. | [`10`](../../plans/10-sandbox-telemetry-and-heuristics.md) §2.A |
| **TrustSource** (`MemoryTrustSource`) | Per-row trust tag (`SystemPrompt`, `OwnerExplicit`, `GuestExplicit`, `ToolOutput`, `UntrustedExternal`). | [`05`](../../plans/05-security-agent.md) §3.3 |
| **Volume Clamp** | `Math.Clamp` on system volume to `60%` day / `25%` night, with pre-`Play` enforcement. | [`05`](../../plans/05-security-agent.md) §1.V |
| **WAL** | Write-Ahead Logging — SQLite journal mode for atomicity + concurrent readers. | [`09`](../../plans/09-data-protection-and-state-hub.md) §3 |
| **worker.manifest.json** | SHA-256 manifest of all `.exe`/`.dll` in `worker/`; verified before each Brainstem spawn. | [`15`](../../plans/15-cli-and-execution-modes.md) §8.2 |

## Abbreviations

| Abbreviation | Meaning |
| :--- | :--- |
| ATAM | Architecture Tradeoff Analysis Method |
| ADR | Architecture Decision Record |
| DAG | Directed Acyclic Graph |
| DoD | Definition of Done |
| EoP | Elevation of Privilege |
| HITL | Human-in-the-Loop |
| IPC | Inter-Process Communication |
| LLM | Large Language Model |
| MADR | Markdown ADR |
| OTT | One-Time Token |
| PoC | Proof of Concept |
| QAS | Quality Attribute Scenario |
| SLO | Service Level Objective |
| STT | Speech-to-Text |
| UDS | Unix Domain Socket |
| VRAM | Video RAM |
