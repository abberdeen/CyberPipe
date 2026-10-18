# Threat Model — Data Flows

```mermaid
flowchart TD
    TG["Telegram update (text/voice/file/callback)"] --> AG["TB-01: TelegramAuthGuard"]
    AG -->|voice| STT["Brainstem Whisper pool (TB-03)"]
    AG -->|text| T0["Tier 0/1 router (local)"]
    STT --> T0
    T0 -->|doc/file| PAR["Brainstem single-use parser (TB-03)"]
    PAR -->|tainted text| TAG["TrustSource=UntrustedExternal (TB-04)"]
    T0 --> G1["Host&Media guard + SecurityGuard screen"]
    G1 --> BG["BudgetGovernor"]
    BG -->|RemoteCloud| SAN["TB-02: Egress sanitizer"]
    SAN --> LLM["Cloud LLM"]
    LLM -->|untrusted output| TAG
    TAG --> STRIP["Hard tool stripping"]
    STRIP --> SB["Sandbox execution (TB-06)"]
    SB --> DIG["Telemetry digest → audit"]
    G1 --> SK["Typed skill → SQLite/vault (TB-03 ACLs)"]
    UI["UI/CLI"] -->|TB-05 IPC| D["Daemon"]
    D --> HB["health.db → GlobalStateHub → UI"]
```

## Storage locations of sensitive data

| Data | Location | Protection (planned) |
| :--- | :--- | :--- |
| Per-subsystem DB keys | `data/secrets.vault` (AES-256-GCM, OS-bound master) | ACL-deny Brainstem; `ZeroMemory`; rotation story |
| 8 subsystem DBs + master | `data/*.db`, `data/subsystems/*.db` (WAL) | SQLCipher **iff PoC passes**; OS ACLs meanwhile |
| Checkpoints, DAG snapshots | `budget.db`, `netrunner.db` | Same as above; no secrets in plaintext columns by policy |
| Session/nonces | Process memory only (OTT hash, 30-s TTL; sessions 4 h) | Burn-on-use; never logged |
| Logs, digests | Local log files + Telegram/audit surfaces | Post-sanitizer only |
| Run artifacts | `data/netrunner-artifacts/{RunId}/` | Per-run isolation; quota 128 MB; GC on completion |

## Entry points

Telegram updates (incl. voice/files/callbacks), UI/CLI IPC commands, Hangfire cron +
watchdog events, dashboard login URL, Netrunner package registration (6-file validator +
sandbox dry-run + HITL gate), model-catalog selection (endpoint/keys into vault).
