# CyberPipe Software Architecture

**Status: pre-implementation baseline (docs-only repository). No production code exists yet.**
**Last updated:** 2026-10-18. Location: `docs/arch-wonder/` (task in [`../arch42.md`](../arch42.md)).
Source of intent: [`../plans/`](../plans/README.md). This set reconciles the earlier baseline in
[`../arch/`](../arch/README.md) (2026-10-17) against the same plan evidence; `../arch/` is
preserved unchanged and `docs/arch-wonder/` is the entry point for this task.

## What this is

Evidence-based architecture documentation for **CyberPipe**: a fast, token-frugal,
local-first intelligent assistant and agent orchestrator on **.NET 10**, operated via
**Telegram** and **Avalonia UI**, with a 4-tier local intent router (0 tokens for routine
tasks), host safety guards (Anti-Wipe, Media/Volume Guard), a token budget governor with
auto-pause, and pluggable sandboxes. See [01-overview.md](01-overview.md).

This documentation does not merely restate the plans. It analyzes them, records what was
verified against the repository, and exposes risks, hidden constraints, contradictions,
and open questions before implementation makes them expensive. Unresolved items live in
[06-validation/assumptions-and-open-questions.md](06-validation/assumptions-and-open-questions.md).

Layout follows the task structure (§4): `00-methodology.md`, `01-overview.md`,
`02-arc42/` (12 sections), `03-atam/` (scenarios, trade-offs, risk register),
`04-threat-model/` (scope, data flows, threat register, mitigations),
`05-adr/` (MADR records), and `06-validation/` (assumptions, compatibility matrix,
review checklist). The prescribed `05-adr/0001-example-decision.md` slot holds the
real, evidence-supported ADR-0001 (message bus) — see [05-adr/README.md](05-adr/README.md)
for the aliasing note; it is not a hypothetical example presented as approved.

## Document map

| Document | Method | Contents |
| :--- | :--- | :--- |
| [00-methodology.md](00-methodology.md) | arc42 / ATAM / Threat Modeling / ADR | What each method contributes, how it is applied here, limitations |
| [01-overview.md](01-overview.md) | — | System purpose, scope, stakeholders, evidence inventory, status |
| [02-arc42/](02-arc42/09-architecture-decisions.md) | arc42 | 12-section architecture description (goals → glossary) |
| [03-atam/](03-atam/risk-register.md) | ATAM (lightweight) | Measurable quality scenarios, trade-offs, ranked risks |
| [04-threat-model/](04-threat-model/threat-register.md) | Threat modeling + STRIDE | Assets, trust boundaries, threats, mitigations, verification |
| [05-adr/](05-adr/README.md) | ADR (MADR) | Significant decisions with context, alternatives, consequences |
| [06-validation/](06-validation/architecture-review-checklist.md) | — | Assumptions, technology compatibility matrix, review checklist |

## Reading paths

- **New to the project:** this file → `01-overview.md` → `02-arc42/01-...` → `02-arc42/04-...`.
- **Reviewing risk:** `03-atam/risk-register.md` → `02-arc42/11-...` → `06-validation/assumptions-and-open-questions.md`.
- **Reviewing security:** `04-threat-model/scope-assets-and-trust-boundaries.md` → `threat-register.md` → `mitigations-and-verification.md`.
- **Challenging a decision:** `05-adr/README.md` → the relevant ADR → linked evidence.

## Methods in one paragraph each

- **arc42** is the primary documentation structure (twelve sections). It contributes
  completeness: goals, constraints, context, strategy, building blocks, runtime,
  deployment, concepts, decisions, quality, risks, glossary. Details in
  [00-methodology.md](00-methodology.md).
- **ATAM (lightweight, no stakeholder workshop was held)** contributes evaluation:
  measurable quality-attribute scenarios, sensitivity and trade-off points, ranked risks.
  See [03-atam/quality-attribute-scenarios.md](03-atam/quality-attribute-scenarios.md).
- **Threat modeling (+STRIDE)** contributes security structure: assets, trust boundaries,
  data flows, threats with severity, mitigations, residual risk. See
  [04-threat-model/](04-threat-model/threat-register.md).
- **ADR (MADR format)** contributes decision traceability: context, alternatives,
  rationale, consequences, revisit triggers. See [05-adr/](05-adr/README.md).

## Architecture status and major unresolved decisions

- **Repository state (verified 2026-10-18):** docs-only. `CyberPipe.slnx` contains only
  `<Solution/>`; zero `.cs` / `.csproj` files. No architecture claim in this baseline is
  implementation-confirmed. Detail: [01-overview.md](01-overview.md).
- **Canonical MVP answers exist in plans** for the audit questions L1–L6 (bus, DB
  registry, SQLCipher hypothesis, Windows-first OS, sandboxed DevTools, local-only SLOs).
  They are recorded here as **Proposed** (project-endorsed, pending Phase-0 proof), not
  Accepted. See [05-adr/README.md](05-adr/README.md).
- **Blocking unknowns:** SQLite encryption provider unproven until PoC
  ([RISK-001](03-atam/risk-register.md)); Brainstem OS-isolation equivalence across
  Windows/Linux/macOS unproven ([RISK-002](03-atam/risk-register.md)); `MassTransit`
  SQLite-transport package and `Hangfire.Storage.SQLite` package facts remain
  *Unverified* (methodology links were live-checked 2026-10-18; NuGet versions were
  not pinned in this task) — see
  [06-validation/technology-compatibility-matrix.md](06-validation/technology-compatibility-matrix.md).
- **Review gate:** Phase 0 (ADRs + DB registry + encryption/isolation PoCs) must pass
  before Sprint 1 work starts, per [`../plans/08-implementation-roadmap.md`](../plans/08-implementation-roadmap.md).

## Traceability IDs

`REQ-xxx` requirements · `QAS-xxx` quality scenarios · `RISK-xxx` risks ·
`THREAT-xxx` threats · `ADR-xxxx` decisions · `TEST-xxx` validation activities ·
`ASSUMPTION-xxx` assumptions. Defined where they are introduced and linked across documents.
