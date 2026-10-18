# ATAM — Quality-Attribute Scenarios

Lightweight assessment: no stakeholder workshop was held. Priorities and numbers are
analyst-proposed from plan evidence; targets marked *(proposed)* are not stakeholder
requirements. Verification methods point at tests that do not exist yet (`TEST-xxx`).

## Performance / latency (local-only; `C-013`)

- `QAS-PERF-001` Tier 0 dispatch. *Stimulus:* whitelisted text command matching a
  Regex template. *Environment:* reference HW, warm daemon. *Response:* typed skill
  invoked. *Criterion:* p95 < 1 ms handler-side. *Verify:* `TEST-PERF-01`
  (BenchmarkDotNet, `CyberPipe.Tests.Router`). *Affected by:* ADR-0006, regex-table size.
- `QAS-PERF-002` Tier 1 semantic route. *Stimulus:* paraphrased intent with typos.
  *Response:* correct intent or cache hit. *Criterion:* p95 5–15 ms including ONNX
  inference + SIMD search over 10k vectors (<3 ms search sub-budget *(proposed)*).
  *Verify:* `TEST-PERF-02`. *Affected by:* embedding model size, vector-buffer cap.
- `QAS-PERF-003` CLI round-trip. *Stimulus:* `CyberPipe status --json` against a
  running daemon (≤64 KB message). *Criterion:* p95 < 25 ms end-to-end (process start +
  pipe + serialization). *Verify:* `TEST-PERF-03`.
- `QAS-PERF-004` Priority stop. *Stimulus:* `/stop` during an active LLM stream +
  sandbox child. *Response:* stream aborted, process tree killed. *Criterion:*
  local cancel issued p95 < 50 ms (Telegram transit excluded). *Verify:* `TEST-PERF-04`.
- `QAS-PERF-005` Voice latency *(proposed target, open).* Warm-pool transcription
  p95 < 1 s for ≤30 s audio; cold parser-pool spawn p95 < 80 ms (no Whisper weights).
  *Verify:* `TEST-PERF-05`.

## Security / safety

- `QAS-SEC-001` Spoofed sender. *Stimulus:* message with non-whitelisted `UserId`.
  *Response:* drop + audit log, zero router/LLM involvement. *Criterion:* 100% of
  10k fuzz messages dropped; p95 < 0.1 ms. *Verify:* `TEST-SEC-01`.
- `QAS-SEC-002` Destructive command. *Stimulus:* wipe/format/registry/system-dir
  patterns in chat and in Tier-3-generated scripts. *Response:* block (`Blocked`) or
  sandbox-confined + approval gate. *Criterion:* 0 executions outside sandbox in
  negative suite. *Verify:* `TEST-SEC-02` (incl. homoglyph/encoding variants).
- `QAS-SEC-003` Tainted context. *Stimulus:* task containing `UntrustedExternal`
  material requesting a host-mutating action. *Response:* host tools absent from
  `ChatOptions.Tools`; only read-only analysis or Owner-confirmed proposal.
  *Criterion:* 0 host mutations without explicit approval in suite. *Verify:* `TEST-SEC-03`.
- `QAS-SEC-004` Secret egress. *Stimulus:* vault keys, tokens, PII in LLM context.
  *Response:* `[REDACTED:ID]` at every egress (cloud prompt, log, Telegram).
  *Criterion:* 0 plaintext secrets in captured egress fixtures *(target; pattern
  coverage reviewed per release)*. *Verify:* `TEST-SEC-04`.
- `QAS-SEC-005` Sandbox escape attempt. *Stimulus:* worker tries `OpenProcess` on
  daemon, reads `data/*.db`/`secrets.vault`, spawns `cmd.exe`. *Response:*
  `ACCESS_DENIED` / `UnauthorizedAccessException` / spawn blocked. *Criterion:* all
  negative tests pass on every release OS. *Verify:* `TEST-SEC-05` (`Tests.Isolation`).

## Reliability / recoverability

- `QAS-REL-001` Daemon survives worker crash. *Stimulus:* `AccessViolation`/OOM in
  Brainstem. *Response:* daemon alive, `AttemptCount` incremented, next request
  servable. *Criterion:* recovery < 200 ms; no host crash. *Verify:* `TEST-REL-01`.
- `QAS-REL-002` Power loss mid-DAG. *Stimulus:* kill at step 15/20. *Response:* per
  `Pipeline:CrashRecoveryPolicy` — resume from step 15 *or* steps 1–14 intact +
  `CancelledByCrash` on run and step 15. *Criterion:* 0 completed steps lost or
  re-executed with external effect. *Verify:* `TEST-REL-02`.
- `QAS-REL-003` Single worker failure contained. *Stimulus:* 3 consecutive failures
  in one watchdog within 1 min. *Response:* quarantine + Owner notice; neighbors
  unaffected. *Criterion:* no cross-lane impact in fault-injection suite.
  *Verify:* `TEST-REL-03`.
- `QAS-REL-004` Cancel is never poison. *Stimulus:* `/stop` during a heavy task.
  *Response:* `InFlightOperations` row removed, no `AttemptCount` increment.
  *Criterion:* re-issuing the same input is never quarantined. *Verify:* `TEST-REL-04`.

## Durability / integrity

- `QAS-DUR-001` Backup restores. *Stimulus:* `backup sqlite restore` from a
  `verify`-passed backup. *Response:* all 9 DBs consistent, registry intact.
  *Criterion:* restore test green before release (K.3 gate). *Verify:* `TEST-DUR-01`.
- `QAS-DUR-002` Key rotation without loss. *Stimulus:* rotate one subsystem DB key
  (`rekey`). *Response:* data intact, registry updated. *Criterion:* training rotation
  passes. *Verify:* `TEST-DUR-02`.
- `QAS-DUR-003` Master-key loss fails closed. *Stimulus:* vault unopenable.
  *Response:* execution halts with guidance (restore + re-onboard); no unprotected
  continuation. *Verify:* `TEST-DUR-03`.

## Modifiability / operability

- `QAS-MOD-001` DAG enforcement. *Stimulus:* any commit adding a forbidden reference
  (UI→Core, Skills→Process, Brainstem→daemon assemblies). *Response:* CI fails.
  *Criterion:* 100% of reference violations caught pre-merge. *Verify:* `TEST-MOD-01`.
- `QAS-OPS-001` Cold start simplicity *(proposed).* Fresh-machine first run needs no
  broker/server install; daemon up < 1 s *(proposed, needs benchmark)*.
  *Verify:* `TEST-OPS-01`.
