# ADR-0001: 4-Tier Intent Router, Deterministic C# Orchestrator, and 16-Project Modular Monolith DAG

> **Note on `0001-example-decision.md` Role:** Per Section 4 of the architecture mandate (*"Prefer a real initial decision if sufficient evidence exists. Never present a hypothetical decision as an approved one"*), this file records a **real, evidence-supported core project decision** derived from [`01-principles.md`](../../plans/01-principles.md), [`02-architecture-diagram.md`](../../plans/02-architecture-diagram.md), [`06-solution-structure.md`](../../plans/06-solution-structure.md), and [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-002`, `ADR-005`). Its status is **`Proposed`** pending Phase 0/1 code implementation.

- **Status:** `Proposed` *(Real Initial Decision — Unimplemented in `CyberPipe.slnx`)*
- **Date:** 2026-10-18
- **Deciders:** CyberPipe Architecture Team
- **Source Plan Evidence:** [`01-principles.md`](../../plans/01-principles.md), [`02-architecture-diagram.md`](../../plans/02-architecture-diagram.md), [`06-solution-structure.md`](../../plans/06-solution-structure.md), [`10-sandbox-telemetry-and-heuristics.md`](../../plans/10-sandbox-telemetry-and-heuristics.md), [`13-model-advisor-and-tier-configuration.md`](../../plans/13-model-advisor-and-tier-configuration.md)
- **Related Requirements:** `REQ-001`, `REQ-003`, `REQ-018`
- **Related Quality Scenarios (ATAM):** [`QAS-001`](../03-atam/quality-attribute-scenarios.md#qas-001), [`QAS-002`](../03-atam/quality-attribute-scenarios.md#qas-002), [`QAS-013`](../03-atam/quality-attribute-scenarios.md#qas-013)
- **Related Risks & Threats:** [`RISK-004`](../03-atam/risk-register.md#risk-004), [`RISK-010`](../03-atam/risk-register.md#risk-010), [`RISK-011`](../03-atam/risk-register.md#risk-011), [`RISK-015`](../03-atam/risk-register.md#risk-015), [`THREAT-001`](../04-threat-model/threat-register.md#threat-001)
- **Verification Gate:** [`TEST-007`](../06-validation/assumptions-and-open-questions.md#test-007), [`TEST-013`](../06-validation/assumptions-and-open-questions.md#test-013)

---

## 1. Context and Problem Statement

Conventional open-source AI assistants (such as LangChain/AutoGPT-style loops or OpenClaw) route **every** user interaction—even trivial commands like *"set volume to 40%"* or *"show today's reminders"*—through an LLM agent loop. This creates three severe architectural problems for a 24/7 personal assistant:
1. **Unacceptable Latency & Token Burn:** Simple commands take `1.5–5.0 seconds` and consume thousands of paid cloud tokens (`$0.01–$0.10` per interaction).
2. **Non-Deterministic Safety & Security Failures:** Allowing an LLM to control execution flow, security filtering, and tool dispatch exposes the system to indirect prompt injection (`THREAT-001`) and runaway recursive loops (`THREAT-013`).
3. **Architectural Erosion:** Without strict compile-time module boundaries, domain skills, database access, and orchestration logic quickly tangle into an unmaintainable ball of mud.

We must define the core execution pipeline and compile-time assembly structure for **CyberPipe**.

---

## 2. Decision Drivers

- **`REQ-003` / `QAS-001` / `QAS-002`:** Resolve $\ge 80\%$ of routine commands deterministically in **`< 1 ms` (`Tier 0`)** or **`< 15 ms` (`Tier 1`)** with **`0` LLM tokens**.
- **`REQ-006` / `QAS-007`:** Guarantee that security boundaries (sanitization, taint checking, tool stripping, budget limits) are enforced by **deterministic C# code**, never delegated to LLM prompts (*"C# Is the Boss"*).
- **`REQ-001` / `QAS-013`:** Enforce a strict, cycle-free 16-project Directed Acyclic Graph (DAG) with a zero-dependency `CyberPipe.Contracts` leaf assembly.
- **`RISK-010` & `RISK-011`:** Prevent stale `L2 SemanticResponseCache` hits across decentralized SQLite databases and mitigate in-process `ONNX Runtime` native crash risks.

---

## 3. Considered Options

1. **Option 1: LLM-First Agent Framework (Semantic Kernel / AutoGen / LangChain Loop for All Queries)**
2. **Option 2: 4-Tier Cascading Intent Router + Deterministic C# `PipelineOrchestrator` + 16-Project Modular Monolith DAG (Chosen)**
3. **Option 3: Distributed Microservices Architecture (Separate OS Processes/Containers per Domain Skill)**

### Option Comparison Matrix

| Criterion | Option 1 (LLM-First Agent) | Option 2 (4-Tier C# Orchestrator + 16-Proj DAG) | Option 3 (Microservices per Skill) |
| :--- | :--- | :--- | :--- |
| **Routine Command Latency** | Poor (`1,500–5,000 ms`) | **Excellent (`< 1 ms` T0 / `< 15 ms` T1)** | Moderate (`10–50 ms` IPC hop) |
| **Token Cost Efficiency** | Poor (`100%` queries hit LLM) | **Excellent (`0` tokens for T0/T1 & L2 cache hits)** | Good (if router is shared) |
| **Prompt Injection Resistance** | Weak (LLM controls loop) | **Strong (C# controls routing & Hard Tool Stripping)** | Strong |
| **Workstation RAM Footprint** | Moderate (`~200 MB`) | **Low (`< 150 MB` daemon + pooled `Brainstem`)** | Poor (`> 1.5 GB` across 16 processes) |
| **Compile-Time Boundary Enforcement** | Weak | **Strong (`ArchUnitNET` DAG gate)** | Strong |

---

## 4. Decision Outcome

**Chosen Option:** **Option 2 — 4-Tier Cascading Intent Router, Deterministic C# `PipelineOrchestrator`, and 16-Project Modular Monolith DAG (with `RISK-010` and `RISK-011` Hardening).**

### Architectural Specification
1. **Deterministic C# `PipelineOrchestrator` (`CyberPipe.Core`):**
   Every inbound command passes sequentially through C# middleware: `Inbound CentralDataSanitizer` $\rightarrow$ `CancellationRegistry` $\rightarrow$ `TieredIntentRouter` $\rightarrow$ `HardToolStripper` $\rightarrow$ `TokenBudgetGovernor` $\rightarrow$ `Egress CentralDataSanitizer`. The LLM (`Microsoft.Extensions.AI.IChatClient`) is treated strictly as a stateless cognitive function invoked only at `Tier 2` and `Tier 3`.
2. **4-Tier Cascading Intent Router (`CyberPipe.Cognition`):**
   - **Tier 0 (`< 1 ms`, `0` tokens):** Compiled `.NET` `[GeneratedRegex]` and Aho-Corasick trie matching for exact/structured commands (`/stop`, `/status`, volume/media, timers).
   - **Tier 1 (`< 15 ms`, `0` tokens):** Local CPU `Microsoft.ML.OnnxRuntime` (`all-MiniLM-L6-v2`, 384-d `float32` embeddings) + `TensorPrimitives.CosineSimilarity` (`< 1.5 ms` for `10,000` vectors).
     - Cosine $\ge 0.92$ + valid subsystem `MutationEpoch`: returns cached answer from `L2 SemanticResponseCache`.
     - Cosine $\in [0.82, 0.92)$: dispatches directly to the matched C# `ISkill` via `L1 SemanticIntentCache`.
   - **Tier 2 (`<= 3` Cloud turns / `<= 5` Local turns):** Single or bounded tool-calling via `IChatClient` (`Local/Cloud/Auto` routing).
   - **Tier 3 (Sandboxed Agent):** Multi-step planning and sandboxed script execution (`TB-05`).
3. **Mandatory Hardening for `RISK-010` (Cross-DB Cache Invalidation) & `RISK-011` (ONNX Fault Isolation):**
   - Each `SemanticResponseCache` row (stored in `knowledge.db` per `ADR-0003`) **must** record `TargetSubsystemId` and validate against an in-memory `SubsystemMutationEpoch` counter combined with the target database's `PRAGMA data_version` (`RISK-010`).
   - `all-MiniLM-L6-v2.onnx` must pass SHA-256 verification before loading, and `CognitionOptions.RunOnnxInBrainstem` must allow failing over `Tier 1` inference to `Brainstem` Pool A (`RISK-011`).
4. **16-Project Assembly DAG:** Enforced by `CyberPipe.Architecture.Tests` (`ArchUnitNET`) per [`../02-arc42/05-building-block-view.md`](../02-arc42/05-building-block-view.md).

---

## 5. Consequences and Trade-Offs

### Positive Consequences (`+`)
- **`+` Sub-Millisecond to 15ms Responsiveness (`QAS-001`, `QAS-002`):** Common commands execute instantaneously offline with zero API cost.
- **`+` Structural Security (`QAS-007`):** C# enforces taint rules and tool stripping before any LLM is called.
- **`+` Modular Maintainability (`QAS-013`):** Strict 4-layer DAG prevents circular dependencies across 16 projects.

### Negative Consequences and Trade-Offs (`-`)
- **`-` Threshold Sensitivity (`SP-03`):** Cosine similarity thresholds (`0.82` / `0.92`) require empirical tuning against false-positive cache hits.
- **`-` Upfront Scaffolding Effort (`RISK-004`):** Creating 16 projects at once is high-risk; mitigated by starting with the 5-project **Phase 0 Walking Skeleton**.

---

## 6. Validation and Compliance Checks

- **`TEST-007` (`BenchmarkDotNet`):** Verify `Tier 0` routing `p99 < 1.0 ms`, `Tier 1` ONNX inference `p95 < 15.0 ms`, SIMD scan over `10,000` 384-d vectors `p99 < 1.5 ms`, and immediate `L2` cache invalidation upon mutating `crm.db`.
- **`TEST-013` (`ArchUnitNET`):** Verify zero dependency cycles, zero NuGet dependencies in `CyberPipe.Contracts`, and zero references from library layers to `CyberPipe.Host`.
