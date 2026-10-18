# arc42 §12 — Glossary

| Term | Meaning |
| :--- | :--- |
| Brainstem | Isolated worker process (`worker/CyberPipe.Brainstem`) for Whisper STT and untrusted document parsing |
| Bulkhead lane | Isolated in-process resource compartment (semaphores + bounded queues) |
| Cloud Freeze | UI toggle blocking all `RemoteCloud` calls (0$ mode) |
| Egress sanitizer | `CentralDataSanitizer`: redacts secrets/PII on outputs to cloud, logs, Telegram |
| Fast path | Tier 0/1 handling at 0 cloud tokens |
| Hard tool stripping | Physical removal of host-skill schemas from `ChatOptions.Tools` in tainted context |
| HITL | Human-in-the-loop approval gate (`HitlApprovalNode`) |
| Local / RemoteCloud | `ModelHostingType`: on-PC via Ollama/LM Studio ($0) vs paid external API |
| Netrunner | Deterministic ETL script packages + mini-n8n DAG engine with HITL |
| OTT | One-time token (Hangfire dashboard handshake, 256-bit, 30-s TTL, burn-on-use) |
| PausedByBudget | `TaskLifecycleState` checkpoint state on soft-limit hit |
| Poison-pill | Input that repeatedly crashes handlers; quarantined after `AttemptCount ≥ 2` |
| QuotaBoundedStream | Stream wrapper aborting reads beyond 16 MB |
| Tainted context | Session containing `UntrustedExternal` material |
| Tier 0–3 | Regex fast-path → ONNX semantic router+cache → light model → heavy agent |
| TrustSource / `MemoryTrustSource` | Provenance tag (`SystemPrompt` … `UntrustedExternal`) on every memory/content item |
| Vault / `SecretsVault` | `data/secrets.vault` (AES-256-GCM, OS-bound key) plus platform key storage |
| WAL | SQLite Write-Ahead Logging mode used by all DBs |
