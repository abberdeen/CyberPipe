# 00. Methodology

This document explains the four methodologies used in `docs/arch-glm/`, what each
contributes to CyberPipe specifically, expected outputs, limitations, and authoritative
references. Links were checked for relevance; where a vendor moves a page, the canonical
root domain is given as a fallback.

---

## 1. arc42 — primary structure

**What it is.** arc42 is a pragmatic template for software architecture documentation. It
organizes architecture information into twelve sections: introduction & goals, constraints,
context, solution strategy, building blocks, runtime view, deployment, cross-cutting
concepts, decisions, quality requirements, risks & technical debt, and glossary.

**Why used here.** CyberPipe's plans are spread across 18 free-form Markdown files with
known cross-document contradictions (`AUD-02`, `AUD-03`, `AUD-05`). arc42 provides a single
navigable skeleton that forces each architectural concern into one canonical place; other
documents then link rather than duplicate.

**How applied.** `docs/arch-glm/02-arc42/` contains the twelve sections, populated to the
depth justified by the available plan evidence. Where a section is genuinely inapplicable
to a pre-implementation local single-user product (e.g. multi-region deployment), it is
marked and the exclusion is explained rather than silently omitted.

**Expected outputs.** A reviewer can answer "what is CyberPipe, what are its constraints,
how is it structured at runtime, how is it deployed, and what are its main risks?" by
reading [02-arc42/](02-arc42/) alone.

**Limitations.** arc42 is a structure, not an analysis method. It does not by itself surface
trade-offs or threats; ATAM and threat modeling are paired with it for that. It also does
not validate the underlying plan claims — those are tagged with evidence/verification status
throughout.

**References.**
- Overview: <https://arc42.org/overview/>
- Documentation: <https://docs.arc42.org/>
- Method: <https://arc42.org/method/>
- Practical examples: <https://examples.arc42.org/sections/>

---

## 2. ATAM — Architecture Tradeoff Analysis Method (lightweight)

**What it is.** ATAM is an architecture evaluation method developed by the SEI at Carnegie
Mellon University. It evaluates how architectural decisions influence competing quality
attributes (performance, security, availability, reliability, modifiability) by constructing
measurable **quality attribute scenarios** and identifying **sensitivity points**,
**trade-off points**, and **risks**.

**Why used here.** CyberPipe's design is dominated by competing quality attributes:
- **Latency** (Tier 0 `< 1 ms`, `/stop < 50 ms`) vs. **token cost** (0 tokens on the fast
  path) vs. **security** (no raw shell from chat) vs. **simplicity** ("no enterprise bloat"
  vs. MassTransit/Hangfire/Docker/Wasmtime all present).
- The muse-wonder audit shows the SLOs are unmeasurable as written (`AUD-06`). ATAM forces
  these into measurable scenarios with explicit response measures, or marks them as proposed
  targets / open questions.

**How applied.** A **lightweight, ATAM-inspired** assessment was performed by the document
author, not a facilitated stakeholder workshop. No stakeholders were interviewed; the
"stakeholders" are inferred from the plans (owner-user, household guests, the host OS, and
external cloud LLM providers). Quality scenarios are written explicitly as **proposed
targets** unless a numeric value is directly quoted from a plan.

**Expected outputs.** `QAS-xxx` scenarios with stimulus, response, and measurable criterion;
a trade-off analysis identifying where one quality is sacrificed for another; a ranked
risk register (`RISK-xxx`).

**Limitations.**
- Not a formal ATAM. No stakeholder voting, no scenarios co-authored with a product owner.
- Numeric targets (e.g. `DailyBudgetUsdCap = 0.50`) are taken from the plan but their
  real-world fitness for the (unknown) user is **not validated**.
- A real ATAM would expose non-functional requirements we cannot infer from docs; those are
  recorded as open questions in
  [06-validation/assumptions-and-open-questions.md](06-validation/assumptions-and-open-questions.md).

**References.**
- SEI ATAM overview: <https://www.sei.cmu.edu/library/the-architecture-tradeoff-analysis-method/>
- ATAM method for architecture evaluation: <https://www.sei.cmu.edu/library/atam-method-for-architecture-evaluation/>

---

## 3. Threat Modeling — STRIDE

**What it is.** A structured process for identifying what needs protection, how data and
control flow through a system, where trust boundaries exist, how components can be attacked,
and what mitigations are required. STRIDE (Spoofing, Tampering, Repudiation, Information
Disclosure, Denial of Service, Elevation of Privilege) is used as a threat-classification
aid.

**Why used here.** CyberPipe is unusual: a chat channel (Telegram) is granted the ability to
control a host PC (volume, media, lock/sleep, file fetch, code execution via sandbox). This
inverts the usual "chat is read-only" trust model and makes threat modeling essential. The
plans claim strong guarantees (Anti-Wipe, Volume Clamp, Hard Tool Stripping, OS sandbox
equivalence) but the muse-wonder audit (`AUD-04`, `AUD-07`, `AUD-08`, `AUD-09`, `AUD-13`)
shows several of those guarantees rest on unverified PoCs. Threat modeling separates
**claimed** mitigations from **verified** ones.

**How applied.** Assets, entry points, trust boundaries, and data flows are extracted from
the plans; STRIDE is applied per trust-boundary crossing; each threat becomes a `THREAT-xxx`
with likelihood/severity (defined scales), existing claimed mitigation, proposed mitigation,
verification method, and residual risk. No vulnerability scanning or penetration testing was
performed (there is no code to scan).

**Expected outputs.** A threat register, a data-flow map, a mitigations/verification matrix.

**Limitations.**
- STRIDE is a classification aid, **not a complete security assessment**. It does not by
  itself prove that a mitigation works — only that one is needed.
- Threats marked "unverified" are hypotheses requiring validation, not confirmed
  vulnerabilities. Equally, claimed mitigations are not confirmed controls until PoC tests
  pass.
- Side-channel, supply-chain compromise of NuGet/native binaries, and physical access are
  noted but out of scope for a single-user home product beyond the supply-chain item in the
  technology matrix.

**References.**
- OWASP Threat Modeling: <https://owasp.org/www-community/Threat_Modeling>
- Microsoft Threat Modeling Tool: <https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool>
- Microsoft STRIDE methodology: <https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats>

---

## 4. ADR — Architecture Decision Records (MADR)

**What it is.** ADRs record significant architectural decisions together with their context,
considered alternatives, rationale, consequences, status, and revisit triggers. MADR
(Markdown ADR) is the starting format used here.

**Why used here.** CyberPipe has three already-accepted plan-level decisions
([ADR-001 bus](../plans/17-adr-mvp.md), [ADR-002 DB layout](../plans/17-adr-mvp.md),
[ADR-003 SQLite encryption](../plans/17-adr-mvp.md)) and several decisions that **cannot be
made responsibly yet** because they depend on PoC evidence (OS sandbox equivalence, IPC
protocol, dev-tool sandboxing). ADRs make both states explicit: accepted-with-PoC-pending vs.
unresolved-with-alternatives.

**How applied.** `docs/arch-glm/05-adr/` holds a MADR template, an index, and an illustrative
ADR. Because no project-internal decision has yet been made *outside* the three MVP ADRs in
`docs/plans/17-adr-mvp.md`, the example ADR is clearly labeled illustrative and the index
points to the plan-level ADRs as the authoritative accepted set. New ADRs created during
implementation will follow the same template and numbering.

**Expected outputs.** A consistent template, an index, decision records that distinguish
*Proposed* from *Accepted*, and explicit revisit triggers tied to PoC outcomes.

**Limitations.**
- ADRs capture decisions, not their execution. A decision marked "Accepted" in the plan
  remains "Accepted-pending-PoC" here until the Phase-0 PoC passes.
- MADR is intentionally lightweight; it does not replace a full design document for a
  decision as consequential as OS-sandbox equivalence — that lives in
  [06-validation/technology-compatibility-matrix.md](06-validation/technology-compatibility-matrix.md)
  and is referenced from the ADR.

**References.**
- MADR: <https://adr.github.io/madr/>
- MADR decision records and templates: <https://adr.github.io/madr/decisions/>
- MADR repository: <https://github.com/adr/madr>

---

## 5. How the four methods compose

```
                ┌──────────────── arc42 (structure) ──────────────────┐
                │  §1 goals · §2 constraints · §3 context · §4 strategy │
                │  §5 blocks · §6 runtime · §7 deploy · §8 cross-cutting │
                │  §9 decisions · §10 quality · §11 risks · §12 glossary │
                └────────────┬──────────────────────┬───────────────────┘
                             │ feeds               │ feeds
                ┌────────────▼─────────┐  ┌────────▼────────────┐
                │ ATAM (quality)       │  │ Threat model (sec)  │
                │ QAS · trade-offs     │  │ STRIDE · THREATs    │
                │ RISK register        │  │ mitigations         │
                └────────────┬─────────┘  └────────┬────────────┘
                             │                     │
                             └──────────┬──────────┘
                                        ▼
                              ┌──────── ADR ────────┐
                              │ accepted decisions   │
                              │ unresolved decisions │
                              │ revisit triggers     │
                              └──────────────────────┘
```

arc42 supplies the spine; ATAM and threat modeling feed quality and security findings into
the relevant arc42 sections (§10, §11) and into dedicated sub-folders; ADRs crystallize the
decisions (or non-decisions) that flow out of all three. Every cross-reference uses stable
IDs (`REQ-`, `QAS-`, `RISK-`, `THREAT-`, `ADR-`, `TEST-`, `ASSUMPTION-`, `AUD-`).
