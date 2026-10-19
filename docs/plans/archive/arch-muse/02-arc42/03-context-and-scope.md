# arc42 §3 — Context and Scope

All components below are **proposed** (no implementation exists). External systems are
real-world counterparts the design depends on.

```mermaid
flowchart LR
    subgraph Users ["Actors"]
        Owner["Owner (Telegram / UI / CLI)"]
        Guest["Guest (restricted)"]
    end
    subgraph External ["External systems"]
        TG["Telegram Bot API"]
        Cloud["Cloud LLM APIs"]
        Ollama["Ollama / LM Studio (localhost)"]
        Media["Media provider APIs"]
        OSINT["Passive OSINT APIs"]
    end
    subgraph System ["CyberPipe (system boundary)"]
        GW["Gateway: Telegram bot, gRPC IPC, CLI/TUI"]
        RT["4-Tier Router + Budget/Safety guards"]
        EX["Execution: skills, Netrunner, sandboxes, Brainstem worker"]
        ST["Storage: SQLite master + subsystem DBs, vault"]
        UI["Avalonia UI (Contracts-only client)"]
    end
    Owner <--> GW
    Guest -->|restricted| GW
    GW --> RT --> EX
    EX <--> ST
    UI <-->|gRPC Named Pipes| GW
    GW <--> TG
    RT <-->|sanitized egress only| Cloud
    RT --> Ollama
    EX --> Media
    EX --> OSINT
```

## External actors and interfaces

| Actor / system | Interface | Trust |
| :--- | :--- | :--- |
| Owner | Telegram chat, Avalonia UI, CLI/TUI | Trusted identity (whitelisted `TelegramUserId`), untrusted content |
| Guest | Telegram chat (subset) | Partially trusted; barred from Tier 3, budgets, secrets |
| Attacker (untrusted third party) | Forged/spoofed messages, malicious files/links forwarded into chat | Untrusted — must fail at `TelegramAuthGuard` |
| Telegram Bot API | Long polling / webhook, voice, files, inline buttons | Transport-trusted; content untrusted |
| Cloud LLM APIs | HTTPS, `IChatClient` via `TokenBudgetGovernor` + `SanitizingChatClient` | Untrusted with secrets (egress-sanitized); billing-relevant |
| Ollama / LM Studio | `http://localhost:11434` via `OllamaSharp` | Local-but-external process; availability not guaranteed |
| Media providers, OSINT APIs | HTTPS allow-listed endpoints | Untrusted content; fail-closed on outage |

## Trust boundaries (summary; full model in `04-threat-model/`)

`TB-01` chat→host (AuthGuard) · `TB-02` router→cloud (egress sanitizer) ·
`TB-03` daemon→Brainstem/sandbox (OS isolation + IPC handshake) ·
`TB-04` tainted content→tools (hard tool stripping) · `TB-05` UI/CLI→daemon (IPC auth +
role authorization). Detail:
[scope-assets-and-trust-boundaries.md](../04-threat-model/scope-assets-and-trust-boundaries.md).
