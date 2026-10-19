# arc42 §1 — Introduction and Goals

## Purpose

CyberPipe (planned) is a local-first intelligent assistant and agent orchestrator that
executes 70–85% of routine requests in milliseconds at zero cloud-token cost, while
confining expensive/heavy work (cloud LLM, generated code) behind budget governance and
real OS/process isolation. Full capability scope: [01-overview.md](../01-overview.md).

## Stakeholders and goals

| Stakeholder | Goal | Priority |
| :--- | :--- | :--- |
| Owner | Routine tasks answered in ms at 0 tokens (`G-01`) | High |
| Owner | Host can never be wiped, blasted, or abused from chat (`G-02`) | Highest |
| Owner | Token spend is bounded, pausable, instantly stoppable (`G-03`) | High |
| Owner | Notes, secrets, finances stay private on local disk (`G-04`) | High |
| Owner | Work survives crash/restart without redo or double-send (`G-05`) | Medium |
| Guest/family | Safe restricted control, no access to Tier 3/budgets/secrets (`G-06`) | Medium |
| Maintainer | Decisions traceable, architecture statically enforced in CI (`G-07`) | Medium |

## Architectural drivers

1. **Zero-token fast path** shapes the router, cache, and skill design.
2. **Host-as-attack-surface** (remote control via Telegram) shapes guards, sandboxing,
   IPC auth, and the no-raw-shell rule.
3. **Single-user local deployment** permits SQLite, InMemory bus, and OS-native vaults —
   and forbids assuming server infrastructure.
4. **Pre-implementation status** (verified: no code exists) makes Phase-0 proof
   (encryption PoC, isolation PoC, DB registry) the gating driver; see
   [`05-adr/0001-message-bus.md`](../05-adr/0001-message-bus.md) and
   [11-risks-and-technical-debt.md](11-risks-and-technical-debt.md).

## Explicit non-goals

Single-owner product (no multi-tenancy); no mandatory broker/server DB for MVP;
no browser automation from chat; Linux/macOS parity deferred. Source and rationale:
[`05-adr/0004-target-os.md`](../05-adr/0004-target-os.md), [01-overview.md §4](../01-overview.md).
