# arc42 §10 — Quality Requirements

Quality targets are measurable scenarios, not adjectives. The full set —
with stimulus, environment, response criteria, and verification — lives in
[`03-atam/quality-attribute-scenarios.md`](../03-atam/quality-attribute-scenarios.md);
analysis of conflicts in [`03-atam/tradeoff-analysis.md`](../03-atam/tradeoff-analysis.md).

## Quality tree (priority order)

1. **Security / safety** — no chat-driven wipe, secret egress, sandbox escape, or
   privilege bypass; fail-closed everywhere. (`QAS-SEC-*`)
2. **Reliability / recoverability** — crash-safe checkpoints, resume-or-mark policy,
   quarantine-not-cascade. (`QAS-REL-*`)
3. **Performance / latency** — local p95 budgets (Tier 0 < 1 ms, Tier 1 5–15 ms,
   CLI round-trip < 25 ms); Telegram path best-effort. (`QAS-PERF-*`)
4. **Data durability / integrity** — WAL atomicity, backups that restore, per-DB keys
   with rotation story. (`QAS-DUR-*`)
5. **Modifiability / testability** — Contracts-only UI/Brainstem, ArchUnitNET DAG,
   negative isolation tests. (`QAS-MOD-*`)
6. **Operational simplicity** — zero-dependency default start (<1 s goal), one-command
   doctor/setup, side-by-side updates. (`QAS-OPS-*`)
7. **Compatibility / portability** — Windows-first explicitly; cross-OS abstraction
   behind `CyberPipe.Contracts` but equivalence unproven. (`QAS-PORT-*`)

Numeric targets taken from plans are **proposed until benchmarked** on reference
hardware; where plans gave no number, the scenario is marked *open target* rather than
invented.
