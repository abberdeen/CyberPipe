# ADR-0005 — DevTools run only via `IDevToolProcessRunner` inside sandbox/isolation

- **Status:** Proposed
- **Date:** 2026-10-17
- **Sources:** `docs/plans/03`, `06`, `17-adr-mvp.md`; audit AUD-08, L5

## Context and constraints

`dotnet build/test` executes arbitrary project code; `git` executes hooks and external
helpers. Name-based allow-listing of binaries is therefore **not** a safety boundary —
it is unsandboxed code execution. Skills must never reach these tools except through a
confined runner (`C-004` scope extension).

## Decision drivers

1. No unsandboxed code execution from any assistant path. 2. Keep legitimate
dev workflows (`/repos`, build/test watchdogs) functional. 3. Static enforceability.

## Alternatives considered

| Alternative | Fit | Fatal flaw |
| :--- | :--- | :--- |
| Binary-name whitelist on the host | Convenient | Arbitrary code execution via build targets/hooks — rejected |
| Fixed subcommands + confined runner (chosen) | Matches drivers | Slower cold runs; some exotic workflows need explicit approval |
| Full ban on DevTools | Safest | Kills core dev-assistant value |

## Evidence and comparative analysis

- Verified: plan convergence (`06` rule 4, `17` L5); no code to audit yet.
- Vendor-documented: git hooks / MSBuild targets execute code by design (standard
  tool behavior, not a vulnerability finding).
- Unverified: sandbox I/O + perf shape for build workloads (needs PoC).

## Decision and rationale

`dotnet`/`git` run **only** through `IDevToolProcessRunner` (fixed subcommand list,
fixed repo roots) **inside** sandbox/isolation. Anything else requires explicit Owner
approval. `Skills.Host` keeps its static ban on `Process`/CliWrap.

## Consequences

+ Closes the AUD-08 hole by construction. − Build-in-sandbox ergonomics/perf to prove;
− approval friction for unusual commands (intended).

## Risks and mitigations

RISK-004; malicious-build-target fixture test; runner audit logging.

## Validation and acceptance criteria

Weaponized build target + hook fixtures execute with zero host-filesystem effect;
ArchUnitNET ban green. Green → Accepted.

## Revisit triggers

Sandboxed builds unusably slow; legitimate workflow uncapturable by the whitelist.

## Related ADRs and source documents

THREAT-010; `02-arc42/05-building-block-view.md`; `04-threat-model/mitigations-and-verification.md`.
