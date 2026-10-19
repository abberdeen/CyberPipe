# ADR-0006 — Latency SLOs are local-only budgets; Telegram path is best-effort

- **Status:** Proposed
- **Date:** 2026-10-17
- **Sources:** `docs/plans/02`, `04`, `15` §2, `17-adr-mvp.md`; audit AUD-06, L6

## Context and constraints

Millisecond budgets (Tier 0 < 1 ms, Tier 1 5–15 ms, fast-path < 20 ms, `/stop` <
50 ms, CLI p95 < 25 ms) cannot survive a Telegram network round-trip, so as-written
they were untestable DoDs (`C-013` fixes the framing).

## Decision drivers

1. Testable Sprint DoDs. 2. Honest user expectations. 3. Comparable benchmarks
(percentiles, reference HW) instead of one-off minima.

## Alternatives considered

| Alternative | Fit | Fatal flaw |
| :--- | :--- | :--- |
| End-to-end budgets incl. Telegram | User-meaningful | Uncontrollable network makes them untestable flakiness |
| Local-only budgets + best-effort end-to-end (chosen) | Testable + honest | Requires telemetry to characterize the real path later |
| No numeric SLOs | Avoids false precision | Loses the performance culture the product needs |

## Evidence and comparative analysis

- Verified: plans converge post-audit on the split; reference HW + p95/p99 named.
- Unverified: every number (no benchmarks exist); reference-HW finalization pending.

## Decision and rationale

All ms budgets measure **in-host processing** on reference hardware (4+ cores, NVMe,
warm process), reported as **p95/p99** via BenchmarkDotNet. The Telegram path is
measured separately as best-effort until statistics justify a percentile target.

## Consequences

+ CI-testable DoDs; + honest UX copy. − Two-number story to communicate; − end-to-end
feel remains unpromised until data exists.

## Risks and mitigations

Benchmarks reveal budgets unreachable → retune numbers (not the method) via ADR amendment.

## Validation and acceptance criteria

`TEST-PERF-01…05` implemented on reference HW; results published; any failing budget
renegotiated before Sprint DoD sign-off. Green → Accepted.

## Revisit triggers

Reference HW changes; accumulated Telegram stats support an end-to-end percentile;
local-model hardware shifts Tier-1 timing.

## Related ADRs and source documents

`03-atam/quality-attribute-scenarios.md` (QAS-PERF-*); `02-arc42/06-runtime-view.md`.
