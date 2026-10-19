# arc42 §4 — Solution Strategy

The strategy decomposes the hardest problems (cost, latency, host safety, durability)
into deterministic local mechanisms first, probabilistic/cloud mechanisms last.

1. **Deterministic core, probabilistic edge.** Router, guards, budget governor, and
   sanitizer are strict C# code (0 tokens). LLMs are invoked only when local rules
   cannot decide. This inverts the classic agent-mesh cost profile.
2. **Cascade routing (Tier 0 → 3).** Each tier is cheaper than the next and yields to
   it only on miss; per-tier Local/RemoteCloud assignment keeps `$0` paths available
   even under `Cloud Freeze`.、`TierModelDescriptor` + `ModelHostingType` make the
   hosting choice explicit to code and user.
3. **Budget as middleware, not advice.** `TokenBudgetGovernor` sits in the
   `IChatClient` pipeline (`DelegatingChatClient`): pre-flight estimation, enforced
   `MaxOutputTokens`, soft-pause with checkpoint, hard stop, per-minute breaker.
4. **Zero-trust execution.** Nothing chat-derived reaches host shell; Tier-3 artifacts
   run in real sandboxes; taint (`UntrustedExternal`) physically removes host tools
   from `ChatOptions.Tools`; AST screening is defense-in-depth, never the boundary.
5. **Decentralized SQLite storage.** One master registry + isolated per-subsystem WAL
   databases with per-DB keys; lazy provisioning and serialized migrations through
   `ISubsystemDatabaseManager`; global crash-recovery policy in `AppGlobalSettings`.
6. **Process isolation in depth.** In-process bulkheads/bounded channels/quotas inside
   `CyberPipe.exe`; risky native parsing (Whisper, PDF/DOCX) in the OS-isolated
   `Brainstem` worker (two pools: warm STT, single-use parsers); generative LLMs in an
   external Ollama process — three different isolation answers for three different
   risk shapes.
7. **Deterministic automation over repeated inference.** Netrunner converts recurring
   LLM work into versioned, checksummed, schema-validated ETL packages + DAGs with
   checkpoint resume and HITL gates.
8. **Prove the foundation first.** Phase 0 (bus ADR, DB registry, encryption PoC,
   isolation PoC, cache-key test, SLO methodology) gates all construction — a direct
   consequence of the audit's NOT READY verdict and [RISK-001](../03-atam/risk-register.md)
   / [RISK-002](../03-atam/risk-register.md).
