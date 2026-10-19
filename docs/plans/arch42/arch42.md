# Task: Create a comprehensive software architecture documentation in `docs/arch-glm/`

## 1. Role and objective

Act as a Senior/Principal Software Architect, Security Architect, and Reliability Engineer.

Your task is to analyze the existing project plans in `docs/plans/` and create a comprehensive, evidence-based software architecture documentation in a new directory: `docs/arch-glm/`.

Use the following methodologies together:

1. **arc42** — the primary structure for architecture documentation.
2. **ATAM (Architecture Tradeoff Analysis Method)** — systematic analysis of quality attributes, architectural trade-offs, risks, and sensitive decisions.
3. **Threat Modeling** — systematic identification of assets, trust boundaries, attack surfaces, threats, and mitigations.
4. **ADR (Architecture Decision Records)** — explicit documentation of important architectural decisions, alternatives, rationale, consequences, and unresolved questions.

The objective is not merely to describe a proposed architecture. It is to discover architectural risks, hidden constraints, incompatible assumptions, and technology limitations before implementation makes them expensive to fix.

Treat `docs/plans/` as the primary source of project intent. Treat it as input to analyze, not as an unquestionable source of technically correct decisions.

## 2. Mandatory preliminary investigation

Before writing architecture documents:

1. Inspect the repository structure and identify the project type, languages, frameworks, existing source code, configuration files, dependencies, tests, and build system.
2. Read all relevant documents in `docs/plans/`, including referenced subdocuments where available.
3. Inspect existing README files, package manifests, project files, dependency lockfiles, configuration, and implementation code when they provide evidence relevant to the architecture.
4. Identify contradictions, ambiguous requirements, missing information, implicit assumptions, external dependencies, and potentially obsolete decisions.
5. Trace each major architectural requirement back to its source document or source-code evidence.
6. Create an inventory of the documents and source artifacts examined.

Do not assume the system's architecture, technology stack, deployment model, or operational requirements before inspecting the repository.

Do not modify `docs/plans/` or existing application code. This task is primarily architecture analysis and documentation.

Create `docs/arch-glm/` if it does not exist. Preserve existing files if the directory already contains content. Inspect and reconcile them before adding or modifying documents.

## 3. Methodology descriptions and authoritative references

Create `docs/arch-glm/00-methodology.md`.

Explain the purpose, scope, workflow, expected outputs, limitations, and practical application of each methodology.

### 3.1 arc42

Description:
arc42 is a pragmatic software architecture documentation template and method. It organizes architecture information into twelve sections, covering goals, constraints, context, solution strategy, building blocks, runtime behavior, deployment, cross-cutting concepts, decisions, quality requirements, risks, technical debt, and terminology.

Use arc42 as the primary structure of the architecture documentation. Adapt its depth to the project, but do not silently omit important sections. Mark genuinely inapplicable sections and explain why.

Official references:
- Overview: https://arc42.org/overview/
- Documentation: https://docs.arc42.org/
- Method: https://arc42.org/method/
- Practical examples: https://examples.arc42.org/sections/

### 3.2 ATAM

Description:
ATAM is an architecture evaluation method developed by the Software Engineering Institute at Carnegie Mellon University. It evaluates how architectural decisions influence competing quality attributes, such as performance, security, availability, reliability, and modifiability.

Use ATAM principles to identify architectural drivers, prioritize quality attributes, construct measurable quality attribute scenarios, analyze architectural approaches, identify risks and sensitivity points, and document trade-offs.

Do not claim that a formal stakeholder workshop or complete formal ATAM evaluation occurred unless it actually did. If necessary, perform a documented, lightweight ATAM-inspired assessment and identify its limitations.

Official references:
- SEI ATAM overview and report: https://www.sei.cmu.edu/library/the-architecture-tradeoff-analysis-method/
- ATAM evaluation report: https://www.sei.cmu.edu/library/atam-method-for-architecture-evaluation/

### 3.3 Threat Modeling

Description:
Threat Modeling is a structured process for identifying what needs protection, how data and control flow through a system, where trust boundaries exist, how components can be attacked, and what mitigations are required.

Use STRIDE as a threat-classification aid where applicable:
- Spoofing
- Tampering
- Repudiation
- Information Disclosure
- Denial of Service
- Elevation of Privilege

Identify assets, entry points, trust boundaries, identities, privileged components, data flows, attacker capabilities, security assumptions, threats, mitigations, residual risks, and verification methods.

Do not treat STRIDE as a complete security assessment by itself.

References:
- OWASP Threat Modeling: https://owasp.org/www-community/Threat_Modeling
- Microsoft Threat Modeling Tool: https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool
- Microsoft STRIDE methodology: https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats

Verify that the referenced resources are accessible and relevant. If a link has moved, use the authoritative current equivalent and document the replacement.

### 3.4 ADR — Architecture Decision Records

Description:
ADR is a practice for recording significant architectural decisions together with their context, considered alternatives, rationale, consequences, and status.

Create an ADR for each important decision that is expensive to change, has significant security or reliability implications, affects multiple components, or contains substantial uncertainty.

Use Markdown Architectural Decision Records (MADR) as the starting format.

References:
- MADR: https://adr.github.io/madr/
- MADR decision records and templates: https://adr.github.io/madr/decisions/
- MADR repository: https://github.com/adr/madr

Every reference must explain what the methodology contributes to this project, not merely provide a link.

## 4. Required output structure

Create the following structure under `docs/arch-glm/`. You may add files or directories when justified, but maintain a clear entry point and avoid unnecessary fragmentation.

    docs/arch-glm/
    ├── README.md
    ├── 00-methodology.md
    ├── 01-overview.md
    ├── 02-arc42/
    │   ├── 01-introduction-and-goals.md
    │   ├── 02-architecture-constraints.md
    │   ├── 03-context-and-scope.md
    │   ├── 04-solution-strategy.md
    │   ├── 05-building-block-view.md
    │   ├── 06-runtime-view.md
    │   ├── 07-deployment-view.md
    │   ├── 08-crosscutting-concepts.md
    │   ├── 09-architecture-decisions.md
    │   ├── 10-quality-requirements.md
    │   ├── 11-risks-and-technical-debt.md
    │   └── 12-glossary.md
    ├── 03-atam/
    │   ├── quality-attribute-scenarios.md
    │   ├── tradeoff-analysis.md
    │   └── risk-register.md
    ├── 04-threat-model/
    │   ├── scope-assets-and-trust-boundaries.md
    │   ├── data-flows.md
    │   ├── threat-register.md
    │   └── mitigations-and-verification.md
    ├── 05-adr/
    │   ├── README.md
    │   ├── template.md
    │   └── 0001-example-decision.md
    └── 06-validation/
        ├── assumptions-and-open-questions.md
        ├── technology-compatibility-matrix.md
        └── architecture-review-checklist.md

The example ADR must be clearly identified as illustrative unless it records a real, evidence-supported project decision. Prefer a real initial decision if sufficient evidence exists. Never present a hypothetical decision as an approved one.

All documents must link to related documents using correct relative Markdown links.

The root `README.md` must provide an entry point, explain the document hierarchy, describe the methodologies, and identify the current architecture status and major unresolved decisions.

## 5. Architecture documentation requirements

### 5.1 Introduction, scope, and goals

Document:
- The system's purpose and expected capabilities.
- Stakeholders and their concerns, where known.
- Functional requirements derived from project plans.
- Business and technical goals.
- Explicit non-goals.
- Scope boundaries and external systems.
- Important assumptions and unknowns.
- Architectural drivers and major constraints.

Distinguish explicit requirements from inferred requirements and recommendations.

### 5.2 Context and building blocks

Describe the system from the outside in:

1. System context and external actors.
2. Major subsystems and containers.
3. Component responsibilities and boundaries.
4. Dependencies and communication mechanisms.
5. Critical data flows and control flows.
6. Runtime scenarios and failure paths.
7. Deployment and operational model.

Use Mermaid diagrams where they materially improve understanding. Include, as appropriate:
- System context diagram.
- Container or subsystem diagram.
- Component diagram for critical subsystems.
- Runtime sequence diagrams for important workflows.
- Deployment diagram.
- Data-flow diagram and trust boundaries.

Diagrams must represent the documented architecture accurately. Label proposed components as proposed and distinguish them from components confirmed by existing code.

For each critical component, describe its responsibility, interfaces, dependencies, lifecycle, failure modes, data ownership, security boundary, and recovery behavior.

### 5.3 Cross-cutting concepts

Explicitly analyze relevant topics such as:
- Configuration and secrets management.
- Authentication, authorization, and local privilege boundaries.
- Encryption at rest and in transit.
- Logging, diagnostics, and sensitive-data redaction.
- Error handling and retries.
- Timeouts, cancellation, and resource limits.
- Concurrency and synchronization.
- Transactions and consistency.
- Idempotency and duplicate processing.
- Persistence and schema migrations.
- Backup, recovery, and disaster scenarios appropriate to the deployment model.
- Versioning and compatibility.
- Dependency management and software supply-chain risks.
- Licensing and redistribution restrictions.
- Updates, rollback, and backward compatibility.
- Observability, maintenance, and supportability.

Do not mechanically include every topic if it is genuinely irrelevant. Explain important exclusions.

## 6. Mandatory hidden-constraint analysis

This section is critical.

For every major technology choice and every important architectural boundary, investigate not only advertised features but also operational limitations, licensing restrictions, version-specific behavior, failure semantics, integration compatibility, and security implications.

For each relevant technology, record:

- Intended use and architectural responsibility.
- Required capabilities.
- Verified capabilities.
- Unsupported capabilities and known limitations.
- Required edition, extension, package, driver, or configuration.
- Version and runtime compatibility.
- Licensing and distribution implications.
- Failure behavior and recovery semantics.
- Security assumptions and exposure.
- Performance constraints and expected bottlenecks.
- Migration and replacement costs.
- Evidence sources and verification status.

Investigate at least the following areas where applicable.

### Data storage

- Embedded versus server-based deployment requirements.
- Concurrent readers and writers.
- Locking, transaction isolation, and write contention.
- Atomicity and consistency guarantees.
- Crash recovery and durability guarantees.
- Encryption at rest, key management, and backup encryption.
- Backup consistency and restoration procedures.
- Database corruption and disk-full behavior.
- Schema migrations and rollback compatibility.
- Data integrity constraints and indexing.
- Provider-specific ORM limitations.
- Maximum practical data size and expected workload.
- File permissions and exposure to other local processes.

Do not assume that a database supports or lacks a capability without checking the actual engine, version, provider, extension, and configuration.

### Messaging and background processing

- Delivery semantics and their exact scope.
- At-most-once, at-least-once, and effectively-once processing assumptions.
- Duplicate delivery and idempotency.
- Message persistence and acknowledgement behavior.
- Retry policies, backoff, poison messages, and dead-letter handling.
- Ordering guarantees.
- Transaction boundaries between message publication and database commits.
- Outbox/inbox requirements where appropriate.
- Behavior during process termination and restart.
- Embedded versus broker-based deployment.
- Resource limits, backpressure, and bounded queues.
- Compatibility with the selected persistence technology.

Do not assume that a messaging library supports a storage provider or transaction model simply because an adapter or integration package exists.

### Desktop and process architecture

If the project is a desktop application, investigate:
- Single-instance and multi-instance behavior.
- IPC security and local endpoint exposure.
- Process lifecycle and graceful shutdown.
- Child-process permissions and sandbox escape boundaries.
- Filesystem and network access restrictions.
- Secrets exposure to local users and processes.
- Local API authorization.
- Updates, crash recovery, and state migration.
- Installation without administrator privileges where required.

Adapt the checklist to the actual project instead of assuming a desktop deployment.

### General requirements

Also evaluate:
- Third-party API limits and availability.
- Network interruption and offline behavior.
- Timeouts and partial failures.
- Memory, CPU, disk, and handle limits.
- Dependency end-of-life and maintenance status.
- Required native binaries and platform support.
- Reproducible builds and deployment requirements.
- Portability across target operating systems.

A capability advertised in a README is not automatically a verified production guarantee.

## 7. ATAM-inspired quality analysis

Create measurable quality attribute scenarios.

For each important scenario, document:
- ID and title.
- Source or rationale.
- Stimulus.
- Operating environment.
- Affected component.
- Expected response.
- Measurable response criterion.
- Verification method.
- Architectural decisions affecting the scenario.

Include relevant quality attributes such as:
- Reliability and recoverability.
- Security and privacy.
- Performance and scalability.
- Modifiability and testability.
- Availability, if relevant to the deployment model.
- Compatibility and portability.
- Operational simplicity.
- Data durability and integrity.

Avoid vague targets such as "fast", "secure", "highly reliable", or "scalable". Define measurable targets where requirements or stakeholder input support them.

When numeric targets cannot be justified, mark them as proposed targets or open questions. Do not fabricate stakeholder requirements.

Identify:
- Risks.
- Non-risks supported by evidence.
- Sensitivity points.
- Trade-off points.
- Competing quality attributes.
- Risk themes affecting multiple components.
- Recommended mitigations and validation experiments.

Rank risks using explicit severity and likelihood criteria. Explain the scoring scale and distinguish confirmed defects from potential risks.

## 8. Threat modeling requirements

Build a threat model based on the actual system scope and architecture.

Identify:
- Assets and their confidentiality, integrity, and availability requirements.
- Trust boundaries.
- External and internal actors.
- Entry points.
- Data flows and storage locations.
- Privileged operations and identities.
- Relevant attacker capabilities.
- Threat scenarios using STRIDE where applicable.
- Existing mitigations, proposed mitigations, and residual risks.

For every threat, record:
- Unique ID.
- Threat description.
- Affected asset and component.
- Preconditions and attack path.
- Impact.
- Likelihood and severity, with defined criteria.
- Existing evidence and controls.
- Proposed mitigation.
- Verification method.
- Residual risk and ownership, if known.

Pay particular attention to trust-boundary violations, insecure defaults, local privilege escalation, secrets leakage, unauthorized file access, dependency compromise, denial of service, tampering, and recovery mechanisms.

Do not claim a threat is mitigated merely because a security library or sandbox is present. Identify the specific enforced control and its limitations.

Do not invent vulnerabilities in existing components. Mark unverified threats as hypotheses requiring validation.

## 9. ADR requirements

Create an ADR for every major architectural decision that is supported by sufficient evidence.

Candidate topics include:
- Overall architecture and component boundaries.
- Database and persistence strategy.
- Messaging and background processing.
- Transaction and consistency strategy.
- Secrets management and encryption.
- IPC and local API security.
- Scheduling and retry behavior.
- Process isolation.
- Deployment and update strategy.
- Observability and recovery.

These are candidate topics, not predetermined decisions. Include only relevant decisions.

Use a consistent ADR template with:
- Title and unique identifier.
- Status: Proposed, Accepted, Rejected, Deprecated, or Superseded.
- Date, if known.
- Context and constraints.
- Decision drivers and mandatory requirements.
- Alternatives considered.
- Evidence and comparative analysis.
- Decision and rationale.
- Positive and negative consequences.
- Risks and mitigations.
- Validation and acceptance criteria.
- Revisit triggers.
- Related ADRs and source documents.

Do not mark decisions Accepted without adequate evidence or explicit approval in the existing project context.

When a critical decision cannot be made responsibly, document the alternatives, missing information, verification plan, and decision owner if known. Do not silently choose a technology.

## 10. Technology compatibility matrix

Create `06-validation/technology-compatibility-matrix.md`.

For every critical technology and integration, include:
- Technology or package.
- Intended responsibility.
- Required capabilities.
- Version and runtime constraints.
- Compatibility evidence.
- Known limitations.
- Licensing and distribution considerations.
- Security and reliability implications.
- Unresolved questions.
- Verification method.
- Status: Verified, Partially verified, Unverified, Incompatible, or Not applicable.

Use official documentation, release notes, source code, issue trackers, and reproducible tests where appropriate.

Prefer authoritative, version-specific sources. Cite URLs and identify the relevant versions. Record the date of verification.

When a conclusion depends on an assumption, state it explicitly.

Do not fabricate citations, package capabilities, benchmark results, issue reports, or test outcomes.

Distinguish:
1. Verified facts.
2. Facts documented by the vendor but not independently tested.
3. Inferences.
4. Unverified hypotheses.
5. Recommendations.

For critical claims, explain the evidence and what it actually proves.

## 11. Traceability and consistency

Maintain traceability across the documentation.

Where applicable, use stable IDs:
- `REQ-xxx` for requirements.
- `QAS-xxx` for quality attribute scenarios.
- `RISK-xxx` for architecture risks.
- `THREAT-xxx` for threats.
- `ADR-xxxx` for decisions.
- `TEST-xxx` for validation activities.
- `ASSUMPTION-xxx` for assumptions.

Link requirements to architecture elements, decisions, risks, threats, and validation activities.

Ensure that:
- Each critical architectural requirement has an identified implementation responsibility or an explicit unresolved status.
- Each critical risk has a mitigation, acceptance decision, or validation plan.
- Each important threat has a control or a documented residual-risk decision.
- Each accepted ADR has supporting rationale and evidence.
- The overview agrees with the detailed documents.
- Diagrams agree with the text.
- Proposed decisions are not presented as implemented facts.

Do not introduce redundant documents or duplicate the same information across files without a clear reason. Prefer links to a single source of truth.

## 12. Architecture review checklist

Create a practical checklist in `06-validation/architecture-review-checklist.md`.

It must help reviewers detect:
- Missing requirements or system boundaries.
- Unsupported architectural assumptions.
- Hidden technology limitations.
- Incompatible dependencies.
- Unexamined failure modes.
- Security and trust-boundary gaps.
- Concurrency and transaction problems.
- Backup and recovery gaps.
- Unclear ownership of data or responsibilities.
- Missing measurable acceptance criteria.
- Missing or unjustified architectural decisions.
- Contradictions between documents.
- Risks that were discovered but not addressed.

For each checklist item, include the question, why it matters, expected evidence, and the condition under which it can be marked complete or not applicable.

## 13. Evidence and research rules

Use the repository as the primary source for project-specific requirements.

Use official and authoritative external sources for technical facts. When needed, inspect the actual source code, package metadata, version-specific documentation, release notes, known issues, and licenses.

Do not invent facts to make the architecture look complete.

Do not hide uncertainty behind confident prose. Record missing evidence and identify the cheapest reliable way to obtain it.

Do not perform destructive operations, modify application behavior, install dependencies unnecessarily, or make unrelated code changes.

Do not claim to have run tests, benchmarks, security scans, or compatibility experiments unless they were actually executed.

If internet access is unavailable, continue with repository analysis and explicitly mark external facts requiring verification.

## 14. Writing standards

Write all architecture documents in English, using precise technical terminology.

The documentation must be:
- Detailed enough for an experienced engineer to implement and review the architecture.
- Concise enough to navigate without reading every document.
- Explicit about assumptions, constraints, trade-offs, and failure modes.
- Supported by evidence and internal cross-references.
- Understandable independently of the original conversation.
- Consistent with the actual repository and project plans.

Use tables for comparisons, Mermaid diagrams for architecture and flows, and Markdown lists for checklists.

Avoid generic filler, textbook passages unrelated to the project, and unsubstantiated claims.

For each methodology, provide a concise explanation of what it is, why it is used, how it is applied to this project, its expected outputs, its limitations, and authoritative links.

## 15. Execution workflow

Perform the task in the following order:

1. Inspect the repository and inventory `docs/plans/`.
2. Extract project requirements, assumptions, constraints, and existing decisions.
3. Identify missing information and contradictory statements.
4. Establish the system context and draft the high-level architecture.
5. Develop the arc42 documentation progressively from goals and constraints to components, runtime, deployment, and risks.
6. Create measurable quality attribute scenarios and perform an ATAM-inspired trade-off analysis.
7. Develop the threat model and map threats to mitigations and verification activities.
8. Research critical technology constraints and create the compatibility matrix.
9. Create ADRs for justified decisions and explicit records of unresolved decisions.
10. Cross-link all documents and verify traceability.
11. Review the documentation for consistency, unsupported claims, missing critical areas, and invalid relative links.
12. Update the architecture overview and review checklist to reflect the actual results.

Do not stop after creating empty templates. Populate the documents using the available project evidence.

Do not stop merely because some information is missing. Document the uncertainty, explain its architectural impact, and define the next verification step.

If a missing answer would fundamentally change the architecture, present the alternatives and their consequences rather than concealing the ambiguity.

## 16. Definition of done

The task is complete only when:

- `docs/arch-glm/README.md` provides a usable entry point.
- The arc42 structure is populated to the level justified by available evidence.
- ATAM-inspired quality scenarios and trade-off analysis exist.
- A threat model includes concrete system-relevant threats and verification activities.
- ADRs capture supported decisions and unresolved decision points.
- Critical technologies have been assessed against explicit capability requirements.
- Major assumptions, unknowns, and risks are recorded.
- Important requirements and decisions are traceable.
- Internal links and diagrams have been checked for consistency.
- No unsupported technical claims are presented as verified facts.
- Existing plans and application code remain unchanged.

After completing the work, provide a concise execution report containing:
1. Files created or modified under `docs/arch-glm/`.
2. Major architectural findings.
3. Critical risks discovered.
4. Decisions that remain unresolved.
5. Important facts that could not be verified.
6. Validation checks actually performed.
7. Recommended next actions, ordered by priority.

Begin by inspecting the repository. Do not ask questions that can be answered from the available files. Do not implement the application; focus on producing a trustworthy architecture baseline.