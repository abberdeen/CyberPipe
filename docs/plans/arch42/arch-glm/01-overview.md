# 01. Overview

This is the entry-point summary. Deeper analysis lives in the
[arc42 sections](02-arc42/), the [ATAM analysis](03-atam/), the
[threat model](04-threat-model/), the [ADRs](05-adr/), and the
[validation folder](06-validation/).

---

## 1. System purpose

CyberPipe is a local, single-user assistant and autonomous-agent orchestrator running on a
personal/work PC under **.NET 10**. It is controlled through **Telegram** (text, voice,
files, inline buttons) and an **Avalonia UI** desktop client, and exposes a system
**CLI/TUI**. The design's central thesis is: resolve 70–85% of daily requests locally in
milliseconds at **0 cloud tokens**, and only escalate ~5–10% of genuinely complex tasks to a
(local or remote) LLM under strict budget and security controls.

Expected capabilities (all **designed**, none implemented — see status):

- 4-tier intent router (`Tier 0` regex → `Tier 1` ONNX embeddings + semantic cache → `Tier 2`
  light LLM → `Tier 3` heavy LLM + sandbox). Source: [`04-agent-mesh-proposals.md`](../plans/04-agent-mesh-proposals.md).
- Host safety guards: no raw shell from chat, Anti-Wipe, Volume Clamp (`60%/25%` day/night),
  NSFW filter, trusted media whitelist. Source: [`05-security-agent.md`](../plans/05-security-agent.md).
- `TokenBudgetGovernor` with per-task / per-minute / per-day ceilings, auto-pause
  (`TaskLifecycleState.PausedByBudget`), and instant `/stop` kill-switch. Source: [`04`](../plans/04-agent-mesh-proposals.md).
- Decentralized SQLite-WAL storage: `cyberpipe_master.db` + `data/subsystems/*.db`, each
  with its own key in `SecretsVault`. Source: [`09-data-protection-and-state-hub.md`](../plans/09-data-protection-and-state-hub.md).
- Isolated `CyberPipe.Brainstem` worker process for `Whisper.net` STT and untrusted
  PDF/DOCX parsing, sandboxed at OS level. Source: [`15-cli-and-execution-modes.md`](../plans/15-cli-and-execution-modes.md).
- `Netrunner` ETL sub-assistant: standardized signed script packages executed as Mini-n8n
  DAG workflows with HITL checkpoints. Source: [`16-netrunner-etl-and-workflow-engine.md`](../plans/16-netrunner-etl-and-workflow-engine.md).
- Personal / dev / business skill catalog (≥16 skills). Source: [`12-assistant-capabilities-and-skills.md`](../plans/12-assistant-capabilities-and-skills.md).

---

## 2. Stakeholders and concerns

Stakeholders are **inferred from the plans**; no stakeholder was interviewed.

| Stakeholder | Concerns |
| :--- | :--- |
| **Owner-user** (single principal, Telegram `Owner` role) | Fast daily assistance; 0-token routine; cost ceiling; PC never wiped; no 18+/loud content from chat; recoverable after crash/power loss. |
| **Household guest** (Telegram `Guest` role) | Limited media/volume access; cannot trigger Tier 3 or change limits. |
| **Host OS** | Resource budget (no freezes, no OOM); filesystem isolation of `data/`; no unauthorized child processes. |
| **External cloud LLM providers** (OpenAI/Anthropic/Google/DeepSeek) | Receive only sanitized prompts; never receive raw secrets; rate/budget compliance. |
| **Implementer / maintainer** | Clear module DAG; reversible decisions; testable DoD; no hidden cross-deps. |
| **Reviewer / approver** | Each claimed security guarantee traced to a verified control, not just a library. |

---

## 3. Functional requirements (derived from plans)

Functional requirements below are tagged `[Explicit]` (stated in a plan) or `[Inferred]`
(reasoned from the plan text). Each maps to plan evidence.

| ID | Requirement | Origin | Evidence |
| :--- | :--- | :--- | :--- |
| REQ-001 | Accept text, voice, file, and inline-button inputs from Telegram; whitelist users by numeric `TelegramUserId` with `Owner`/`Guest` roles. | Explicit | [`05`](../plans/05-security-agent.md) §1 |
| REQ-002 | Resolve slash-commands and regex patterns in `< 1 ms` at 0 tokens (`Tier 0`). | Explicit (SLO target) | [`04`](../plans/04-agent-mesh-proposals.md) §1 |
| REQ-003 | Resolve natural-language intents locally via ONNX embeddings + SIMD cosine (`Tier 1`) at 0 tokens, threshold `0.86`. | Explicit | [`04`](../plans/04-agent-mesh-proposals.md) §1 |
| REQ-004 | Allow user to assign a Local or RemoteCloud model to `Tier 2` and `Tier 3` from UI. | Explicit | [`13`](../plans/13-model-advisor-and-tier-configuration.md) §1 |
| REQ-005 | Enforce per-task / per-minute / per-day token+USD budgets with auto-pause and `/stop` < 50 ms. | Explicit (SLO target) | [`04`](../plans/04-agent-mesh-proposals.md) §2–3 |
| REQ-006 | Never translate chat messages into raw `cmd`/`powershell`/`bash`; only compiled C# skills and sandboxed generated code. | Explicit | [`05`](../plans/05-security-agent.md) §1.B |
| REQ-007 | Clamp volume to `60%` day / `25%` night; pre-check `EnsureSafeVolumeBeforePlayback` before `Play`/`Resume`. | Explicit | [`05`](../plans/05-security-agent.md) §1.V |
| REQ-008 | Provide decentralized SQLite-WAL storage with master registry and per-subsystem DBs; each DB has its own vault key. | Explicit | [`09`](../plans/09-data-protection-and-state-hub.md) §3 |
| REQ-009 | Isolate `Whisper.net` STT and untrusted PDF/DOCX parsing in `CyberPipe.Brainstem` under OS sandbox. | Explicit | [`15`](../plans/15-cli-and-execution-modes.md) §1, §3 |
| REQ-010 | Run Tier-3 generated scripts only inside `Wasmtime`/`Docker`/OS-sandbox; native processes require owner approval. | Explicit | [`05`](../plans/05-security-agent.md) §1.B, [`01`](../plans/01-principles.md) §5 |
| REQ-011 | Strip host-tool schemas from `ChatOptions.Tools` when context is `TaintedContext` (`UntrustedExternal`). | Explicit | [`05`](../plans/05-security-agent.md) §3.3 |
| REQ-012 | Sanitize secrets/PII on egress (RemoteCloud LLM, Serilog logs, Telegram) without blind rehydration. | Explicit | [`09`](../plans/09-data-protection-and-state-hub.md) §1 |
| REQ-013 | Provide a CLI/TUI (`CyberPipe <command>`) with `--json` contract and exit codes `0..4`. | Explicit | [`15`](../plans/15-cli-and-execution-modes.md) §10 |
| REQ-014 | Provide a Netrunner DAG engine with 8 node types, HITL, crash-recovery, SHA-256 package integrity. | Explicit | [`16`](../plans/16-netrunner-etl-and-workflow-engine.md) §3–4 |
| REQ-015 | Provide proactive scheduling (Hangfire) on isolated `hangfire.db` with read-only dashboard on `127.0.0.1`. | Explicit | [`03`](../plans/03-tech-stack-libraries.md) Hangfire section, [`12`](../plans/12-assistant-capabilities-and-skills.md) §1.2 |
| REQ-016 | Selectively switch MassTransit transport (InMemory/SQLite/RabbitMQ) from UI. | Explicit | [`03`](../plans/03-tech-stack-libraries.md) MassTransit section |
| REQ-017 | Cross-platform (Windows/Linux/macOS) via OS contracts in `CyberPipe.Contracts`. | Explicit (goal) | [`01`](../plans/01-principles.md) §1 |
| REQ-018 | Local voice STT (Whisper.net) and local embeddings (ONNX) at 0 cloud tokens. | Explicit | [`12`](../plans/12-assistant-capabilities-and-skills.md) §1.1, [`04`](../plans/04-agent-mesh-proposals.md) §1 |
| REQ-019 | Persist Netrunner workflow state across crash/power loss with `ResumeFromCheckpoint` or `StopAndMarkCancelledByCrash` policy. | Explicit | [`16`](../plans/16-netrunner-etl-and-workflow-engine.md) §4.1 |
| REQ-020 | [Inferred] Maintain `MemoryTrustSource` tag on every memory/cache row; RAG filters by `TrustSource <= ToolOutput` for host-control commands. | Inferred | [`05`](../plans/05-security-agent.md) §3.3 (stated as design rule) |

---

## 4. Business and technical goals

**Business goals** (inferred):
- G1 — Daily personal/dev/business assistant that runs locally and cheaply.
- G2 — Cost predictability: hard USD/day ceiling (`DailyBudgetUsdCap = 0.50` default).
- G3 — Host integrity: the assistant can never wipe the system or blast audio.
- G4 — Recoverability: no data loss from crash, power loss, or `/stop`.

**Technical goals**:
- T1 — Sub-50 ms local control path (`/stop`, `/pause`).
- T2 — Zero cloud tokens for ~70–85% of requests.
- T3 — Strict module DAG enforced in CI by `ArchUnitNET`.
- T4 — OS-level isolation of untrusted parsing and generated code.
- T5 — Each subsystem owns its own SQLite DB; no cross-DB access.

---

## 5. Non-goals (explicit and inferred)

| Non-goal | Source |
| :--- | :--- |
| Multi-tenant / multi-user service. | Inferred from single-`Owner` design. |
| Cloud-hosted deployment. | Inferred; local-first, OS-session-bound. |
| Mandatory external broker (RabbitMQ) for MVP. | [`01`](../plans/01-principles.md) §1; [`ADR-001`](../plans/17-adr-mvp.md) (RabbitMQ = enterprise option). |
| Linux/macOS support in MVP. | [`L4`](../plans/17-adr-mvp.md); Windows 10/11 only first. |
| Mobile app. | Inferred; Telegram + Avalonia desktop only. |
| Replacing `Ollama`/`LM Studio` with an embedded generative LLM. | [`15`](../plans/15-cli-and-execution-modes.md) §1.2. |
| Running `dotnet`/`git` unsandboxed for "trusted" repos. | [`L5`](../plans/17-adr-mvp.md); forbidden. |

---

## 6. Scope boundaries and external systems

**In scope**: daemon host `CyberPipe.exe`, desktop `CyberPipe.UI.Desktop.exe`, isolated
worker `worker/CyberPipe.Brainstem.exe`, all `src/*` and `tests/*` projects defined in
[`06-solution-structure.md`](../plans/06-solution-structure.md).

**External systems** (out of scope but integrated):
- **Telegram** servers (text/voice/file transport, inline buttons). Network latency is
  explicitly **excluded** from local SLO budgets — see `AUD-06`.
- **Ollama / LM Studio** local HTTP server (`localhost:11434`) for Tier-2/3 local LLMs.
- **Cloud LLM providers** (OpenAI, Anthropic, Google, DeepSeek) for RemoteCloud tier.
- **Optional RabbitMQ** broker (enterprise option, not MVP).
- **OS-specific services**: DPAPI/LibSecret/Keychain (secret vault), Windows.Media.Ocr /
  Tesseract / Vision Framework (OCR), NAudio/PipeWire/CoreAudio (audio).

---

## 7. Architectural drivers

The drivers below are the concerns that shape the architecture most strongly.

| Driver | Why it drives design |
| :--- | :--- |
| **Latency** of control path | Drives Tier-0/1 in-process design, priority kill-channel, IPC p95 < 25 ms. |
| **Token cost** | Drives 4-tier cascade, dynamic tool pruning, output truncation, semantic caches, Netrunner ETL. |
| **Host integrity** from chat control | Drives compiled-skill-only API, Anti-Wipe, Volume Clamp, sandboxed Tier-3 execution. |
| **Secret confidentiality** | Drives egress sanitizer, no-blind-rehydration rule, vault + OS file isolation. |
| **Recoverability** | Drives WAL, step checkpointing, crash-recovery policy, side-by-side update + auto-rollback. |
| **Simplicity** (vs. enterprise bloat) | Tension: drives InMemory default + zero-dependency startup; conflicts with full stack (see `AUD-10`). |
| **Cross-platform** | Drives OS contract interfaces in `CyberPipe.Contracts` with per-OS implementations. |

---

## 8. Major assumptions and unknowns

These are detailed in
[06-validation/assumptions-and-open-questions.md](06-validation/assumptions-and-open-questions.md)
and summarized here:

- **ASSUMPTION-001** — SQLCipher provider (`SQLitePCLRaw.bundle_e_sqlcipher`) encrypts pages
  and wrong-key open fails. **Unverified** (`AUD-04`).
- **ASSUMPTION-002** — `AppContainer` / `RestrictedToken + Deny DACL` actually deny Brainstem
  read access to `data/`. **Unverified on every target OS** (`AUD-13`).
- **ASSUMPTION-003** — IPC PID/Nonce handshake resists squatting and replay. **Protocol not
  yet specified end-to-end** (`AUD-09`).
- **ASSUMPTION-004** — `dotnet build`/`test` and `git` can be safely sandboxed via fixed
  subcommand whitelist. **Not safe as stated** (`AUD-08`): build executes arbitrary project
  code.
- **ASSUMPTION-005** — Whisper.net runs under `AppContainer` on the user's GPU; if not,
  fallback is `RestrictedToken`. **GPU-driver-dependent**, unverified (`AUD-13`).
- **ASSUMPTION-006** — Netrunner manifest SHA-256 + OS sandbox routing provides execution
  isolation. Integrity is provable; isolation is **only as strong as the OS sandbox PoC**
  (`AUD-07`).
- **ASSUMPTION-007** — MassTransit consumers are idempotent via `MessageDeduplicationId`.
  **Per-consumer idempotency not specified** (`AUD-02`).

---

## 9. Architecture status and major unresolved decisions

See [README.md](README.md#current-architecture-status) for the status table and
[06-validation/assumptions-and-open-questions.md](06-validation/assumptions-and-open-questions.md)
for the full open-question list. Headline unresolved decisions:

1. SQLite encryption actually works at rest (PoC).
2. OS sandbox equivalence across Windows/Linux/macOS (PoC per OS).
3. IPC protocol fully specified + negative tests.
4. Dev-tool (`dotnet`/`git`) sandboxing strategy.
5. Per-consumer idempotency contracts for MassTransit.
6. Netrunner script execution isolation (beyond file integrity).
7. Master-key loss recovery runbook (tested).

---

## 10. Artifacts examined

**Repository artifacts** (inspected directly in this session):
- [`CyberPipe.slnx`](../../CyberPipe.slnx) — empty: `<Solution />`.
- [`.gitignore`](../../.gitignore) — standard Visual Studio .NET gitignore; no project-specific rules.
- [`.agents/skills/`](../../.agents/skills) — `netrunner-script-package/`, `rabbitmq-windows/` (project skill packs; not source code).
- `docs/` — `arch-argon/`, `arch-muse/`, `arch-wonder/`, `plans/`, `arch42.md` (this task brief). Per task instruction, only `docs/plans/` was used as the architectural source; other `arch-*` folders were **not** explored.

**Plan documents read in full** (all under [`docs/plans/`](../plans/)):
`README.md`, `01-principles.md`, `02-architecture-diagram.md`, `03-tech-stack-libraries.md`,
`04-agent-mesh-proposals.md`, `05-security-agent.md`, `06-solution-structure.md`,
`07-ui-isolation-and-arch-tests.md`, `08-implementation-roadmap.md`,
`09-data-protection-and-state-hub.md`, `10-sandbox-telemetry-and-heuristics.md`,
`11-changelog.md`, `12-assistant-capabilities-and-skills.md`,
`13-model-advisor-and-tier-configuration.md`, `14-fault-isolation-and-resilience.md`,
`15-cli-and-execution-modes.md`, `16-netrunner-etl-and-workflow-engine.md`,
`17-adr-mvp.md`, `muse-wonder-audit.md`.

**What does NOT exist** (confirmed by `glob`): no `*.cs`, `*.csproj`, `*.fsproj`,
`Directory.Packages.props`, `Directory.Build.props`, `global.json`, `NuGet.config`,
`appsettings.json`, `Dockerfile`, or `worker.manifest.json`. There is no code, build
system, dependency manifest, or test project to inspect — all claims about library behavior
are taken from the plans and from the (cited) vendor documentation, and tagged by
verification status in the [technology compatibility matrix](06-validation/technology-compatibility-matrix.md).

**Cross-document contradictions discovered** (mirrors muse-wonder audit §D):

| Topic | Plan A says | Plan B says | Resolution status |
| :--- | :--- | :--- | :--- |
| Message bus | `README.md` mentions `System.Threading.Channels` | `03`/`01` mandate MassTransit | ADR-001 accepts MassTransit; Channels = internal bounded queues only. README line is **stale**. |
| Master DB name | `09`: `cyberpipe_master.db` | `03`: `cyberpipe.db` | ADR-002 accepts `cyberpipe_master.db`. `03`'s `cyberpipe.db` is **invalid**. |
| Hangfire path | `09`/`12`: `data/subsystems/hangfire.db` | `03` legacy: `data/hangfire.db` | ADR-002 accepts `data/subsystems/hangfire.db`. |
| Semantic cache key | `04`/`08`: `(UserRole, ExpiresAtUtc)` | (no alternative) | `AUD-05`: key lacks query hash → cross-query leakage. Plan now states `(QueryHash, UserRole, DataVersion, ExpiresAtUtc)` in `04` §1, but `08` still shows the old key. **Not reconciled**. |
| Changelog tone | `11` writes in past tense ("спроектировано") | No code exists | `AUD-15`: must label as designed, not implemented. |

These contradictions are recorded as `AUD-xx` findings and tracked through the risk register.
