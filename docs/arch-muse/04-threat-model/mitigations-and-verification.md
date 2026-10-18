# Threat Model — Mitigations and Verification

Each row ties a threat to the control that must be *enforced* (not merely present),
how enforcement is proven, and what risk remains until then.

| Threat | Enforced control (specific) | Verification | Residual risk / owner |
| :--- | :--- | :--- | :--- |
| `THREAT-001` | Whitelist comparison against API-authenticated `UserId`; Guest capability ceiling | `TEST-SEC-01`: 10k-message fuzz incl. group/callback spoofs | Takeover procedure missing → Owner documents runbook pre-release |
| `THREAT-002` | Compile-time: no `Process`/CliWrap in `Skills.Host`; runtime: sandbox-or-approval for Tier-3 artifacts | `TEST-SEC-02` + ArchUnitNET rule | New-skill review checklist must include the ban |
| `THREAT-003` | OS-profile check at spawn + DACL/mount/Seatbelt denial of `data/` + manifest hash + Job/cgroup caps | `TEST-SEC-05` per release OS | Profile-B audit depth; Linux/macOS parity open until PoC |
| `THREAT-004` | Egress fixtures captured at 3 sinks (cloud prompt, log, Telegram) with 0-plaintext assertion | `TEST-SEC-04` adversarial corpus | Zero-day formats; accept + monitor, review patterns each release |
| `THREAT-005` | `ChatOptions.Tools` membership assertion in tainted sessions; RAG trust-filter unit tests | `TEST-SEC-03` | Model-output-as-instruction drift; HITL is the backstop |
| `THREAT-006` | Cache-key unit test: two questions × two roles × version bump → 4 distinct entries | Phase-0 test (roadmap K.1) | `DataVersion` bump discipline on memory-schema change |
| `THREAT-007` | Clamp assertion before every `Play/Resume` incl. locked-PC path; homoglyph suite | `TEST-SEC-02` media cases | Provider-side flag is best-effort; clamp is the guarantee |
| `THREAT-008` | Squat/replay test clients in `Tests.Isolation`; role-auth matrix for control-plane commands | `TEST-SEC-05` IPC cases | PID-reuse windows; nonce TTL + handshake timeout bound them |
| `THREAT-009` | Runner asserts sandbox confinement + workspace-only FS scope before exec | Sandbox-negative test | Reviewer must still eyeball `permissions` on HITL card |
| `THREAT-010` | Subcommand whitelist test + in-sandbox execution proof for build tasks | Malicious-target fixture test | Build-tool network/sample needs remain per-task decisions |
| `THREAT-011` | Governor placement test (no `IChatClient` path bypasses middleware) + breaker drill | Burn-rate test | Pricing drift; refresh tariffs with catalog (`DEBT-004`) |
| `THREAT-012` | Cancel-during-flight test asserts no quarantine; orphan-crash test asserts quarantine | `TEST-REL-04`, `TEST-REL-01` | Handler-thrown `CorruptedPayloadException` needs careful taxonomy |
| `THREAT-013` | Bind-address assertion + OTT-reuse rejection + read-only mutation attempt tests | Dashboard auth tests | Browser-history URL exposure; mitigated by burn-on-use + 30-s TTL |
| `THREAT-014` | Lane-saturation drill: one misbehaving worker, neighbors meet SLOs | `TEST-REL-03` | User-created cron storms; per-user task quotas recommended (open) |

**Global rule.** A threat graduates from *hypothesis* to *mitigated* only when its
verification test exists and passes on the release OS. Until then every control above
is a design intent, and the register in `threat-register.md` stays the source of truth.
