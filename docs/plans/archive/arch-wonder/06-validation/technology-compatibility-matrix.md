# Technology Compatibility Matrix

**Evidence date:** 2026-10-18 (reconciled from 2026-10-17). **Evidence conditions:**
repository inspected directly; methodology references live-checked (HTTP 200 for
arc42, SEI ATAM, MADR — see [00-methodology.md](../00-methodology.md)); **no NuGet,
package-version, or benchmark run was performed in this task**, so no technology row
reaches *Verified* (which requires a local run) and version-specific rows stay
*Unverified*. ***Status definitions:*** Verified (ran here) · Partially verified
(vendor-documented + consistent with repo, not run) · Unverified (could not check) ·
Incompatible / N/A as marked. Fact classes used inline: **[V]** verified fact,
**[D]** vendor-documented-not-tested, **[I]** inference, **[H]** unverified hypothesis.
Recommendations are labeled as such and never presented as facts.

## Data storage

| Technology | Intended responsibility | Required capabilities | Version/runtime constraints | Evidence / known limitations | License/distribution | Security & reliability | Open questions / verification | Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: |
| `Microsoft.Data.Sqlite` + WAL + FTS5 | All 9 DBs, FTS5 memory index | WAL, FTS5, Backup API, `user_version` migrations **[D]** | .NET 10; bundled native `e_sqlite3` version TBD **[H]** | Single writer; readers don't block writers **[D]**; stock build has **no page encryption** **[D]** | SQLite public domain; package MIT **[D]** | Crash-atomic commits **[D]**; WAL must be checkpointed before backup-copy **[D]** | Confirm bundled SQLite version + FTS5 compile flags on .NET 10 | Partially verified |
| SQLCipher provider (`SQLitePCLRaw.bundle_e_sqlcipher` or equiv.) | Page encryption + `rekey` for all DBs | `Password=` open, wrong-key failure, `rekey` **[H]** | Per-OS native binaries; .NET 10 compat TBD **[H]** | None confirmed **[H]** | SQLCipher Community (BSD-style) vs commercial Zetetic — variant-dependent **[H]** | Real at-rest control **iff** PoC passes | PoC open-right/fail-wrong/rotate; license pick | Unverified |
| `Dapper` | Lightweight mapping over raw SQL | .NET 10 support **[H]** | TBD | Thin layer; no migration story of its own **[D]** | Apache-2.0 **[D]** | Neutral | Version pin check | Unverified |
| `Hangfire` + `Hangfire.Storage.SQLite` | Scheduler + `hangfire.db` | SQLite storage, read-only dashboard mode **[D]** | Core maintained **[D]**; SQLite storage is **community-maintained** — freshness unchecked **[H]** | Storage-provider lag risk on new Hangfire/SQLite versions **[I]** | LGPL-ish/free tiers vary by package **[H]** | Dashboard must stay loopback+read-only+OTT | Confirm storage package commits/releases; dashboard flag test | Unverified |

## Messaging

| Technology | Intended responsibility | Required capabilities | Version/runtime constraints | Evidence / known limitations | License/distribution | Security & reliability | Open questions / verification | Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `MassTransit` (+`MassTransit.RabbitMQ`) | Canonical bus, consumer model | Transport abstraction; InMemory; RabbitMQ AMQP **[D]** | .NET 10 compat TBD **[H]** | InMemory = volatile; AMQP transports redeliver → idempotency mandatory **[D]** | Apache-2.0 **[D]** | Secrets for RabbitMQ in vault, never in `health.db` | Pin versions; redelivery test | Partially verified |
| `MassTransit.SqlTransport.Sqlite` | Optional durable local transport (`masstransit.db`) | WAL-backed queues, exclusive DB use **[H]** | Existence/version for .NET 10 **not pinned in this task** **[H]** | If missing: custom outbox fallback (ADR amendment) | Apache-2.0 (if exists) **[H]** | Same single-writer physics as all SQLite use | NuGet existence + release check | Unverified |
| `RabbitMQ` (optional) | Enterprise broker | AMQP 5672, mgmt 15672, `cyberpipe_admin` least-privilege **[D]** | Erlang/OTP compat per server release; needs supervision skill **[D]** | Heaviest ops burden; single-user overkill (hence opt-in) **[I]** | MPL-2.0 server; client Apache-2.0 **[D]** | Least-privilege vhost perms; creds in vault | Supervision drill on Windows | Partially verified |

## Desktop / process / IPC

| Technology | Intended responsibility | Required capabilities | Version/runtime constraints | Evidence / known limitations | License/distribution | Security & reliability | Open questions / verification | Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `Avalonia` 11.x + `CommunityToolkit.Mvvm` | Desktop UI, Contracts-only | Win/Linux/macOS rendering **[D]** | 11.x+ named in plans **[V]** (plan text) | UI must add Contracts DTO per action (accepted cost) **[I]** | MIT **[D]** | No business logic in UI process | Version pin + trim/AOT posture | Partially verified |
| `Grpc.Net.Client/Grpc.AspNetCore` on Named Pipes/UDS | UI/CLI↔daemon transport | 4 MB frame caps, streaming **[D]** | Kestrel `ListenNamedPipe` availability per .NET 10 **[H]** | Pipe squat risk → first-instance guard (designed) **[I]** | Apache-2.0 **[D]** | PID/nonce/role layers above transport | Bind + flag tests | Partially verified |
| AppContainer / Restricted token + Job Object (Win) | Brainstem isolation | Net-less profile, DACL deny on `data/`, `NO_READ_UP` on daemon, `ActiveProcessLimit=1` **[D-platform]** | Win 10/11 **[V]** (plan scope) | GPU drivers may refuse AppContainer → profile B **[H]**; `NO_READ_UP` applies to process objects, not files **[D-platform]** | OS facility | Strongest-tested path (MVP focus) | Negative suite on reference builds | Partially verified |
| `bwrap` + cgroups v2 (Linux), Seatbelt (macOS) | Same on other OSes | Net-ns removal, dir non-mount, memory/pid caps **[D-platform]** | Post-MVP **[V]** (plan scope) | **Equivalence with Windows path unproven** **[H]** | OS facilities | Sandboxing claims deferred | Same negative suite per OS | Unverified |
| `Wasmtime`, `Docker.DotNet` | Tier-3 code sandboxes | WASI confinement; `--network none` containers **[D]** | TBD pins **[H]** | Wasm ≠ full OS API; Docker needs engine present **[D]** | MIT/Apache-2.0 **[D]** | Complements (not replaces) OS sandbox | Runtime spikes (ASSUMPTION-009) | Unverified |

## AI / media / dev

| Technology | Intended responsibility | Required capabilities | Version/runtime constraints | Evidence / known limitations | License/distribution | Security & reliability | Open questions / verification | Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `Microsoft.ML.OnnxRuntime`, `Whisper.net`, `System.Numerics.Tensors` | Tier-1 embeddings+SIMD; local STT | `multilingual-e5-small` ~110 MB; CPU inference 5–10 ms **[H-plan]** | Native runtime per arch **[H]** | Model-file provenance + size; cold-start cost **[I]** | ORT MIT; model/data licenses vary **[H]** | Tainted-audio text still tagged; parser pool single-use | Benchmark + model-hash pinning | Unverified |
| `Microsoft.Extensions.AI` + `OllamaSharp` + cloud SDKs + `Microsoft.ML.Tokenizers` | Tier 2/3 inference, token counting | `MaxOutputTokens` enforcement; `UsageDetails` billing; Cyrillic 1.2× factor **[H-plan]** | Ollama API drift possible **[H]** | Tokenizer mismatch per vendor; reasoning-token opacity **[I]** | Mixed MIT/Apache **[D]** | Governor non-bypassability test required | Per-vendor counting calibration | Unverified |
| `NAudio` / PipeWire / CoreAudio via `IAudioDeviceController` | Volume clamp, capture | WASAPI loopback+mic; clamp before `Play` **[H-plan]** | Session-≥1 requirement (C-014) **[V]** (plan rationale) | Locked-PC speaker path is the abuse edge **[I]** | NAudio MIT **[D]** | Clamp is the guarantee; provider flags best-effort | Abuse-case tests | Partially verified |
| `LibGit2Sharp` (+ native `git2`) | Read-only repo radar | Repo scan without shelling out **[D]** | Native binary per OS **[H]** | Native-lib shipping; large-repo scan cost **[I]** | LGPL-ish (libgit2) — redistribution review needed **[H]** | Read-only by contract; hooks only via runner path | License + perf check | Unverified |
| `UglyToad.PdfPig`, `DocumentFormat.OpenXml`, OCR (`Windows.Media.Ocr`/Tesseract/Vision) | Untrusted doc/screen text | Single-use, 512 MB cap **[H-plan]** | Parser exploits assumed possible → isolation, not hardening **[I]** | Native/CPU cost; OCR accuracy variance **[I]** | Apache-2.0/MIT-family **[D]** | Output always `UntrustedExternal` | Isolation-negative tests | Unverified |
| `Telegram.Bot` | Bot transport | Polling/webhook, files, callbacks **[D]** | Bot API drift **[H]** | Network path excluded from SLOs (ADR-0006) | MIT **[D]** | Whitelist + taint above transport | Fuzz suite | Partially verified |
| `ArchUnitNET`, `Serilog`+`OpenTelemetry`, `System.CommandLine`+`Spectre.Console` | DAG tests; audit/cost logging; CLI/TUI | Compile-time rule/assertion APIs **[D]** | TBD pins **[H]** | ArchUnitNET is necessary-not-sufficient (needs `Tests.Isolation`) **[I]** | Apache-2.0/MIT-family **[D]** | Logs post-sanitizer only | CI wiring in Sprint 1 | Partially verified |

## Hidden-constraint checklist (task §6) — what the table proves and what it does not

The per-row table above records intended use, required capabilities, version
constraints, evidence/limitations, licensing, security/reliability implications,
open questions, verification method, and status for every critical technology.
The checklist below states the hidden-constraint verdict per area so that no
advertised feature is mistaken for a verified production guarantee.

### Data storage (SQLite estate)

- Embedded (not server) deployment is the design assumption **[V]** (plan text);
  concurrent readers + single writer per file, WAL checkpoint-before-copy, and
  crash-atomic commits are vendor-documented **[D]**, not benchmarked here.
- Page encryption, `rekey`, corruption/disk-full behavior, provider-specific ORM
  limits, practical size ceilings, and backup-restore consistency are **not
  verified** — they collapse to RISK-001/RISK-007 and PoCs `TEST-DUR-01…03`.
  Until then, assume DB files are readable with direct file access.
- Migration/rollback compatibility rests on `PRAGMA user_version` serialization
  and pre-migration backups **[D-plan]**; no migration has ever run (no code).

### Messaging and background processing

- Delivery semantics are transport-dependent by design: InMemory may lose on
  restart; SQLite/RabbitMQ may redeliver **[D]**. Effectively-once is achieved
  only by idempotent consumers (`MessageDeduplicationId`), never by the bus.
- Acknowledgement, poison-message, dead-letter, ordering, and outbox/inbox
  behavior are specified in plans but **untested**; transaction boundaries
  between bus publish and DB commit remain an open design gap
  (`ASSUMPTION-003`). Bounded queues/backpressure are enforced in-process
  (`BoundedChannel`, quotas) **[D-plan]** and must be proven under load.
- Adapter existence (`MassTransit.SqlTransport.Sqlite`,
  `Hangfire.Storage.SQLite`) does not imply transaction-model compatibility;
  both stay *Unverified* until pinned and spiked (RISK-003).

### Desktop and process architecture

- Single-instance, IPC endpoint exposure, child-process permissions, sandbox
  escape boundaries, secrets exposure to local users/processes, local API
  authorization, updates/crash recovery, and per-user (no-admin) install are
  all plan-specified **[D-plan]** and post-MVP-scoped to Windows 10/11
  (ADR-0004). None has an executed negative test; equivalence across
  AppContainer/`bwrap`/Seatbelt is a hypothesis (RISK-002).
- Required native binaries, platform support, reproducible-build needs, and
  dependency end-of-life dates are recorded per row above; model-weight and
  native-lib redistribution licenses require legal review before bundling.

**Bottom line.** The stack is coherent on paper; the load-bearing unknowns are exactly
RISK-001/002/003 (+ASSUMPTION-004/005/009). Nothing above was presented as a benchmark,
test outcome, or confirmed version — the first version-pinning + PoC pass must fill
the *Unverified* cells before Sprint 1.
