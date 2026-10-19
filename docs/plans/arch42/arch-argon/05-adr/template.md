# ADR-NNNN: [Short Title of Solved Problem and Solution]

- **Status:** `Proposed` | `Accepted` | `Superseded by ADR-XXXX` | `Deprecated` | `Rejected`
- **Date:** YYYY-MM-DD
- **Deciders:** CyberPipe Architecture Team
- **Source Plan Evidence:** [Link to `docs/plans/...`]
- **Related Requirements:** `REQ-XXX`
- **Related Quality Scenarios (ATAM):** `QAS-XXX`
- **Related Risks & Threats:** `RISK-XXX`, `THREAT-XXX`
- **Verification Gate:** `TEST-XXX`

---

## 1. Context and Problem Statement

[Describe the architectural context and problem statement in 2–4 concise paragraphs. Explain what forces are in play, what hidden constraints or contradictions were discovered in `docs/plans/`, and why this decision is expensive to change once implemented.]

---

## 2. Decision Drivers

- **Driver 1 (`REQ-XXX` / `QAS-XXX`):** [Specific measurable quality attribute or requirement]
- **Driver 2 (`RISK-XXX` / `THREAT-XXX`):** [Specific technical constraint, security boundary, or failure mode]
- **Driver 3:** [Operational, licensing, or resource footprint constraint]

---

## 3. Considered Options

1. **Option 1:** [Name of Option 1 — e.g., Baseline from `docs/plans/`]
2. **Option 2:** [Name of Option 2 — e.g., Hardened / Revised Architecture]
3. **Option 3:** [Name of Option 3 — e.g., Alternative Framework or External Service]

### Option Comparison Matrix

| Criterion | Option 1 | Option 2 | Option 3 |
| :--- | :--- | :--- | :--- |
| **Technical Feasibility** | ... | ... | ... |
| **Security & Isolation** | ... | ... | ... |
| **Performance / Resource Footprint** | ... | ... | ... |
| **Maintenance & Licensing** | ... | ... | ... |

---

## 4. Decision Outcome

**Chosen Option:** **[Option X — Name]**

[Provide a clear, specific specification of the chosen architectural mechanism, including exact NuGet packages, configuration settings, failure handling, and security invariants.]

---

## 5. Consequences and Trade-Offs

### Positive Consequences (`+`)
- **`+` [Quality Attribute]:** [Concrete benefit]

### Negative Consequences and Trade-Offs (`-`)
- **`-` [Competing Quality Attribute]:** [Concrete trade-off cost and how it is mitigated]

---

## 6. Validation and Compliance Checks

- **Automated Verification Test (`TEST-XXX`):** [Describe the exact unit, integration, benchmark, or `ArchUnitNET` test that proves compliance]
- **Acceptance Criteria for Transitioning Status to `Accepted`:** [Measurable pass criterion]

---

## 7. Cross-References
- **arc42 Section:** [`../02-arc42/...`](../02-arc42/09-architecture-decisions.md)
- **ATAM Risk / Scenario:** [`../03-atam/...`](../03-atam/risk-register.md)
- **Threat Model:** [`../04-threat-model/...`](../04-threat-model/threat-register.md)
