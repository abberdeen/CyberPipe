# arc42 §1 — Introduction and Goals

Part of the [arc42 documentation](../01-overview.md). See
[README.md](../README.md) for navigation.

---

## 1.1 Purpose and expected capabilities

CyberPipe is a local-first, single-user assistant and agent orchestrator on **.NET 10**,
controlled via **Telegram**, an **Avalonia UI** desktop client, and a system **CLI/TUI**.
Its differentiating goal: resolve the majority of daily requests in milliseconds at **0
cloud tokens**, and only escalate complex tasks to a (local or remote) LLM under strict
budget and security controls.

Expected capabilities (all designed, none implemented — see
[01-overview.md](../01-overview.md) §1):

1. 4-tier intent router (regex → ONNX embeddings → light LLM → heavy LLM + sandbox).
2. Token-budget governor with auto-pause and instant `/stop` kill-switch.
3. Host safety guards (no raw shell, Anti-Wipe, Volume Clamp, NSFW filter, media whitelist).
4. Decentralized SQLite-WAL storage with per-subsystem DBs and vault-backed keys.
5. Isolated `CyberPipe.Brainstem` worker for Whisper.net STT and untrusted document parsing.
6. Netrunner ETL sub-assistant: signed script packages as Mini-n8n DAG workflows with HITL.
7. Personal/dev/business skill catalog (≥16 skills).

The full functional requirement list is in [01-overview.md](../01-overview.md) §3
(`REQ-001`…`REQ-020`).

## 1.2 Stakeholders and their concerns

See [01-overview.md](../01-overview.md) §2. Stakeholders are inferred from the plans; none
were interviewed.

Primary concerns that shaped the architecture:

- **Owner-user**: fast routine help, predictable cost, host never damaged, no loud/18+
  content from chat, recoverable after crash.
- **Host OS**: bounded resources, filesystem isolation of `data/`, no rogue child processes.
- **Cloud LLM providers**: receive only sanitized prompts; never raw secrets; budget/rate
  compliance.

## 1.3 Functional requirements

Requirement catalog: [01-overview.md](../01-overview.md) §3.

Distinguishing **explicit** (directly stated) from **inferred** (reasoned from plan text):

- **Explicit**: `REQ-001..004`, `REQ-006..015`, `REQ-017..019`.
- **Inferred**: `REQ-005` (numeric SLO values are explicit but measurement methodology is
  not), `REQ-016` (transport switch is explicit; idempotency is inferred), `REQ-020`.

## 1.4 Business and technical goals

See [01-overview.md](../01-overview.md) §4.

## 1.5 Non-goals

See [01-overview.md](../01-overview.md) §5.

## 1.6 Scope boundaries and external systems

See [01-overview.md](../01-overview.md) §6.

## 1.7 Important assumptions and unknowns

See [01-overview.md](../01-overview.md) §8 and
[06-validation/assumptions-and-open-questions.md](../../06-validation/assumptions-and-open-questions.md).

## 1.8 Architectural drivers

See [01-overview.md](../01-overview.md) §7. The dominant drivers are **latency**, **token
cost**, **host integrity**, **secret confidentiality**, **recoverability**, and a tension
between **simplicity** and the full proposed stack.

## 1.9 Hand-off to the rest of arc42

- Constraints → [§2](02-architecture-constraints.md).
- Context → [§3](03-context-and-scope.md).
- Strategy → [§4](04-solution-strategy.md).
- Building blocks → [§5](05-building-block-view.md).
- Runtime → [§6](06-runtime-view.md).
- Deployment → [§7](07-deployment-view.md).
- Cross-cutting → [§8](08-crosscutting-concepts.md).
- Decisions → [§9](09-architecture-decisions.md) and [05-adr/](../../05-adr/).
- Quality → [§10](10-quality-requirements.md) and [03-atam/](../../03-atam/).
- Risks → [§11](11-risks-and-technical-debt.md) and [03-atam/risk-register.md](../../03-atam/risk-register.md).
- Glossary → [§12](12-glossary.md).
