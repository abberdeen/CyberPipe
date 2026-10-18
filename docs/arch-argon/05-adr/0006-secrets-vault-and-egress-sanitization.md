# ADR-0006: Hardware-Backed `SecretsVault` (`DPAPI`/`AES-256-GCM`), Hard Tool Stripping, and Egress `CentralDataSanitizer`

- **Status:** `Proposed`
- **Date:** 2026-10-18
- **Deciders:** CyberPipe Architecture Team
- **Source Plan Evidence:** [`05-security-and-zero-trust.md`](../../plans/05-security-and-zero-trust.md), [`09-data-protection-and-state-hub.md`](../../plans/09-data-protection-and-state-hub.md), [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-006`)
- **Related Requirements:** `REQ-005`, `REQ-006`
- **Related Quality Scenarios (ATAM):** [`QAS-007`](../03-atam/quality-attribute-scenarios.md#qas-007), [`QAS-008`](../03-atam/quality-attribute-scenarios.md#qas-008)
- **Related Risks & Threats:** [`RISK-014`](../03-atam/risk-register.md#risk-014), [`RISK-016`](../03-atam/risk-register.md#risk-016), [`THREAT-001`](../04-threat-model/threat-register.md#threat-001), [`THREAT-003`](../04-threat-model/threat-register.md#threat-003), [`THREAT-007`](../04-threat-model/threat-register.md#threat-007)
- **Verification Gate:** [`TEST-002`](../06-validation/assumptions-and-open-questions.md#test-002), [`TEST-005`](../06-validation/assumptions-and-open-questions.md#test-005)

---

## 1. Context and Problem Statement

A personal assistant integrating cloud LLMs (`Anthropic`, `OpenAI`, `Gemini`), Telegram, email, and financial/CRM databases faces three existential security threats:
1. **Plaintext Credential Theft (`THREAT-007`):** Storing API keys or database encryption keys in `.env` files, `appsettings.json`, or environment variables makes them trivially readable by any script, child process, or backup reader.
2. **Egress PII & Secret Leakage (`THREAT-003`):** Sending unsanitized prompts to cloud LLMs or writing unredacted exception logs leaks secrets, credit cards, SSN/INN numbers, and emails. Furthermore, systems that "rehydrate" placeholders (`[SECRET_1]` $\rightarrow$ real secret) after the LLM responds are vulnerable to prompt-injection exfiltration.
3. **Indirect Prompt Injection Tool Hijacking (`THREAT-001`):** Relying on system prompts (*"Do not call destructive tools when reading external emails"*) fails against adversarial prompt injections.
4. **Master Key Loss (`RISK-014` / `AUD-14`):** Sealing `data/vault.bin` solely with Windows `DPAPI` (`CurrentUser`) creates a single point of catastrophic data loss if the Windows user profile is corrupted or wiped.

---

## 2. Decision Drivers

- **`REQ-005` / `QAS-008`:** Zero plaintext secrets in config/env vars; `< 0.5 ms` deterministic egress scrubbing with zero secret rehydration.
- **`REQ-006` / `QAS-007`:** Structural `100%` prevention of destructive tool execution when `UntrustedExternal` data is present in the LLM context.
- **`RISK-014`:** Provide a deterministic offline recovery mechanism if the OS `DPAPI` key hive is lost.

---

## 3. Considered Options

1. **Option 1:** Environment Variables / `dotnet user-secrets` + System-Prompt Safety Instructions
2. **Option 2:** External Vault Server (HashiCorp Vault / 1Password CLI Dependency)
3. **Option 3 (Chosen):** Local `SecretsVault` (`data/vault.bin` `AES-256-GCM` + OS `DPAPI`/Keychain + Offline `Argon2id` Recovery Kit) + C# Hard Tool Stripping + One-Way Egress `CentralDataSanitizer`.

### Option Comparison Matrix

| Criterion | Option 1 (`.env` + Prompt Rules) | Option 2 (External Vault Server) | Option 3 (`vault.bin` + Hard Tool Stripping + Egress Sanitizer) |
| :--- | :--- | :--- | :--- |
| **Protection Against Child Process Env Dump** | Fails | Pass | **Pass (`ZeroMemory` scoped callbacks)** |
| **Prompt Injection Tool Immunity** | Fails (`< 50%` under red-team) | Fails | **100% Enforced in C# (`ChatOptions.Tools = Empty`)** |
| **Zero-Config Offline Operation** | Pass | Fails (External dependency) | **Pass (Built into `CyberPipe.Security`)** |
| **Survives OS User Profile Reset (`RISK-014`)** | Pass | Pass | **Pass (via Offline `Argon2id` Emergency Recovery Kit)** |

---

## 4. Decision Outcome

**Chosen Option:** **Option 3 — Hardware-Backed `SecretsVault` with `Argon2id` Offline Recovery Kit, Structural C# Hard Tool Stripping, and One-Way Egress `CentralDataSanitizer`.**

### Architectural Specification

1. **`SecretsVault` (`data/vault.bin` in `CyberPipe.Security`):**
   - Payload encrypted using `System.Security.Cryptography.AesGcm` (`256-bit` key, `96-bit` CSPRNG nonce per write, `128-bit` authentication tag).
   - The `32-byte` Master Encryption Key (MEK) is sealed at rest via Windows `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` (`DPAPI`), Linux `libsecret`, or macOS Keychain.
   - **Memory Hygiene (`THREAT-007`):** `ISecretsVault.UseSecretAsync<T>(key, callback)` decrypts individual secrets into a rented buffer and wipes the buffer inside a `finally` block using `CryptographicOperations.ZeroMemory`.
   - **Mandatory Offline Recovery Kit (`RISK-014` Fix):** During `cyberpipe init` (or `cyberpipe vault export-recovery-key`), the MEK is additionally wrapped using an Owner-supplied recovery passphrase stretched via **`Argon2id`** (`64 MB` memory cost, `4` iterations, `16-byte` random salt) + `AES-256-GCM`, allowing full restoration of `vault.bin` and all 9 `.db` files on a new machine (`CyberPipeBackupService`).
2. **Taint Contagion & Hard Tool Stripping (`QAS-007`, `THREAT-001`):**
   - Canonical enum in `CyberPipe.Contracts`: `OriginTrustLevel { Owner = 0, TrustedAutomation = 1, UntrustedExternal = 2 }` (`RISK-016` fix).
   - Whenever any item with `OriginTrustLevel.UntrustedExternal` enters `AgentExecutionContext`, `ContextContainsUntrustedData` becomes `true` for the remainder of the pipeline.
   - When `ContextContainsUntrustedData == true`, `PipelineOrchestrator` sets `ChatOptions.Tools = Array.Empty<AITool>()` in C# *before* calling `IChatClient` and discards any tool-call blocks in the model response.
3. **One-Way Egress `CentralDataSanitizer` (`QAS-008`, `THREAT-003`):**
   - Runs before every outbound cloud LLM call (`TB-03` $\rightarrow$ `TB-01`) and as a Serilog log sink filter (`DF-10`).
   - **Stage 1 (Exact & Prefix Match):** Aho-Corasick automaton loaded with all active secret values from `SecretsVault` and known API key prefixes (`sk-`, `ghp_`, `glpat-`, `xoxb-`, `AIza`, PEM headers).
   - **Stage 2 (Compiled Regex + Algorithmic Check):** Luhn-validated credit card numbers (`[PII_CARD]`), SSN/INN/Passport patterns (`[PII_ID]`), email addresses (`[PII_EMAIL_N]`), phone numbers (`[PII_PHONE_N]`), and user home directory paths (`[REDACTED_PATH]`).
   - **No Rehydration Invariant:** Placeholders are never substituted back with real secrets after LLM generation.

---

## 5. Consequences and Trade-Offs

### Positive Consequences (`+`)
- **`+` Deterministic Prompt-Injection Containment (`QAS-007`):** Even a 100% hijacked LLM cannot invoke a tool that C# has stripped from `ChatOptions.Tools`.
- **`+` Eliminates Catastrophic `DPAPI` Lockout (`RISK-014`):** The `Argon2id` recovery kit ensures backups remain restorable after hardware failure.

### Negative Consequences and Trade-Offs (`-`)
- **`-` Multi-Step Untrusted Workflows Require `HITL` (`TP-03`):** Because tainted LLM turns cannot call mutating tools directly, workflows that ingest external data and then mutate state must transition through a deterministic `Netrunner` `HitlApprovalNode`.

---

## 6. Validation and Compliance Checks

- **`TEST-005` (Red-Team Prompt Injection & Egress Sanitizer Suite):**
  1. Feed 50 adversarial prompt-injection payloads tagged `UntrustedExternal` into `PipelineOrchestrator` and assert `ChatOptions.Tools.Count == 0` and `0` tool executions occur.
  2. Benchmark `CentralDataSanitizer.SanitizeForCloud()` on a `4 KB` mixed PII/secret corpus and assert `100%` redaction recall in `p99 < 0.5 ms`.
