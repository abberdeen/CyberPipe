# 01 — System Overview

## 1. Purpose and capabilities

CyberPipe is a planned local-first intelligent assistant and agent orchestrator for a
single user's PC or home server. Expected capabilities (all **planned**, none
implemented — see §6):

- Instant routine control from Telegram / desktop UI / CLI at 0 cloud tokens
  (media, volume, timers, notes, expenses, `/repos`, `/status`) via a 4-tier intent
  router (deterministic Regex → local ONNX semantic router → light model → heavy agent).
- Token-budget governance with auto-pause, per-minute burn-rate breaker, daily USD cap,
  and an instant priority kill-switch.
- Host safety: no raw shell from chat, anti-wipe guards, volume clamp (60% day /
  25% night), 18+/NSFW filtering, trusted-media-provider whitelist.
- Zero-trust execution of generated code in real sandboxes (OS sandbox / Wasmtime /
  Docker), hard tool-stripping on tainted context, AST screening as defense-in-depth.
- Local voice (Whisper.net), hybrid long-term memory (SQLite FTS5 + SIMD vectors),
  proactive scheduling (Hangfire), 16+ personal/dev/business skills, and a Netrunner
  deterministic ETL / mini-n8n DAG workflow engine with human-in-the-loop approval.

Source: [`../plans/README.md`](../plans/README.md), `01`–`16` plan documents.

## 2. Stakeholders and concerns (as far as the plans reveal)

| Stakeholder | Concerns |
| :--- | :--- |
| Owner (single user, `Owner` role) | Latency, token cost, host safety, privacy of notes/secrets, recovery after crash |
| Guest / family (`Guest` role) | Restricted safe control (media, timers), no access to Tier 3, budgets, secrets |
| Future maintainer / reviewer | Decision traceability, test gates, migration/backup procedures |
| External model/API providers | Correct, minimal, sanitized egress; key handling |

No multi-tenant operator, app-store reviewer, or compliance auditor appears in the plans —
an explicit non-goal boundary (see §4).

## 3. Functional scope (derived)

- `REQ-001` Intent routing Tier 0–3 with per-tier Local/RemoteCloud model assignment.
- `REQ-002` Telegram gateway: text/voice/files, whitelist auth, pause/stop buttons.
- `REQ-003` Avalonia desktop UI + system CLI/TUI over local IPC (gRPC/Named Pipes).
- `REQ-004` Budget governor: soft/hard task limits, per-minute breaker, daily cap, checkpoints.
- `REQ-005` Host & media safety guards; Owner/Guest roles.
- `REQ-006` Sandboxed execution (Brainstem worker, Wasmtime/Docker/OS sandbox) + telemetry digest.
- `REQ-007` SQLite WAL storage: master registry + isolated per-subsystem DBs, secrets vault.
- `REQ-008` Skills catalog (personal/dev/business), proactive scheduler, Netrunner DAG engine.

Detail and component mapping: [`02-arc42/05-building-block-view.md`](02-arc42/05-building-block-view.md).

## 4. Non-goals (explicit)

- No multi-user / multi-tenant service; single-owner desktop product.
- No mandatory external broker or server DB for MVP (RabbitMQ is enterprise-opt-in).
- No browser automation from chat links; no arbitrary-URL opening on the host.
- Linux/macOS parity deferred until after the first working Windows version
  ([`05-adr/0004-target-os.md`](05-adr/0004-target-os.md)).

## 5. Scope boundaries and external systems

In scope: on-host daemon, UI, CLI, Brainstem worker, local DBs, local models via
Ollama/LM Studio. External: Telegram Bot API, cloud LLM APIs, Ollama/LM Studio server,
media provider APIs, passive OSINT APIs, RabbitMQ (optional), OS platform facilities
(DPAPI/LibSecret/Keychain, AppContainer/bwrap/Seatbelt). Diagram:
[`02-arc42/03-context-and-scope.md`](02-arc42/03-context-and-scope.md).

## 6. Repository state — actually verified evidence

Inspection performed 2026-10-17 (`Get-ChildItem`, file reads, `dotnet --list-sdks`;
outbound web unavailable):

| Artifact | Finding |
| :--- | :--- |
| `CyberPipe.slnx` | Contains only `<Solution/>` — empty solution |
| Source code | **Zero** `.cs` / `.csproj` files in the repository |
| `docs/plans/` | 19 files: `README.md`, `01`–`17` (`17-adr-mvp.md`), `muse-wonder-audit.md` — all read in full |
| `.agents/skills/netrunner-script-package/SKILL.md` | Exists (referenced package standard); content not re-audited here |
| `.agents/skills/rabbitmq-windows/SKILL.md` | Exists (cited by plans for RabbitMQ supervision) |
| `docs/arch/` | Did not exist; created by this task |
| .NET SDK | `10.0.402` installed (plus 6.0.417, 8.0.417) — target framework plausibly available |
| `git`/history state | Not inspected (sandbox `dubious-ownership`; same limitation as the audit) |

Consequence: every behavior, latency number, and guarantee in this baseline has status
**proposed / vendor-documented / unverified hypothesis** — never *verified fact*.
The audit's `NOT READY` verdict (`muse-wonder-audit.md`, §M) is therefore **confirmed and
inherited**: implementation may begin only with Phase-0 ADRs, DB registry, and PoCs.

## 7. Relationship to `docs/plans/`

Plans are the primary source of intent and are treated as input, not as correct
decisions. Known plan-level issues already resolved inside the plans (canonical answers
L1–L6, fixed cache key, local-vs-end-to-end SLO split, "designed vs implemented"
wording) are taken at their corrected values and traced to source. Residual concerns —
unproven encryption, unproven cross-OS isolation equivalence, unverifiable package
facts (no internet) — are carried as risks and assumptions, not silently adopted.
`docs/plans/` itself was not modified, per task constraints.
