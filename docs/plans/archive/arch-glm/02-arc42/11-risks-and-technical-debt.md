# arc42 §11 — Risks and Technical Debt

Part of the [arc42 documentation](01-introduction-and-goals.md). The ranked register lives
in [03-atam/risk-register.md](../../03-atam/risk-register.md). This section is the
narrative plus the debt items.

---

## 11.1 Risk themes (across multiple components)

| Theme | Components affected | Why it is a theme |
| :--- | :--- | :--- |
| **Unverified PoCs** | SQLCipher, OS sandboxes, IPC protocol, dev-tool sandboxing | Four critical guarantees (DB-at-rest, host isolation, IPC auth, sandboxed build) all rest on PoCs not yet run (`AUD-04/07/08/09/13`). |
| **Plan internal contradictions** | Bus, DB names, cache key, changelog tone | Plans disagree with each other; ADR-001/002/003 partly reconcile, but `AUD-05` (cache key) and `AUD-15` (changelog tone) remain. |
| **Idempotency unspecified** | MassTransit consumers, Telegram update dedup | `InMemory` loses messages; `SQLite`/`RabbitMQ` duplicate. Without per-consumer idempotency, switching transports corrupts state. |
| **Host-control surface from chat** | Telegram gateway, skills, guards | Chat can drive volume/media/lock/getfile/code-exec; any guard bypass = host compromise. Concentrated risk. |
| **Token economy vs. quality** | Tier-1 threshold `0.86`, dynamic tool pruning | Wrong threshold → either mis-routing (too low) or unnecessary paid Tier-2 calls (too high). Tunable without calibration data. |
| **Simplicity vs. stack** | Principles vs. full stack | Principle "no enterprise bloat" coexists with MassTransit/Hangfire/Docker/Wasmtime (`AUD-10`). |
| **Recoverability untested** | Backup/restore, master-key loss, key rotation | Procedures described but no tested runbook (`AUD-12`, `AUD-14`). |

## 11.2 Top risks (full ranking in [03-atam/risk-register.md](../../03-atam/risk-register.md))

| ID | Risk | Severity | Likelihood | Status |
| :--- | :--- | :--- | :--- | :--- |
| RISK-001 | SQLCipher encryption does not work as claimed → DB files readable at rest | Critical | Medium | PoC pending (`AUD-04`) |
| RISK-002 | OS sandbox equivalence fails on a target OS → Brainstem can read `data/` or spawn shells | Critical | Medium | PoC per OS pending (`AUD-13`) |
| RISK-003 | IPC PID/Nonce protocol not specified → squatting/replay/EoP | High | Medium | Spec pending (`AUD-09`) |
| RISK-004 | `dotnet`/`git` whitelist insufficient → arbitrary code execution on host | High | High | Sandbox routing pending (`AUD-08`) |
| RISK-005 | Semantic cache key lacks `QueryHash` → cross-query answer leakage within role | High | Confirmed defect | Fix designed; `08` still shows old key (`AUD-05`) |
| RISK-006 | MassTransit consumers not idempotent → duplicate-driven state corruption when transport switches | High | Medium | Per-consumer contracts pending (`AUD-02`) |
| RISK-007 | SLOs unmeasurable as written → DoD not testable | High | Confirmed | Methodology defined; benchmarks pending (`AUD-06`) |
| RISK-008 | Netrunner manifest SHA-256 ≠ execution isolation → script runs with host rights without OS sandbox | High | Medium | Mandatory sandbox routing designed, PoC pending (`AUD-07`) |
| RISK-009 | Master-key loss with no tested restore runbook → unrecoverable data | Medium | Low | Runbook + test pending (`AUD-12`, `AUD-14`) |
| RISK-010 | Cloud LLM prompt-injection via `UntrustedExternal` reaches host-tool call | High | Medium | Hard Tool Stripping designed; negative tests pending |
| RISK-011 | `/stop` cancel does not propagate to sandbox child processes within 50 ms | Medium | Medium | Hard kill after 1 500 ms fallback; measurement pending |
| RISK-012 | Whisper.net fails under AppContainer on user GPU → STT pool redesign | Medium | Medium | Profile B fallback; `doctor` auto-test (`AUD-13`) |
| RISK-013 | Tokenizer mismatch under-counts non-OpenAI Cyrillic tokens → silent budget overrun | Medium | Medium | `1.2x` safety factor; final settle by `UsageDetails` |
| RISK-014 | Plan contradictions (bus/DB/cache) leak into implementation → rework | Medium | High | ADR-001/002/003 + cache-key fix |
| RISK-015 | Supply-chain: native runtime DLL hijack (`whisper.dll`, etc.) | Medium | Low | `worker.manifest.json` SHA-256 + `FileShare.Read` hold |

Severity scale: **Critical** = data loss / host compromise / unrecoverable; **High** =
security or correctness defect with bounded blast radius; **Medium** = rework or degraded
quality; **Low** = minor. Likelihood: **Confirmed** (defect observed), **High/Medium/Low**
(estimated). See [03-atam/risk-register.md](../../03-atam/risk-register.md) for full
definitions.

## 11.3 Technical debt (pre-implementation)

Pre-implementation debt = inconsistencies and over-scoping in the plans themselves.

| ID | Debt | Source | Resolution |
| :--- | :--- | :--- | :--- |
| TD-001 | `README.md` mentions `System.Threading.Channels` as the bus, contradicts `MassTransit` plans | `AUD-02` | Update README; ADR-001 is canonical. *(Plan file — not modified by this task.)* |
| TD-002 | `03-tech-stack-libraries.md` uses `cyberpipe.db` and `data/hangfire.db` instead of canonical `cyberpipe_master.db` / `data/subsystems/hangfire.db` | `AUD-03` | Reconcile to ADR-002 paths. |
| TD-003 | `08-implementation-roadmap.md` shows old semantic cache key `(UserRole, ExpiresAtUtc)` | `AUD-05` | Update to `(QueryHash, UserRole, DataVersion, ExpiresAtUtc)`. |
| TD-004 | `11-changelog.md` uses past tense for undesigned features | `AUD-15` | Label as "designed", not "implemented". |
| TD-005 | Three bus transports, Hangfire + own scheduler, 15-model catalog, ≥16 skills — over-scoped for MVP | `AUD-10`, audit §I | Reduce MVP to one transport (InMemory), one scheduler (Hangfire), one end-to-end scenario. |
| TD-006 | No `Directory.Packages.props` / `NuGet.config` / `global.json` exists | repo | Decide central package management before first release. |
| TD-007 | CyberPipe distribution license unspecified | plans | Decide before any public release. |
| TD-008 | No outbox/inbox specified for MassTransit SQLite transport | plans (gap) | Decide if outbox needed for ordering/durability when SQLite transport used. |

## 11.4 Sensitivity points (ATAM)

A **sensitivity point** is a parameter that, if changed, affects multiple quality attributes.

| Sensitivity | Affects | Notes |
| :--- | :--- | :--- |
| Tier-1 cosine threshold (`0.86`) | Latency (higher → more Tier-2), token cost (higher → more paid calls), correctness (lower → mis-route) | Needs calibration data; no calibration in plans. |
| `MaxOutputTokens` per step (`2 048`) | Token cost, quality, latency | Reasoning models may need more; raising risks budget overrun. |
| `MaxLlmTurnsPerTask` (`3`) | Token cost, task completeness, user friction (auto-pause) | Too low → frequent auto-pause; too high → runaway cost. |
| Brainstem Pool 1 size (`1`) | Voice STT throughput vs. RAM | Single warm Whisper process; can't parallelize voice. |
| `MaxParallelBranches` (`8`) in Netrunner | CPU saturation vs. throughput | Bounds fan-out; tunable per workflow. |
| `QuotaBoundedStream` limit (`16 MB`) | OOM safety vs. parse-ability of large docs | Larger PDFs truncated; may miss content. |
| `ToolOutputTruncator` (`1 500` chars) | Token economy vs. agent context completeness | Over-truncation loses information; agent may make wrong calls. |

## 11.5 Trade-off points (ATAM)

A **trade-off point** is where one quality attribute is sacrificed for another.

| Trade-off | Sacrifices | For |
| :--- | :--- | :--- |
| InMemory bus default | Durability (messages lost on restart) | Latency + simplicity + zero-dependency startup |
| OS sandbox for Tier-3 | Latency (cold start) + simplicity (3 OS impls) | Host integrity |
| Per-DB vault keys | Operational complexity (rotation, recovery) | Blast-radius containment |
| Bounded channels only | Throughput ceiling (backpressure) | OOM safety |
| Egress sanitizer always-on | Latency (~0.5 ms per check) + risk of false-positive redaction | Secret confidentiality |
| Hard Tool Stripping | Functionality (host tools unavailable in TaintedContext) | Prompt-injection defense |
| Tier-1 in-process ONNX | RAM (~110 MB) + in-process risk | Latency (no IPC) |
| Compiled-skill-only API | Flexibility (new skills need code) | Host integrity |

## 11.6 Risk themes affecting multiple components

See §11.1. The dominant theme is **"unverified PoCs"** — five separate guarantees
(DB-at-rest, host isolation, IPC auth, sandboxed build, OS equivalence) all rest on PoCs not
yet executed. Until Phase-0 PoCs pass, every threat-model mitigation tagged "claimed" must
be treated as not-yet-effective. This is the single most important thing an implementer must
do first, and it is the spine of the [Phase 0 roadmap](../../plans/08-implementation-roadmap.md).
