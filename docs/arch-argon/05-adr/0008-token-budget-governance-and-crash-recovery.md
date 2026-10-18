# ADR-0008: Hard-Capped `TokenBudgetGovernor`, `< 50 ms` `/stop` Preemption, Polly Bulkheads, and `InFlightOperations` Crash Recovery

- **Status:** `Proposed`
- **Date:** 2026-10-18
- **Deciders:** CyberPipe Architecture Team
- **Source Plan Evidence:** [`02-architecture-overview.md`](../../plans/02-architecture-overview.md), [`07-resilience-and-self-healing.md`](../../plans/07-resilience-and-self-healing.md), [`08-token-optimization-and-caching.md`](../../plans/08-token-optimization-and-caching.md), [`13-llm-and-model-routing.md`](../../plans/13-llm-and-model-routing.md), [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-002`, `ADR-005`)
- **Related Requirements:** `REQ-004`, `REQ-009`, `REQ-010`, `REQ-012`
- **Related Quality Scenarios (ATAM):** [`QAS-003`](../03-atam/quality-attribute-scenarios.md#qas-003), [`QAS-009`](../03-atam/quality-attribute-scenarios.md#qas-009), [`QAS-010`](../03-atam/quality-attribute-scenarios.md#qas-010), [`QAS-012`](../03-atam/quality-attribute-scenarios.md#qas-012)
- **Related Risks & Threats:** [`RISK-008`](../03-atam/risk-register.md#risk-008), [`RISK-015`](../03-atam/risk-register.md#risk-015), [`RISK-016`](../03-atam/risk-register.md#risk-016), [`THREAT-013`](../04-threat-model/threat-register.md#threat-013), [`THREAT-014`](../04-threat-model/threat-register.md#threat-014), [`THREAT-015`](../04-threat-model/threat-register.md#threat-015)
- **Verification Gate:** [`TEST-008`](../06-validation/assumptions-and-open-questions.md#test-008), [`TEST-010`](../06-validation/assumptions-and-open-questions.md#test-010), [`TEST-012`](../06-validation/assumptions-and-open-questions.md#test-012)

---

## 1. Context and Problem Statement

Autonomous AI agents routinely fail in production due to four operational hazards:
1. **"Denial of Wallet" & Runaway Tool Loops (`THREAT-013`):** An agent caught in a retry/tool loop or bursts of concurrent scheduled tasks can exhaust tens of dollars in cloud API credits in minutes. Furthermore, our audit found specification drift across `docs/plans/` regarding `MaxLlmTurnsPerTask` (`3` globally in `02`/`08` vs `3` Cloud / `5` Local in `13`, `RISK-016`).
2. **Unresponsive Stop Commands (`THREAT-014`):** If `/stop` is queued behind active tasks or fails to kill child OS processes, the Owner loses emergency control over the assistant.
3. **Cascading Subsystem Exhaustion (`THREAT-010`):** A backlog of slow PDF extractions or cloud LLM streams can starve fast-path `Tier 0/1` commands.
4. **Duplicate Side Effects After Crash (`THREAT-015`):** If the daemon crashes mid-task, blindly re-running an interrupted financial or email command causes duplicate transactions or messages.

---

## 2. Decision Drivers

- **`REQ-009` / `QAS-009` / `THREAT-013`:** Guarantee `0` unapproved cloud spend beyond `100.0%` of `DailyLimitUsd` / `MonthlyLimitUsd` even under concurrent requests, and hard-cap tool loops.
- **`REQ-004` / `QAS-003` / `THREAT-014`:** Terminate all active LLM streams and child sandboxes within **`p99 < 50 ms`** of receiving `/stop`.
- **`REQ-010` / `THREAT-015`:** Prevent duplicate execution of non-idempotent operations after a crash via `InFlightOperations`.

---

## 3. Considered Options

1. **Option 1:** Post-Call Token Accounting + Cooperative `CancellationToken` Only
2. **Option 2 (Chosen):** Atomic Pre-Call SQLite Budget Reservation (`budget.db`) + Reconciled Turn Caps (`3` Cloud / `5` Local) + Priority Out-of-Band `/stop` Preemption (`< 50 ms` Job Object Kill) + Polly v8 Subsystem Bulkheads + `InFlightOperations` Crash Recovery in `cyberpipe_master.db`.
3. **Option 3:** External API Gateway Proxy (e.g., LiteLLM Proxy Container) for Budget Enforcement

### Option Comparison Matrix

| Criterion | Option 1 (Post-Call Tally + Cooperative Cancel) | Option 2 (Atomic Pre-Call Reservation + Priority Kill + `InFlightOperations`) | Option 3 (External LiteLLM Container) |
| :--- | :--- | :--- | :--- |
| **Concurrent Budget Race Protection** | Fails (Concurrent calls overshoot cap) | **100% Atomic (`BEGIN IMMEDIATE` in `budget.db`)** | Pass |
| **`/stop` Preemption Latency** | Poor (`1–30 s` if child process blocks) | **Excellent (`< 50 ms` via `TerminateJobObject` + Socket Abort)** | N/A |
| **Zero-Config Local Footprint** | Pass | **Pass (Built into `CyberPipe.Cognition` & `Core`)** | Fails (Requires Python/Docker) |

---

## 4. Decision Outcome

**Chosen Option:** **Option 2 — Atomic Pre-Call `TokenBudgetGovernor` Reservation, Out-of-Band `< 50 ms` `/stop` Preemption, Polly v8 Bulkheads, and `InFlightOperations` Crash Recovery.**

### Architectural Specification

1. **Atomic Pre-Call Budget Reservation (`TokenBudgetGovernor` in `budget.db`):**
   - Before dispatching any paid cloud LLM request, `TokenBudgetGovernor.TryReserveBudgetAsync` executes a `BEGIN IMMEDIATE` transaction in `data/subsystems/budget.db` reserving:
     $$\text{ReservedCostUsd} = (\text{EstimatedPromptTokens} \times \text{Price}_{\text{in}}) + (\text{MaxOutputTokens} \times \text{Price}_{\text{out}})$$
   - If `CurrentSpendUsd + ReservedCostUsd > DailyLimitUsd` (or `MonthlyLimitUsd`), the reservation is rejected immediately (`100.0%` hard ceiling), paid cloud providers are paused, an alert is sent at `80%` and `100%`, and `ModelRouter` automatically fails over to local `Ollama`/`LlamaSharp` (`QAS-009`).
   - Upon completion (or cancellation), `CommitUsageAsync` reconciles the reservation with actual token usage.
2. **Reconciled Loop Breaker (`RISK-016` Fix):**
   - `MaxCloudLlmTurnsPerTask = 3` (hard ceiling for paid cloud models).
   - `MaxLocalLlmTurnsPerTask = 5` (for local `Ollama`/`LlamaSharp` models, configurable down to `3`).
   - Every cloud call must explicitly set `ChatOptions.MaxOutputTokens` (e.g., `512` for classification/summaries, `2048` for code synthesis).
3. **Out-of-Band `< 50 ms` `/stop` Kill-Switch (`CancellationRegistry`):**
   - `TelegramChannelAdapter` and Contour 1 IPC inspect inbound frames on the socket reader thread *before* enqueueing into the command channel. If the command is `/stop` or `cyberpipe stop`, it invokes `CancellationRegistry.CancelAll()` directly.
   - `CancelAll()` triggers all active `CancellationTokenSource` instances, disposes active outbound HTTP response streams, and calls `TerminateJobObject` (`Win32`) / `kill(-pgid, SIGKILL)` (`POSIX`) on all active ephemeral sandboxes (`QAS-003`).
4. **Polly v8 Subsystem Bulkheads (`SubsystemBulkheadPolicy`):**
   - Independent concurrency compartments prevent cross-subsystem starvation:
     - `FastPath_T0_T1`: Unthrottled (`MaxParallelization = 32`)
     - `Voice_Brainstem_PoolA`: `MaxParallelization = 1, MaxQueue = 4`
     - `Docs_Brainstem_PoolB`: `MaxParallelization = 2, MaxQueue = 8`
     - `CloudLlm_T2_T3`: `MaxParallelization = 4, MaxQueue = 8`
     - `Netrunner_Sandbox`: `MaxParallelization = 2, MaxQueue = 8`
5. **`InFlightOperations` Crash Recovery (Assigned to `cyberpipe_master.db`, `RISK-008` Fix):**
   - Every task writes a pre-execution journal entry to `InFlightOperations` (`IdempotencyCategory`: `ReadOnly`, `SafeIdempotent`, or `SideEffecting`).
   - On startup after an unclean shutdown, `ReadOnly`/`SafeIdempotent` tasks are retried once (`RetryCount == 0`), whereas `SideEffecting` tasks transition to `InterruptedRequiresConfirmation` and prompt the Owner (`REQ-010`).

---

## 5. Consequences and Trade-Offs

### Positive Consequences (`+`)
- **`+` Zero Unapproved Cloud Overages (`QAS-009`):** Pre-call SQLite reservation eliminates concurrent race conditions across parallel tasks.
- **`+` Guaranteed Emergency Control (`QAS-003`):** Bypassing normal queues and terminating OS Job Objects guarantees `< 50 ms` preemption.

### Negative Consequences and Trade-Offs (`-`)
- **`-` Conservative Reservation Headroom:** Pre-reserving `MaxOutputTokens` temporarily holds slightly more budget than the actual completion until the response finishes (`~1–5 seconds`).

---

## 6. Validation and Compliance Checks

- **`TEST-008` (`/stop` Preemption Benchmark):** Launch a streaming cloud mock + infinite-loop sandbox process, send `/stop`, and assert all tokens and OS processes terminate in `p99 < 50.0 ms`.
- **`TEST-010` (Concurrent Budget Race Test):** Fire 10 simultaneous cloud LLM requests when remaining daily budget is `$0.05` (`1` request worth) and assert exactly `1` cloud request is permitted while `9` fail over to local `Ollama`.
