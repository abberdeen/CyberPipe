# 12. Glossary

> **Document Role:** Defines canonical architectural, domain, security, and runtime terms used across **CyberPipe**, and explicitly reconciles naming drift discovered across `docs/plans/` (`RISK-016`).

---

## 12.1 Core Architectural & Domain Terms

| Term | Canonical Definition in CyberPipe |
| :--- | :--- |
| **`Brainstem` (`CyberPipe.Brainstem.exe`)** | The out-of-process worker executable spawned by `BrainstemSupervisor` into two OS-isolated pools (**Pool A: `HeavyCompute`** for `Whisper.net`/ONNX and **Pool B: `UntrustedParser`** for `PdfPig`/DOCX/HTML) to isolate native C++ memory hazards and untrusted document parsing from `CyberPipe.exe` (`REQ-002`, [`ADR-0004`](../05-adr/0004-brainstem-process-isolation-and-sandboxing.md)). |
| **`CentralDataSanitizer`** | Two-stage (`Regex` + structural entropy/NER) deterministic data scrubber in `CyberPipe.Security` that redacts secrets from `SecretsVault`, API tokens, credit cards (Luhn), SSN/INN, emails, and file paths before any payload is sent to a cloud LLM or written to logs (`REQ-005`, [`ADR-0006`](../05-adr/0006-secrets-vault-and-egress-sanitization.md)). |
| **Contour 1 IPC** | The local Named Pipe channel (`\\.\pipe\CyberPipe.Ipc.{Username}` on Windows; `0600` Unix Domain Socket on Linux/macOS) connecting the `cyberpipe.exe` Native AOT CLI client to `CyberPipe.exe` using JSON-RPC 2.0 (`REQ-016`, [`ADR-0005`](../05-adr/0005-ipc-security-and-hangfire-dashboard-auth.md)). |
| **Contour 2 IPC** | The internal Named Pipe channel (`\\.\pipe\CyberPipe.Brainstem.{InstanceGuid}`) connecting `BrainstemSupervisor` in `CyberPipe.exe` to child `CyberPipe.Brainstem.exe` worker processes using 4-byte length-prefixed Protobuf frames (`<= 8 MB` max frame size) (`REQ-002`, [`ADR-0005`](../05-adr/0005-ipc-security-and-hangfire-dashboard-auth.md)). |
| **Hard Tool Stripping** | Zero-Trust security control enforced in C# (`PipelineOrchestrator`) where `ChatOptions.Tools` is physically emptied (`Array.Empty<AITool>()`) whenever `ContextContainsUntrustedData == true`, making it structurally impossible for a prompt-injected LLM to invoke state-mutating or shell tools (`REQ-006`, `QAS-007`). |
| **`HITL` (`HitlApprovalNode`)** | **Human-in-the-Loop** approval gate. Pauses a `Netrunner` workflow DAG or destructive `Tier 2/3` skill execution, persists the checkpoint to `netrunner.db` / `InFlightOperations`, and sends an interactive approval prompt (e.g., Telegram inline keyboard) to the Owner (`REQ-013`, `QAS-011`). |
| **`InFlightOperations`** | Durable crash-recovery ledger table (assigned to `data/cyberpipe_master.db` per [`ADR-0003`](../05-adr/0003-decentralized-sqlite-and-sqlcipher-encryption.md)) that records active tasks before execution so interrupted non-idempotent operations can require explicit Owner confirmation after a crash (`REQ-010`). |
| **L1 `SemanticIntentCache`** | Persistent vector cache mapping normalized user utterances (384-d embeddings via `all-MiniLM-L6-v2`) to deterministic `SkillId` + parameter templates when cosine similarity $\ge 0.82$ (`REQ-003`). |
| **L2 `SemanticResponseCache`** | Persistent vector cache returning full cached responses when cosine similarity $\ge 0.92$ and the target subsystem database's mutation epoch / `PRAGMA data_version` has not changed (`REQ-003`, `QAS-002`, `RISK-010`). |
| **`Netrunner` Package** | A deterministic 6-file ETL skill unit (`netrunner.manifest.json`, `schema.input.json`, `schema.output.json`, `SKILL.md`, entrypoint script/module, and `tests/` fixtures) executed inside a resource-capped (`64 MB` RAM) sandbox (`REQ-013`, [`ADR-0007`](../05-adr/0007-proactive-scheduling-and-netrunner-dag-engine.md)). |
| **`OTT` (One-Time Token)** | A single-use, cryptographically random 256-bit token with a `60 s` TTL generated via `cyberpipe dashboard open` to authenticate the Owner's local browser at `http://127.0.0.1:5080/hangfire` and exchange for an `HttpOnly; SameSite=Strict` session cookie (`REQ-011`, [`ADR-0005`](../05-adr/0005-ipc-security-and-hangfire-dashboard-auth.md)). |
| **`SecretsVault` (`vault.bin`)** | Hardware-backed local credential store in `data/vault.bin` encrypted with `AES-256-GCM`, whose `32-byte` master key is sealed by Windows `DPAPI` (`CurrentUser`) or Linux/macOS OS keychains (`REQ-005`, [`ADR-0006`](../05-adr/0006-secrets-vault-and-egress-sanitization.md)). |
| **`TaintedData<T>`** | Immutable C# wrapper struct in `CyberPipe.Contracts` pairing a payload `T` with its `OriginTrustLevel` (`Owner`, `TrustedAutomation`, `UntrustedExternal`) and `SourceIdentifier` to enforce compile-time and runtime taint tracking (`REQ-006`). |
| **`Tier 0` .. `Tier 3` Routing** | CyberPipe's 4-tier cascading intent router (`REQ-003`): **Tier 0** (Compiled Regex / Aho-Corasick, `< 1 ms`, `0` tokens), **Tier 1** (Local ONNX embedding + SIMD cache lookup, `< 15 ms`, `0` tokens), **Tier 2** (Single/bounded LLM tool-calling, `<= 3` cloud turns), **Tier 3** (Sandboxed multi-step agent / code execution). |
| **`TokenBudgetGovernor`** | Financial circuit breaker in `CyberPipe.Cognition` backed by `data/subsystems/budget.db` that enforces daily/monthly USD caps, per-call `MaxOutputTokens`, and turn limits, automatically pausing paid cloud LLMs at 100% spend and routing to local `Ollama`/`LlamaSharp` (`REQ-009`, `QAS-009`). |
| **`VolumeClamp`** | Hardware acoustic safety invariant in `CyberPipe.Hardware` that clamps any programmatic volume increase to **`<= 60%`** during daytime (`08:00–22:00`) and **`<= 25%`** during quiet hours (`22:00–08:00`), preventing startle or hearing hazards (`REQ-012`, `QAS-014`). |

---

## 12.2 Reconciliation of Cross-Document Terminology Drift (`RISK-016`)

Several terms and identifiers vary across `docs/plans/01`..`17`. The table below establishes the **single canonical identifier** for implementation:

| Concept | Variant A in `docs/plans/` | Variant B in `docs/plans/` | Canonical Identifier in `docs/arch-argon/` |
| :--- | :--- | :--- | :--- |
| **Trust Level Enum (`CyberPipe.Contracts`)** | `OwnerExplicit`, `ExternalUntrusted` (`05-security-and-zero-trust.md`) | `TrustedOwner`, `UntrustedExternal` (`12-proactive-automation-and-scheduling.md`) | `enum OriginTrustLevel { Owner = 0, TrustedAutomation = 1, UntrustedExternal = 2 }` |
| **Max LLM Turns Per Task** | `MaxLlmTurnsPerTask = 3` globally (`02-architecture-overview.md`, `08-token-optimization-and-caching.md`) | `3` for Cloud, `5` for Local (`13-llm-and-model-routing.md`) | `MaxCloudLlmTurnsPerTask = 3`, `MaxLocalLlmTurnsPerTask = 5` |
| **CLI Scheduling Command** | `cyberpipe schedule list/add/remove` (`12-proactive-automation-and-scheduling.md`) | `CyberPipe cron list/add/Pause` (`15-cli-and-execution-modes.md`) | `cyberpipe schedule` (with `cron` registered as a command alias) |
| **Main CLI Binary Name** | `CyberPipe` (`15-cli-and-execution-modes.md` examples) | `cyberpipe.exe` (`04-project-structure-and-dependencies.md`) | `cyberpipe` / `cyberpipe.exe` (`CyberPipe.Cli` project) |
| **Brainstem Task Enum (`CyberPipe.Contracts`)** | `Ping = 0, WhisperTranscribe = 1, DocumentExtractText = 2` (`15-cli-and-execution-modes.md`) | Netrunner script execution referenced in `16-netrunner-etl-and-workflow-engine.md` | Keep `Brainstem` for `WhisperTranscribe` (Pool A) and `DocumentExtractText` (Pool B); route `Netrunner` scripts through `ISandboxProvider` (`TB-05`) |

---

## Cross-References
- **Architecture Overview & Requirements Traceability:** [`../01-overview.md`](../01-overview.md)
- **Assumptions & Open Questions:** [`../06-validation/assumptions-and-open-questions.md`](../06-validation/assumptions-and-open-questions.md)
