# CyberPipe Architecture Documentation (arch-glm)

> **Status: pre-implementation (AUD-01).** The repository is docs-only: `CyberPipe.slnx`
> contains only `<Solution />` and there is no `.cs` / `.csproj` source code. Every
> architectural statement below describes a **designed** system, not a verified
> implementation, unless explicitly marked otherwise. Where source evidence exists, it
> is cited to `docs/plans/*`. See [01-overview.md](01-overview.md) and
> [06-validation/assumptions-and-open-questions.md](06-validation/assumptions-and-open-questions.md).

This directory is an evidence-based architecture baseline for **CyberPipe**. It analyzes
the project plans in [`docs/plans/`](../plans/) using four complementary methodologies and
records what is known, what is assumed, what is risky, and what must be verified before
implementation.

## What CyberPipe is (one paragraph)

CyberPipe is a fast, token-frugal personal/engineering/business assistant and autonomous-agent
orchestrator on **.NET 10**, controlled through **Telegram** and an **Avalonia UI** desktop
client. A deterministic C# orchestrator and a 4-tier intent router resolve ~70–85% of daily
requests locally in milliseconds with **0 cloud tokens**. Strict host-safety guards
(Anti-Wipe, Media/Volume Clamp, NSFW filter) protect the PC when driven from chat. A
`TokenBudgetGovernor` enforces per-task / per-minute / per-day cost ceilings with auto-pause
and an instant `/stop` kill-switch. Tier-3 agent-generated code runs only in real OS
sandboxes (`AppContainer` / `bwrap` / `Seatbelt` / `Wasmtime` / `Docker`). State lives in
decentralized SQLite-WAL databases (`cyberpipe_master.db` + `data/subsystems/*.db`) with
secrets in a platform vault (`DPAPI` / `LibSecret` / `Keychain`). An ETL sub-assistant
**Netrunner** turns repetitive AI tasks into deterministic, signed script packages executed
as Mini-n8n DAG workflows with Human-in-the-Loop checkpoints.

## Document hierarchy

| Group | Document | Purpose |
| :--- | :--- | :--- |
| **Overview** | [01-overview.md](01-overview.md) | System summary, drivers, stakeholders, status, major unresolved decisions |
| **Methodology** | [00-methodology.md](00-methodology.md) | arc42, ATAM, Threat Modeling (STRIDE), ADR/MADR — purpose and application |
| **arc42** | [02-arc42/](02-arc42/) | 12-section primary architecture description |
| | [02-arc42/01-introduction-and-goals.md](02-arc42/01-introduction-and-goals.md) | Goals, requirements, stakeholders, drivers |
| | [02-arc42/02-architecture-constraints.md](02-arc42/02-architecture-constraints.md) | Hard constraints, implicit assumptions |
| | [02-arc42/03-context-and-scope.md](02-arc42/03-context-and-scope.md) | System context, external actors |
| | [02-arc42/04-solution-strategy.md](02-arc42/04-solution-strategy.md) | Top-level strategy and principles |
| | [02-arc42/05-building-block-view.md](02-arc42/05-building-block-view.md) | Subsystems, components, DAG of projects |
| | [02-arc42/06-runtime-view.md](02-arc42/06-runtime-view.md) | Runtime scenarios, fast-path vs heavy path |
| | [02-arc42/07-deployment-view.md](02-arc42/07-deployment-view.md) | Executables, install layout, OS sessions |
| | [02-arc42/08-crosscutting-concepts.md](02-arc42/08-crosscutting-concepts.md) | Security, persistence, observability, resilience |
| | [02-arc42/09-architecture-decisions.md](02-arc42/09-architecture-decisions.md) | Summary index of decisions (links to ADRs) |
| | [02-arc42/10-quality-requirements.md](02-arc42/10-quality-requirements.md) | Quality tree and scenarios |
| | [02-arc42/11-risks-and-technical-debt.md](02-arc42/11-risks-and-technical-debt.md) | Risks, debt, sensitivity points |
| | [02-arc42/12-glossary.md](02-arc42/12-glossary.md) | Terms and abbreviations |
| **ATAM** | [03-atam/](03-atam/) | Lightweight ATAM-inspired quality analysis |
| | [03-atam/quality-attribute-scenarios.md](03-atam/quality-attribute-scenarios.md) | Measurable QAS-xxx scenarios |
| | [03-atam/tradeoff-analysis.md](03-atam/tradeoff-analysis.md) | Trade-off and sensitivity points |
| | [03-atam/risk-register.md](03-atam/risk-register.md) | Ranked risk register |
| **Threat model** | [04-threat-model/](04-threat-model/) | STRIDE-based threat model |
| | [04-threat-model/scope-assets-and-trust-boundaries.md](04-threat-model/scope-assets-and-trust-boundaries.md) | Assets, entry points, trust boundaries |
| | [04-threat-model/data-flows.md](04-threat-model/data-flows.md) | Critical data and control flows |
| | [04-threat-model/threat-register.md](04-threat-model/threat-register.md) | THREAT-xxx register |
| | [04-threat-model/mitigations-and-verification.md](04-threat-model/mitigations-and-verification.md) | Mitigations and verification |
| **ADR** | [05-adr/](05-adr/) | Architecture Decision Records (MADR) |
| | [05-adr/README.md](05-adr/README.md) | ADR index and conventions |
| | [05-adr/template.md](05-adr/template.md) | MADR template |
| | [05-adr/0001-example-decision.md](05-adr/0001-example-decision.md) | Illustrative ADR |
| **Validation** | [06-validation/](06-validation/) | Verification and review aids |
| | [06-validation/assumptions-and-open-questions.md](06-validation/assumptions-and-open-questions.md) | Open questions and assumptions |
| | [06-validation/technology-compatibility-matrix.md](06-validation/technology-compatibility-matrix.md) | Critical tech assessment |
| | [06-validation/architecture-review-checklist.md](06-validation/architecture-review-checklist.md) | Reviewer checklist |

## Methodologies used and why

| Method | Contribution to this project |
| :--- | :--- |
| **arc42** | Primary skeleton. Structures the 12 sections so an engineer can navigate from goals to risks. |
| **ATAM** (lightweight) | Surfaces competing quality attributes (latency vs. token-cost vs. security vs. simplicity) and turns vague "fast/secure" claims into measurable QAS scenarios. **No formal stakeholder workshop was held** — see [03-atam/](03-atam/). |
| **Threat Modeling (STRIDE)** | Maps the chat-driven control surface (Telegram, IPC, sandboxes, DBs, secrets) to concrete threats and required mitigations, since the product deliberately exposes host control to a chat channel. |
| **ADR (MADR)** | Records the few decisions that are expensive to reverse (bus, DB layout, encryption, OS target, sandbox) and — equally important — the decisions that **cannot yet be made** without PoC evidence. |

See [00-methodology.md](00-methodology.md) for full descriptions and authoritative references.

## Current architecture status

| Aspect | Status | Evidence |
| :--- | :--- | :--- |
| Source code | **None** | `CyberPipe.slnx` = `<Solution />`; no `*.cs` / `*.csproj` |
| Plan maturity | Conceptually detailed, internally inconsistent | [`muse-wonder-audit.md`](../plans/muse-wonder-audit.md): `NOT READY` |
| Canonical bus | MassTransit (`InMemory` default) — accepted in plan, **not PoC-verified** | [ADR-001](../plans/17-adr-mvp.md), `AUD-02` |
| Canonical DB layout | `cyberpipe_master.db` + `data/subsystems/*.db` — accepted in plan | [ADR-002](../plans/17-adr-mvp.md), `AUD-03` |
| SQLite encryption | SQLCipher provider required — **hypothesis until PoC** | [ADR-003](../plans/17-adr-mvp.md), `AUD-04` |
| OS target MVP | Windows 10/11 only | [L4](../plans/17-adr-mvp.md), `AUD-13` |
| Sandbox isolation equivalence | `AppContainer` / `bwrap` / `Seatbelt` — **hypothesis until PoC** | `AUD-13` |
| SLO measurement methodology | Local budgets vs. end-to-end Telegram path — defined but unverified | `AUD-06`, [L6](../plans/17-adr-mvp.md) |

## Major unresolved decisions

These are the open decision points that would fundamentally change the architecture if
resolved differently. Each is elaborated in [06-validation/assumptions-and-open-questions.md](06-validation/assumptions-and-open-questions.md).

1. **SQLite-at-rest encryption actually works** (`AUD-04`). Without SQLCipher PoC, "encrypted
   DB" is a non-guarantee and all threat-model mitigations around DB file confidentiality
   weaken.
2. **OS sandbox equivalence** (`AUD-13`). If `AppContainer` cannot run `Whisper.net` on GPU,
   the plan falls back to `RestrictedToken + Deny DACL`; if both fail on a target GPU, the
   voice STT pool design changes.
3. **IPC protocol is fully specified and tested** (`AUD-09`). PID/Nonce guard is asserted
   but not turned into a verifiable protocol with negative tests.
4. **`dotnet`/`git` as "dev tools"** (`AUD-08`). Building/testing a project executes
   arbitrary project code; the current whitelist-by-binary is insufficient until runs are
   routed through a sandbox.
5. **Idempotency of MassTransit consumers** is asserted but not specified per consumer —
   `InMemory` loses messages on restart, `SQLite`/`RabbitMQ` duplicate them.
6. **Netrunner script execution isolation** (`AUD-07`). Manifest SHA-256 proves file
   integrity, not execution isolation; mandatory OS-sandbox routing is designed but unverified.
7. **Master-key loss recovery** (`AUD-14`). Fail-closed behavior is stated; the restore
   procedure from `backups/cyberpipe.v{N}.bak` is not yet a tested runbook.

## How to read this documentation

- **Reviewers / approvers** — start at [01-overview.md](01-overview.md), then
  [06-validation/architecture-review-checklist.md](06-validation/architecture-review-checklist.md).
- **Implementers** — start at [02-arc42/](02-arc42/), then the relevant
  [ADR](05-adr/) and [technology compatibility matrix](06-validation/technology-compatibility-matrix.md).
- **Security reviewers** — start at [04-threat-model/](04-threat-model/) and
  [02-arc42/08-crosscutting-concepts.md](02-arc42/08-crosscutting-concepts.md).

## Stable ID conventions

| Prefix | Meaning | Defined in |
| :--- | :--- | :--- |
| `REQ-xxx` | Functional / non-functional requirement | arc42 §1, §10 |
| `QAS-xxx` | Quality attribute scenario | 03-atam |
| `RISK-xxx` | Architecture risk | 03-atam/risk-register, arc42 §11 |
| `THREAT-xxx` | Identified threat | 04-threat-model |
| `ADR-xxxx` | Architecture decision record | 05-adr |
| `TEST-xxx` | Validation activity / acceptance test | 06-validation |
| `ASSUMPTION-xxx` | Explicit assumption | 06-validation |
| `AUD-xx` | Audit finding from muse-wonder audit | [../plans/muse-wonder-audit.md](../plans/muse-wonder-audit.md) |

## Source inventory

Primary sources examined (full inventory in
[01-overview.md](01-overview.md) §"Artifacts examined"):

- All 18 documents under [`docs/plans/`](../plans/) (read in full).
- [`docs/arch42.md`](../arch42.md) — this task brief.
- [`CyberPipe.slnx`](../../CyberPipe.slnx) — confirmed empty (`<Solution />`).
- [`.gitignore`](../../.gitignore) — standard Visual Studio .NET gitignore; no project-specific ignores.
- [`.agents/skills/`](../../.agents/skills/) — `netrunner-script-package/`, `rabbitmq-windows/` (project skill packs).
- No `*.cs`, `*.csproj`, `*.fsproj`, `Directory.Packages.props`, `global.json`, `NuGet.config`, `appsettings.json`, or `dockerfile` exist in the repository.

No application code was modified. No tests were executed (there is nothing to execute).
