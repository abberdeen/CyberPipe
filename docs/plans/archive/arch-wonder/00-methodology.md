# 00 — Methodologies: arc42, ATAM, Threat Modeling, ADR

> Note on references (verified 2026-10-18 for `docs/arch-wonder/`): the links below are
> the authoritative sources named in the task statement ([`../arch42.md`](../arch42.md)).
> Live fetches in this task returned HTTP 200 for arc42 overview
> (`https://arc42.org/overview/`), the SEI ATAM report page
> (`https://www.sei.cmu.edu/library/the-architecture-tradeoff-analysis-method/`), and
> MADR (`https://adr.github.io/madr/`). OWASP Threat Modeling and Microsoft Threat
> Modeling Tool links were not re-fetched in this task and remain **to be re-verified**
> before citing them as live; no link was found moved, so no replacement is recorded.
> Technology-package facts (NuGet versions, maintenance status) were not live-verified
> and stay marked *Unverified* in the compatibility matrix.

## 1. arc42

**What it is.** A pragmatic, template-based method for documenting software architecture
in twelve sections: 1) introduction and goals, 2) constraints, 3) context and scope,
4) solution strategy, 5) building-block view, 6) runtime view, 7) deployment view,
8) cross-cutting concepts, 9) decisions, 10) quality requirements, 11) risks and technical
debt, 12) glossary. Overview: <https://arc42.org/overview>; full docs:
<https://docs.arc42.org/>; method: <https://arc42.org/method/>; examples:
<https://examples.arc42.org/sections/>.

**Why it is used here.** It is the primary structure of this baseline
([`02-arc42/`](02-arc42/09-architecture-decisions.md)). Its contribution is
**completeness and navigability**: a reviewer can find goals, constraints, structure,
behavior, deployment, and risks in predictable places instead of scattered plan notes.

**How it is applied.** Each arc42 section is populated to the depth the evidence
supports. Sections with no evidence are marked explicitly (never silently omitted).
Proposed components are labeled *proposed*; nothing is presented as implemented because
the repository contains no implementation code (see
[01-overview.md](01-overview.md)).

**Expected outputs.** The twelve files under `02-arc42/`.

**Limitations.** arc42 describes and organizes; it does not evaluate trade-offs,
quantify quality, or find threats. Evaluation is delegated to ATAM, threat modeling,
and ADRs below.

## 2. ATAM (Architecture Tradeoff Analysis Method)

**What it is.** An architecture evaluation method from the SEI (Carnegie Mellon) that
examines how architectural decisions affect competing quality attributes (performance,
security, availability, modifiability, …) via quality-attribute scenarios, sensitivity
points, trade-off points, and risks. References:
<https://www.sei.cmu.edu/library/the-architecture-tradeoff-analysis-method/> and
<https://www.sei.cmu.edu/library/atam-method-for-architecture-evaluation/>.

**Why it is used here.** CyberPipe's plans compete on exactly the attributes ATAM
handles: sub-millisecond latency vs. process isolation, token economy vs. answer quality,
SQLite simplicity vs. durability/encryption, Windows-first delivery vs. portability.

**How it is applied.** As a **documented lightweight ATAM-inspired assessment** —
no stakeholder workshop and no full formal evaluation took place, and none is claimed.
Utility tree → measurable scenarios ([`03-atam/quality-attribute-scenarios.md`](03-atam/quality-attribute-scenarios.md))
→ analysis of approaches → risks/sensitivities/trade-offs
([`03-atam/tradeoff-analysis.md`](03-atam/tradeoff-analysis.md)) → ranked register
([`03-atam/risk-register.md`](03-atam/risk-register.md)).

**Expected outputs.** The three files under `03-atam/`, cross-linked to arc42 quality
requirements and the risk register.

**Limitations.** Without stakeholders, scenario priorities and numeric targets are the
analyst's proposal, marked as such. Rankings reflect engineering judgment over plan
evidence, not business sign-off — a full ATAM workshop remains recommended before
Release 1.0 scope lock.

## 3. Threat Modeling (+STRIDE)

**What it is.** A structured process: enumerate what must be protected (assets), map how
data and control flow, mark where trust changes (trust boundaries), identify who can
attack and how, then assign mitigations and residual risk. STRIDE classifies threats as
Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation
of Privilege. References: <https://owasp.org/www-community/Threat_Modeling>;
<https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool>;
<https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats>.

**Why it is used here.** CyberPipe is a remote-controlled host agent: Telegram messages
can trigger local execution, audio capture, file access, and secret use. The highest
architectural risks are trust-boundary violations (chat → host, sandbox → host,
untrusted document → tool use). Threat modeling forces each boundary to name its enforced
control — a library name alone is never accepted as mitigation.

**How it is applied.** Scope/assets/boundaries
([`04-threat-model/scope-assets-and-trust-boundaries.md`](04-threat-model/scope-assets-and-trust-boundaries.md))
→ data flows ([`04-threat-model/data-flows.md`](04-threat-model/data-flows.md)) →
STRIDE-classified register with severity, evidence, and owner
([`04-threat-model/threat-register.md`](04-threat-model/threat-register.md)) →
mitigations with verification methods
([`04-threat-model/mitigations-and-verification.md`](04-threat-model/mitigations-and-verification.md)).
Unverified threats are marked as hypotheses.

**Expected outputs.** The four files under `04-threat-model/`.

**Limitations.** STRIDE is a classification aid, not a complete assessment: it does not
replace penetration testing, dependency review, or the mandatory negative isolation
tests (`CyberPipe.Tests.Isolation`, still unwritten). Cryptography choices (SQLCipher,
AES-256-GCM usage) need expert review beyond this model.

## 4. ADR (Architecture Decision Records, MADR format)

**What it is.** Short, immutable records of significant decisions: context, drivers,
alternatives considered, the decision and its rationale, consequences (positive and
negative), risks, validation criteria, and revisit triggers. Starting format: MADR —
<https://adr.github.io/madr/>, decisions/templates: <https://adr.github.io/madr/decisions/>,
repository: <https://github.com/adr/madr>.

**Why it is used here.** The plans already contain de-facto decisions (bus, DB layout,
encryption approach, OS scope) plus audit questions L1–L6 demanding explicit answers.
ADRs convert implicit consensus into reviewable commitments and stop silent drift
(e.g. the `Channels` vs `MassTransit` contradiction found by the audit).

**How it is applied.** One ADR per expensive-to-reverse, security/reliability-relevant,
or highly uncertain decision ([`05-adr/`](05-adr/README.md)). Statuses are honest:
**Proposed** for project-endorsed-but-unproven choices (no code exists to validate
against); **Accepted** only with adequate evidence or explicit approval — none is
claimed yet. Where no responsible choice is possible, the ADR records alternatives,
missing information, and the verification plan instead of choosing.

**Expected outputs.** `05-adr/README.md`, `05-adr/template.md`, numbered decision records.

**Limitations.** ADRs record decisions; they do not implement or validate them. Every
ADR here carries acceptance criteria pointing at Phase-0 PoCs and tests.
