# arc42 §10 — Quality Requirements

Part of the [arc42 documentation](01-introduction-and-goals.md). Measurable scenarios live
in [03-atam/quality-attribute-scenarios.md](../../03-atam/quality-attribute-scenarios.md).
This section is the quality tree + summary.

---

## 10.1 Quality tree

```
CyberPipe quality
├── Performance & Latency
│   ├── Tier-0 routing < 1 ms (host-internal, p95)
│   ├── Tier-1 ONNX routing 5–15 ms (host-internal, p95)
│   ├── /stop kill < 50 ms (host-internal, p95)
│   ├── CLI round-trip p95 < 25 ms
│   └── Brainstem cold start p95 < 80 ms (no Whisper weights)
├── Token & Cost Efficiency
│   ├── 70–85% of daily requests at 0 cloud tokens
│   ├── Per-task soft/hard token limits (4 000 / 12 000)
│   ├── Daily USD cap (default $0.50)
│   └── Velocity circuit breaker (15 000 tokens/min)
├── Security & Privacy
│   ├── No raw shell from chat
│   ├── Anti-Wipe + Volume Clamp + NSFW filter
│   ├── Secret egress sanitization (no blind rehydration)
│   ├── OS-sandboxed Tier-3 execution
│   ├── Hard Tool Stripping in TaintedContext
│   └── Per-DB vault keys + (PoC-pending) SQLCipher at rest
├── Reliability & Recoverability
│   ├── WAL + step checkpointing → no data loss on crash/power loss
│   ├── Crash recovery policy (Resume / CancelledByCrash)
│   ├── IsolatedWorkerBase quarantine (no cascade)
│   └── Tested backup/restore runbook
├── Modifiability & Testability
│   ├── Strict acyclic project DAG (ArchUnitNET)
│   ├── UI depends only on Contracts
│   ├── Skills never call Process directly
│   └── No unbounded channels
├── Availability (local-only)
│   ├── Daemon stays up on worker failure (Ignore + quarantine)
│   ├── Cloud Freeze mode keeps local models working
│   └── Fail-closed on guard/vault/sandbox unavailability
├── Compatibility & Portability
│   ├── Windows 10/11 MVP; Linux/macOS later (OS contracts)
│   ├── Cross-platform via CyberPipe.Contracts OS interfaces
│   └── Local vs RemoteCloud per tier (user-selectable)
├── Operational Simplicity
│   ├── Zero-dependency startup (InMemory default)
│   ├── No admin elevation required
│   └── Single host, no orchestration
└── Data Durability & Integrity
    ├── WAL atomicity; BusyTimeout=5000
    ├── Per-subsystem DB isolation
    ├── SHA-256 manifest verification (worker, Netrunner packages)
    └── Per-step SHA-256 of artifacts
```

## 10.2 Quality requirements (most important)

Numbered as `QAS-xxx` in [03-atam/quality-attribute-scenarios.md](../../03-atam/quality-attribute-scenarios.md).
Top-priority scenarios:

| ID | Quality | Scenario (summary) | Status |
| :--- | :--- | :--- | :--- |
| QAS-001 | Latency | `/stop` cancels active LLM stream within 50 ms (host-internal, p95) | Proposed target (`AUD-06`) |
| QAS-002 | Token cost | 70–85% of daily Telegram requests resolve at 0 cloud tokens | Proposed target |
| QAS-003 | Security | Chat-driven command can never invoke raw `cmd`/`powershell`/`bash` | Designed; enforced by `ArchUnitNET` + compiled-skill-only API |
| QAS-004 | Security | Tier-3 generated script cannot read `data/` or spawn child processes | Designed; **PoC-pending** (`AUD-13`) |
| QAS-005 | Recoverability | After power loss, Netrunner workflow resumes from last checkpoint (or marks `CancelledByCrash`) without losing prior step outputs | Designed |
| QAS-006 | Reliability | Worker AV/OOM does not crash daemon; next request served within 200 ms | Designed (`AUD-07` Poison-Pill rules) |
| QAS-007 | Confidentiality | Cloud LLM never receives raw secrets/PII | Designed; egress sanitizer |
| QAS-008 | Integrity | `worker.manifest.json` mismatch blocks Brainstem spawn (ExitCode 3) | Designed |
| QAS-009 | Modifiability | Adding a UI action requires only a new DTO in Contracts (no Core/Skills dep) | Designed; enforced by ArchUnitNET |
| QAS-010 | Availability | If cloud LLM is blocked/unavailable, local models on T2/T3 keep working ("Cloud Freeze") | Designed |

## 10.3 Marked inapplicable quality attributes

- **Scalability (horizontal)**: N/A; single-user single-host. Vertical scaling limited by
  host RAM/VRAM (`ISystemResourceMonitor`).
- **Geo-redundancy**: N/A; local-only product.
- **Multi-tenant isolation**: N/A; single owner + household guest role.
