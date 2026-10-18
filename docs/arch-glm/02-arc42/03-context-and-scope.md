# arc42 §3 — Context and Scope

Part of the [arc42 documentation](01-introduction-and-goals.md).

---

## 3.1 System context diagram

```mermaid
flowchart LR
    subgraph ExternalActors ["External actors"]
        Owner["Owner-user<br/>(Telegram Owner role)"]
        Guest["Household guest<br/>(Telegram Guest role)"]
        Admin["Local OS user<br/>(interactive session)"]
    end

    subgraph ExternalSystems ["External systems"]
        TG["Telegram servers<br/>(text / voice / files / buttons)"]
        Ollama["Ollama / LM Studio<br/>(localhost:11434, Local LLM)"]
        CloudLLM["Cloud LLM providers<br/>(OpenAI / Anthropic / Google / DeepSeek)"]
        OSVault["OS secret vault<br/>(DPAPI / LibSecret / Keychain)"]
        OSProviders["OS providers<br/>(OCR / Audio / Git / dotnet / docker)"]
        RabbitMQopt["RabbitMQ<br/>(enterprise option, not MVP)"]
    end

    CyberPipe["<b>CyberPipe</b><br/>local assistant + orchestrator"]
    UI["CyberPipe.UI.Desktop.exe<br/>(Avalonia)"]
    Brainstem["worker/CyberPipe.Brainstem.exe<br/>(isolated)"]

    Owner -->|chat / voice / files| TG
    Guest -->|chat (limited)| TG
    TG -->|webhook / long-poll| CyberPipe
    Admin -->|CLI / TUI| CyberPipe
    UI -->|gRPC Named Pipe / UDS| CyberPipe
    CyberPipe -->|spawn + IPC| Brainstem
    CyberPipe -->|HTTP localhost| Ollama
    CyberPipe -->|HTTPS| CloudLLM
    CyberPipe -->|read master key| OSVault
    CyberPipe -->|OCR / audio / git / dotnet| OSProviders
    CyberPipe -.->|optional AMQP| RabbitMQopt
```

**Note**: Telegram network latency is **outside** the local SLO budgets (`AUD-06`). The
fast-path numbers (`Tier 0 < 1 ms`, `/stop < 50 ms`) measure host-internal processing only.

## 3.2 External actors and interfaces

| Actor | Interface | Trust | AuthN / AuthZ |
| :--- | :--- | :--- | :--- |
| Owner-user | Telegram chat | Whitelisted `TelegramUserId` (`Owner` role) | Crypto-confirmed Telegram ID; full skill access; budget approvals. |
| Household guest | Telegram chat | Whitelisted (`Guest` role) | Telegram ID; media/volume/status/timers only; ≤40% volume; no Tier 3, no limit changes. |
| Local OS user | CLI / TUI over Named Pipe / UDS | OS user session `>= 1` | `PID` + `SessionNonce`; command authorization by role (`Owner` vs `Guest`); guest cannot run Tier 3, secrets, or limit commands. |
| Avalonia UI client | gRPC Named Pipe / UDS | Local | `CurrentUserOnly` pipe flag + PID verification; UI depends only on `CyberPipe.Contracts`. |
| `CyberPipe.Brainstem` worker | Length-prefixed binary Named Pipe | Spawned by daemon | `PID` match + one-time `SessionNonce` (TTL, burn-after-use). |
| Cloud LLM provider | HTTPS | Untrusted | API key from `SecretsVault`; egress sanitizer strips secrets/PII; `TokenBudgetGovernor` enforces caps. |
| Ollama / LM Studio | HTTP `localhost:11434` | Local | Local-only; no USD budget; `ISystemResourceMonitor` gates by RAM/VRAM. |
| OS secret vault | DPAPI / LibSecret / Keychain | OS-bound to user profile | `ProtectedData.Protect` (CurrentUser scope); optional Argon2id master password. |

## 3.3 Major subsystems and containers

See [§5 building block view](05-building-block-view.md) for the full component breakdown.
Headline subsystems:

1. **Gateway** — `CyberPipe.Gateway.Telegram`, `CyberPipe.Gateway.Api` (gRPC server + Hangfire dashboard).
2. **Router & Skills** — `CyberPipe.Router` (4-tier), `CyberPipe.Skills.Host`, `CyberPipe.Skills.Netrunner`.
3. **Security** — `Guards`, `Budget`, `Sanitization`, `Telemetry`.
4. **Core** — `Orchestration`, `Agents`.
5. **Execution** — `Abstractions`, `Providers`, `CyberPipe.Brainstem`.
6. **Storage** — `CyberPipe.Storage.Sqlite` (master + subsystem manager).
7. **UI** — `CyberPipe.UI`, `CyberPipe.UI.Desktop`.
8. **Host** — `CyberPipe` (daemon + CLI dispatcher).

## 3.4 Dependencies and communication mechanisms

| Between | Mechanism | Frame limit / notes |
| :--- | :--- | :--- |
| Telegram → CyberPipe | Telegram Bot API (long-poll or webhook) | Out-of-scope latency. |
| UI / CLI → CyberPipe | gRPC over Named Pipe (Win) / UDS (Linux/mac) | `MaxReceive/SendMessageSize = 4 MB`; `KeepAliveTimeout = 10 s`. |
| CyberPipe → Brainstem | Length-prefixed binary Named Pipe | `maxNumberOfServerInstances = 1`; header `frameLength <= 16 MB` else drop. |
| CyberPipe → Ollama | HTTP `localhost:11434` via `OllamaSharp` | Local model context `8K–16K`. |
| CyberPipe → Cloud LLM | HTTPS via `Microsoft.Extensions.AI` | Budget-gated, sanitized. |
| CyberPipe → OS vault | DPAPI / LibSecret / Keychain | Per-DB 256-bit keys under `db:secret:{SubsystemKey}`. |
| CyberPipe → SQLite | `Microsoft.Data.Sqlite` + Dapper | WAL; `BusyTimeout = 5000`; one file per subsystem. |
| CyberPipe → MassTransit | InMemory / SQLite (`masstransit.db`) / RabbitMQ | Switchable from UI. |

## 3.5 Critical data flows (high level)

Detailed in [04-threat-model/data-flows.md](../../04-threat-model/data-flows.md). Summary:

- **Path A — Fast command** (`Telegram → AuthGuard → Tier 0/1 → HostGuard → HostSkill → SQLite`): ~10–30 ms, 0 tokens.
- **Path B — Complex task** (`Telegram → AuthGuard → Router → BudgetGov → Sanitizer → Tier 2/3 → SecurityGuard → Sandbox → response`): tokens metered, auto-pause, HITL approvals.
- **Path C — Proactive** (`Hangfire cron → local SQLite aggregation → Telegram digest`): 0–200 tokens.
- **Path D — Voice STT** (`Telegram voice → Brainstem Whisper.net → text → Tier 0/1`): 0 cloud tokens.
- **Path E — Untrusted doc parse** (`Telegram PDF → Brainstem (SingleUse, UntrustedExternal) → Hard Tool Stripping → Tier 3 → HITL`).

## 3.6 Scope boundaries

- **In scope**: all `src/*` and `tests/*` projects listed in [`06`](../../plans/06-solution-structure.md); daemon `CyberPipe.exe`, desktop `CyberPipe.UI.Desktop.exe`, worker `worker/CyberPipe.Brainstem.exe`.
- **Out of scope**: Telegram server, Ollama/LM Studio runtime, cloud LLM APIs, OS-native runtimes (`onnxruntime`, `whisper`, `e_sqlite3`), optional RabbitMQ broker (enterprise).

## 3.7 External system trust posture

| External | Posture | Why |
| :--- | :--- | :--- |
| Telegram | Authentic channel but **untrusted content** | Messages may contain prompt-injection or 18+ content; `MemoryTrustSource.UntrustedExternal` for forwarded/external. |
| Cloud LLM | Untrusted egress destination | Receives only sanitizer output; never raw secrets. |
| Ollama / LM Studio | Trusted local | Localhost; but output may still be `UntrustedExternal` if model is fed external text. |
| OS vault | Trusted | OS-bound; fail-closed if unavailable. |
| OS providers (OCR/audio/git/dotnet) | Mixed | `git`/`dotnet` execute arbitrary project code → must be sandboxed (`AUD-08`). |
