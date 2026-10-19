# ADR-0004 — MVP targets Windows 10/11; Linux/macOS after the first working version

- **Status:** Proposed
- **Date:** 2026-10-17
- **Sources:** `docs/plans/09` §7, `15`, `17-adr-mvp.md`; audit AUD-13, L4

## Context and constraints

Isolation designs differ per OS (AppContainer/restricted-token vs bwrap vs Seatbelt)
with different guarantees, APIs, and failure modes (`C-002`). Proving all three before
any working version multiplies PoC/installer/CI work and delays the feedback the
audit demands first (Phase-0 proofs on one OS).

## Decision drivers

1. Prove isolation somewhere real, fast. 2. Bound installer/CI matrix for MVP.
3. Keep portability honest (abstractions, no lock-in) without claiming parity.

## Alternatives considered

| Alternative | Fit | Fatal flaw |
| :--- | :--- | :--- |
| All three OSes from day one | True parity | Triples PoC + CI + installer cost pre-first-feedback |
| Windows-first, contracts abstract the rest | Matches drivers | Linux/macOS behavior stays hypothesis (tracked, not hidden) |
| Windows-only forever | Simplest | Abandons stated cross-platform product without a decision |

## Evidence and comparative analysis

- Verified: .NET 10 SDK present on analyst machine; plans name Windows-first.
- Unverified: exact supported Windows builds/SKUs, .NET 10 release status at
  implementation start, per-OS PoC results (all pending).

## Decision and rationale

MVP ships on **Windows 10/11**. `CyberPipe.Contracts` keeps platform seams abstract
(`IBrainstemIsolationPlatform`, vault, audio, OCR, installer) so Linux/macOS follow
without redesign — but their isolation equivalence is an open hypothesis until each OS
passes the same negative suite.

## Consequences

+ Focused PoCs, shippable MVP. − Deferred parity; − risk of Windows-isms leaking past
contracts (ArchUnitNET + per-OS CI to contain).

## Risks and mitigations

RISK-002 (per-OS PoCs, divergence log).

## Validation and acceptance criteria

Windows negative suite green (`TEST-SEC-05`); supported-build list published by
`doctor`; Linux/macOS tracked as follow-up scope, not MVP claims.

## Revisit triggers

First working Windows version ships (starts Linux port); PoC shows a design seam is
Windows-only; .NET 10 support dates shift.

## Related ADRs and source documents

`15-cli-and-execution-modes.md`; RISK-002; `06-validation/technology-compatibility-matrix.md`.
