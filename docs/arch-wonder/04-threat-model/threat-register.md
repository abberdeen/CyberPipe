# Threat Model — Threat Register

**Severity scale.** Critical: host compromise / secret mass-egress. High: single-asset
breach or safety bypass. Medium: limited/contained impact. Likelihood: High/Med/Low
against the *planned* controls (most unwritten → likelihoods are conservative).
`H` = hypothesis requiring validation (no code exists to confirm or refute).

| ID | STRIDE | Threat (asset, component) | Preconditions / attack path | S × L | Controls (existing→proposed) |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `THREAT-001` | S | Telegram identity spoofing (`A-03`): forged `UserId` drives host actions | Attacker crafts updates appearing whitelisted | Critical × Low (H) | Whitelist at API-verified ID (planned) → add receiver-side revalidation + takeover runbook (`TEST-SEC-01`) |
| `THREAT-002` | E | Raw-shell breakout (`A-03`): chat-derived input reaches host shell | Guard bypass, new skill with `Process`, LLM-crafted command | Critical × Low (H) | No-shell rule + ArchUnitNET ban + sandbox-only Tier 3 → negative suite (`TEST-SEC-02`) |
| `THREAT-003` | T/E | Sandbox escape (`A-03`, `A-01`): Brainstem/sandbox reads `data/`, vault, or daemon memory | Isolation misconfig; profile-B token weaker; manifest not checked | Critical × Med (H) | AppContainer/restricted-token/bwrap/seatbelt + DACL deny + Job limits + manifest → `Tests.Isolation` (`TEST-SEC-05`) |
| `THREAT-004` | I | Secret egress to cloud (`A-01`): novel key format passes sanitizer | New provider format, model-specific token shape | High × Med (H) | Pattern+entropy redaction → adversarial corpus tests + per-release pattern review (`TEST-SEC-04`) |
| `THREAT-005` | I | Prompt-injection tool abuse (`A-03`): tainted PDF/RSS/web drives host mutation | `UntrustedExternal` reaches Tier 2/3 with host tools attached | High × Med (H) | Trust tagging + RAG filter + hard stripping + HITL writes → taint suite (`TEST-SEC-03`) |
| `THREAT-006` | I | Cache cross-role disclosure (`A-02`): Guest receives Owner's cached answer | Cache key regression, `DataVersion` not bumped | High × Low | Fixed key `(QueryHash, UserRole, DataVersion, ExpiresAtUtc)` → two-question collision test (Phase 0) |
| `THREAT-007` | D | Volume/NSFW abuse (`A-04`): 100% blast or 18+ at night | Clamp bypass (locked-PC path, homoglyph query, provider flag ignored) | High × Low (H) | `VolumeClamp` + `EnsureSafeVolumeBeforePlayback` + provider family flag + DNS filter → abuse suite (`TEST-SEC-02`) |
| `THREAT-008` | S/T | IPC impersonation (`A-01`, `A-03`): rogue UI/CLI or fake Brainstem | Pipe squat, PID reuse, nonce replay | High × Low (H) | First-instance guard + PID+single-use-nonce + role auth → spoof/replay tests (`TEST-SEC-05`) |
| `THREAT-009` | T | Netrunner package supply-chain (`A-03`): malicious `main.*` runs with host rights | Validator checks hashes but execution unconstrained | High × Med (H) | 6-file contract + SHA-256 + sandbox-only run + HITL registration → escape-negative test |
| `THREAT-010` | E | DevTool runner as code execution (`A-03`): malicious build target/hook runs free | `dotnet`/`git` allow-listed by binary name | High × Med (H) | Fixed subcommands + sandbox confinement ([ADR-0005](../05-adr/0005-devtools-confinement.md)) → malicious-build test |
| `THREAT-011` | D | Budget burn (`A-05`): runaway reasoning model or spam loop | `MaxOutputTokens` unset on one path; governor bypassed | Med × Med (H) | Governor middleware (non-bypassable) + breaker + pause → burn-rate test |
| `THREAT-012` | R/D | Silent quarantine of legit input (`A-06`): poison logic misfires on `/stop` or net errors | `finally` cleanup missing on one path | Med × Low (H) | Cancel/validation errors never increment `AttemptCount` → cancel-resilience test (`TEST-REL-04`) |
| `THREAT-013` | I | Dashboard session hijack (`A-01`): loopback/cookie assumptions fail | Non-loopback bind regression, cookie leak, OTT reuse | Med × Low (H) | Loopback-only + read-only + OTT burn + HttpOnly/Strict → bind/auth tests |
| `THREAT-014` | D | Watchdog/task pile-up freezes PC (`A-03` avail.) | Unbounded retries, parallel-fanout abuse | Med × Low (H) | Bulkheads + bounded channels + branch/step caps → fault-injection suite (`TEST-REL-03`) |

No threat is marked mitigated: mitigations are planned controls whose enforcement
tests do not exist yet. Residual risk and verification per threat are tracked in
[mitigations-and-verification.md](mitigations-and-verification.md).
