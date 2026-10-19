# CyberPipe.Router

`CyberPipe.Router` provides multi-tier request classification and intent routing for CyberPipe.

## Architecture

Routing operates as an escalating cascade:
- **Tier 0 (`Tier0RegexRouter`)**: Instant, zero-cost, deterministic regular expression matching with backtracking timeouts.
- **Tier 1**: Semantic router using local embeddings (`multilingual-e5-small`) and pgvector semantic cache.
- **Tier 2 / Tier 3**: LLM-based routing using light and heavy models governed by `TokenBudgetGovernor`.

## Tier 0 Features

- **Normalization**: Trims leading/trailing whitespace and collapses multiple consecutive spaces.
- **Priority-Driven Matching**: Evaluates rules ordered by priority descending.
- **Named Group Extraction**: Automatically populates `Parameters` with named capturing groups.
- **ReDoS Protection**: Enforces per-rule execution timeouts (`RegexMatchTimeoutException`) to prevent catastrophic backtracking.
- **Pure In-Memory**: No database, network, or AI model dependencies.
