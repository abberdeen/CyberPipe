# arc42 §2 — Architecture Constraints

Part of the [arc42 documentation](01-introduction-and-goals.md).

---

## 2.1 Hard constraints (technical)

| ID | Constraint | Source / Evidence | Status |
| :--- | :--- | :--- | :--- |
| CON-001 | Target framework: **.NET 10**. | [`01-principles.md`](../../plans/01-principles.md) §1 | Designed; .NET 10 GA status to confirm in Phase 0. |
| CON-002 | MVP target OS: **Windows 10/11** only; Linux/macOS after first working version. | [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) L4 | Accepted in plan. |
| CON-003 | Daemon runs in the **interactive user session** (`Session >= 1`), not Windows Service `Session 0`, to retain audio/clipboard/OCR access without admin. | [`15-cli-and-execution-modes.md`](../../plans/15-cli-and-execution-modes.md) §11 | Designed; no admin elevation required. |
| CON-004 | Single-user, single-host deployment. No multi-tenant, no cloud-hosted daemon. | Inferred from [`01`](../../plans/01-principles.md), [`15`](../../plans/15-cli-and-execution-modes.md) | Inferred. |
| CON-005 | Storage: **SQLite in WAL mode**, no external RDBMS. | [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md) §3 | Accepted (ADR-002). |
| CON-006 | Bus: **MassTransit**, transport `InMemory` by default; `SQLite WAL` optional; `RabbitMQ` enterprise-only. | [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) ADR-001 | Accepted in plan; **PoC pending**. |
| CON-007 | UI: **Avalonia 11+**; communicates with daemon only via `CyberPipe.Contracts`. | [`07-ui-isolation-and-arch-tests.md`](../../plans/07-ui-isolation-and-arch-tests.md) §2; [`06`](../../plans/06-solution-structure.md) DAG rule 3 | Designed; enforced by `ArchUnitNET` test `UI_Must_Depend_Only_On_Contracts`. |
| CON-008 | LLM integration via **`Microsoft.Extensions.AI` (`IChatClient`)**; local models via `OllamaSharp` to `Ollama`/`LM Studio` on `localhost:11434`. | [`03-tech-stack-libraries.md`](../../plans/03-tech-stack-libraries.md) AI pipeline row | Designed. |
| CON-009 | Project DAG: strict acyclic; `CyberPipe.Contracts` has 0 project dependencies; `CyberPipe.Security.Telemetry` depends only on `Contracts`. | [`06-solution-structure.md`](../../plans/06-solution-structure.md) DAG rules 1–2 | Designed; enforced by `ArchUnitNET`. |
| CON-010 | IPC: **gRPC over Named Pipes** (Windows) / Unix Domain Sockets (Linux/macOS); max frame `4 MB`. | [`15-cli-and-execution-modes.md`](../../plans/15-cli-and-execution-modes.md) §4 | Designed; **protocol not fully specified** (`AUD-09`). |
| CON-011 | Sandbox isolation of Tier-3 generated code: mandatory `Wasmtime` / `Docker` / OS sandbox (`AppContainer`/`bwrap`/`Seatbelt`). | [`05-security-agent.md`](../../plans/05-security-agent.md) §1.B; [`01`](../../plans/01-principles.md) §5 | Designed; **equivalence unverified** (`AUD-13`). |
| CON-012 | `CyberPipe.Skills.Host` must not call `System.Diagnostics.Process` or `CliWrap` directly; uses `IDevToolProcessRunner`. | [`06`](../../plans/06-solution-structure.md) DAG rule 4; [`07`](../../plans/07-ui-isolation-and-arch-tests.md) `HostSkills_Must_Never_Invoke_Arbitrary_Processes_Or_Shell` | Designed; enforced by `ArchUnitNET`. |
| CON-013 | All `Channel` instances bounded; `Channel.CreateUnbounded` forbidden. | [`14-fault-isolation-and-resilience.md`](../../plans/14-fault-isolation-and-resilience.md) §3; [`07`](../../plans/07-ui-isolation-and-arch-tests.md) `No_Unbounded_Channels_Allowed` | Designed; enforced by `ArchUnitNET`. |
| CON-014 | `BackgroundServiceExceptionBehavior.Ignore`; no blocking I/O in DI constructors; all workers inherit `IsolatedWorkerBase`. | [`14`](../../plans/14-fault-isolation-and-resilience.md) §1–2; [`07`](../../plans/07-ui-isolation-and-arch-tests.md) `All_Background_Workers_Must_Inherit_From_IsolatedWorkerBase` | Designed; enforced by `ArchUnitNET`. |
| CON-015 | Telegram gateway must not call agents/skills directly; must route via AuthGuard + Router + Guards. | [`07`](../../plans/07-ui-isolation-and-arch-tests.md) `TelegramGateway_Must_Not_Call_Agents_Or_Skills_Directly_Bypassing_Router_And_Guards` | Designed; enforced by `ArchUnitNET`. |

## 2.2 Organizational / process constraints

| ID | Constraint | Source |
| :--- | :--- | :--- |
| CON-016 | Phase 0 (ADR + DB registry + encryption PoC + Brainstem isolation PoC + cache-key fix + SLO methodology) must close before Sprint 1 starts. | [`08-implementation-roadmap.md`](../../plans/08-implementation-roadmap.md) Phase 0 |
| CON-017 | No destructive operations, no unnecessary dependency installs, no unrelated code changes during architecture work. | [`docs/arch42.md`](../../arch42.md) §13 |
| CON-018 | Plans and existing application code must not be modified by this task. | [`docs/arch42.md`](../../arch42.md) §2, §16 |

## 2.3 Implicit assumptions (need validation)

| ID | Assumption | Risk if wrong | Tracking |
| :--- | :--- | :--- | :--- |
| ASSUMPTION-001 | SQLCipher provider encrypts SQLite pages; wrong key fails to open. | All "encrypted DB" claims collapse; `data/subsystems/*.db` readable on direct file access. | `AUD-04`, [tech matrix](../../06-validation/technology-compatibility-matrix.md) |
| ASSUMPTION-002 | OS sandbox denies Brainstem read of `data/`. | Brainstem compromise → full secrets/DB exfiltration. | `AUD-13` |
| ASSUMPTION-003 | PID/Nonce IPC handshake resists squatting and replay. | Local privilege escalation / command forgery. | `AUD-09` |
| ASSUMPTION-004 | `dotnet`/`git` fixed-subcommand whitelist is safe. | Build/test executes arbitrary project code; git hooks run. | `AUD-08` |
| ASSUMPTION-005 | Whisper.net runs under AppContainer on user GPU; else RestrictedToken. | Voice STT pool design changes; GPU support may need rework. | `AUD-13` |
| ASSUMPTION-006 | Netrunner file SHA-256 + sandbox routing = execution isolation. | Without sandbox PoC, scripts run with host rights. | `AUD-07` |
| ASSUMPTION-007 | MassTransit consumers can be made idempotent via `MessageDeduplicationId`. | Duplicates from SQLite/RabbitMQ transports corrupt state. | `AUD-02` |
| ASSUMPTION-008 | The owner-user's hardware has ≥4 CPU cores and SSD NVMe (reference SLO hardware). | SLO numbers unattainable; targets need rescaling. | `AUD-06` |
| ASSUMPTION-009 | Whisper.net, PdfPig, OpenXml can run inside an AppContainer/RestrictedToken process and still access only `./models/` and the per-instance `TEMP`. | Worker cannot load model files → STT/parse unavailable. | `AUD-13` |

## 2.4 Marked inapplicable constraints

- **Multi-region deployment / geographic DR**: not applicable to a single-host home product.
  Backup/restore is local-only (`backups/cyberpipe.v{N}.bak`).
- **Kubernetes / container orchestration**: not applicable; daemon is a single process per
  host. Docker appears only as an optional sandbox provider for Tier-3 code and (optionally)
  RabbitMQ supervision.
