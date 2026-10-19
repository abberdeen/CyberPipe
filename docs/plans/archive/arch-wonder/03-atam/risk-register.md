# ATAM — Risk Register

**Scale.** Severity: Critical (blocks release / breaks core promise), High (major
rework or safety gap), Medium (scoped rework), Low (polish). Likelihood: High /
Medium / Low given current evidence. Prefix `RISK-` links scenarios (`QAS-xxx`),
threats (`THREAT-xxx`), and validation (`TEST-xxx`).

| ID | Risk | S × L | Evidence / status | Mitigation / validation |
| :--- | :--- | :---: | :--- | :--- |
| `RISK-001` | SQLite at-rest encryption unproven: stock `Microsoft.Data.Sqlite` ignores `Password=`; SQLCipher PoC not run | Critical × High | `17-adr-mvp.md` ADR-003 declares hypothesis; repo has no code | PoC: open with right key / fail with wrong key (`TEST-DUR-02`); until then assume disk-readable |
| `RISK-002` | AppContainer / bwrap / Seatbelt presented as equivalent but never jointly tested | High × High | `15` §3 claims profiles; no tests exist | Per-OS negative suite (`TEST-SEC-05`); document divergences |
| `RISK-003` | `MassTransit.SqlTransport.Sqlite` existence/version and `Hangfire.Storage.SQLite` maintenance unverified (not pinned in this task) | High × Medium | Package names only in plans | Verify on NuGet + release history; pin versions; fallback plan — compat matrix |
| `RISK-004` | `dotnet`/`git` via name allow-list = unsandboxed code execution (build scripts, git hooks) | High × Medium | `06`/`03` describe runner; sandboxing asserted in `17` L5 | Confine in sandbox + fixed subcommand list; malicious-build test (`TEST-SEC-02`) |
| `RISK-005` | Egress sanitizer misses novel secret formats → silent plaintext egress | High × Medium | Pattern+entropy design, no corpus test | Adversarial fixture suite (`TEST-SEC-04`); fail-closed review per release |
| `RISK-006` | Local-model/ONNX outage collapses all traffic to billable tiers with no degraded mode | High × Medium | Fallback behavior unspecified | Define degraded mode (refuse-vs-cloud with consent); monitor + `Cloud Freeze` interplay |
| `RISK-007` | SQLite single-writer contention across bus/scheduler/skills untested at load | High × Low | Write-behind asserted, no benchmarks | Contention benchmarks; writer-discipline doc; backup-consistency test |
| `RISK-008` | IPC handshake (PID+nonce) has no written negative tests yet | High × Low | Protocol specified in `15` §4 | Spoof/replay/endpoint-squat tests (`TEST-SEC-05`) |
| `RISK-009` | Curated model catalog + cloud prices rot; KV-cache formula drifts from Ollama behavior | Medium × High | Static tables in `13` | Refresh trigger per release; `models scan` self-reports |
| `RISK-010` | Telegram as control plane: account takeover = host control; no number-change/revoke story | High × Low | Whitelist-only design | Document key-hygiene procedure; Guest minimization; revoke runbook (open — ASSUMPTION-008) |

**Non-risks (evidence-backed).** Cache-key collision class fixed in-plan
(`QueryHash` in key); SLO measurability fixed by local-vs-end-to-end split (`L6`);
bus/DB naming contradictions resolved to canonical forms (`17-adr-mvp.md`).
Each still needs its Phase-0 test to graduate from "resolved on paper".
