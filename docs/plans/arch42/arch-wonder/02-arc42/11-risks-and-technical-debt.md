# arc42 §11 — Risks and Technical Debt

Ranked register with severity × likelihood, owners, and mitigations:
[`03-atam/risk-register.md`](../03-atam/risk-register.md). Headlines only below —
severity **Critical/High**, all **unmitigated until Phase-0 PoCs pass**:

- `RISK-001` SQLite encryption unproven — at-rest confidentiality is currently a claim,
  not a control. Blocks `C-007`/`C-008` credibility.
- `RISK-002` Cross-OS isolation equivalence unproven — one abstraction, three
  different kernels; Linux/macOS behavior is hypothesis.
- `RISK-003` MassTransit SQLite-transport + `Hangfire.Storage.SQLite` facts unverified
  (versions/maintenance not pinned in this task; compat unknown until spiked).
- `RISK-004` DevTools-as-code-execution — `dotnet`/`git` treated as safe commands
  would be unsandboxed code execution; contained by design rule, awaiting tests.
- `RISK-005` Sanitizer bypass by novel secret formats — redaction is pattern-based;
  misses are silent egress.
- `RISK-006` ONNX/router fallback collapse — if local models fail, all traffic becomes
  billable with no designed degraded mode.
- `RISK-007` Single-writer SQLite + multi-DB contention untested — write-behind,
  Hangfire, bus, and skills share files with one writer each.

## Accepted technical debt (intentional, tracked)

- `DEBT-001` InMemory bus loses messages on restart — accepted for MVP default;
  durability is opt-in (SQLite) per [ADR-0001](../05-adr/0001-message-bus.md).
- `DEBT-002` No in-memory secret re-encryption inside the daemon process — accepted;
  boundary controls carry the weight (see §8).
- `DEBT-003` Hangfire + Netrunner checkpoints overlap in scheduling duty — accepted
  duplication pending post-MVP simplification review (audit §I).
- `DEBT-004` Curated model catalog (15 local / 8 cloud) will rot — accepted with a
  revisit trigger: refresh catalog + prices before any release that ships defaults.
